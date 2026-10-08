using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Gameplay-only seam: callers already hold the campaign transaction gate.</summary>
internal interface ICampaignEventScheduling
{
    bool Schedule(string ruleId);
}

internal sealed partial class CampaignWorld : ICampaignEventScheduling
{
    private const int MaximumScheduledEvents = 100_000;
    private const string EventTimerReason = "Strategic event timer exceeds the supported range.";
    private const string EventCapacityReason = "Strategic event population or generation count exceeds the supported range.";
    private const string EventPayloadReason = "Strategic event consequences require campaign event execution (Phase 6 branch 6).";
    private readonly Dictionary<string, int> _generatedEvents = new(StringComparer.Ordinal);

    private void RegisterEventScheduling(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignEventScheduling>(this);
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldEvents,
            "strategic event countdowns", AdvanceEvents);
    }

    // SavedGame::spawnEvent records history immediately, even for events later interrupted.
    bool ICampaignEventScheduling.Schedule(string ruleId)
    {
        if (!campaign.Content.RuntimeRules.Events.TryGet(ruleId, out var handle)) return false;
        var rule = campaign.Content.RuntimeRules.Events[handle].Value;
        if (!WorldEventTiming.IsValid(rule.Timer, rule.TimerRandom)) throw new InvalidOperationException(EventTimerReason);
        if (_events.Count >= MaximumScheduledEvents || _generatedEvents.GetValueOrDefault(ruleId) == int.MaxValue)
            throw new InvalidOperationException(EventCapacityReason);
        var countdown = WorldEventTiming.SpawnCountdown(rule.Timer, rule.TimerRandom, campaign.Random);
        var generation = checked(_generatedEvents.GetValueOrDefault(ruleId) + 1);
        _events.Add(new GeoscapeEventSnapshot(ruleId, countdown)
        {
            PreservationKey = $"event:generated:{ruleId}:{generation}",
        });
        _generatedEvents[ruleId] = generation;
        return true;
    }

    private void ValidateEventHistory()
    {
        if (_events.Count > MaximumScheduledEvents || _generatedEvents.Count > MaximumScheduledEvents)
            throw new InvalidDataException(EventCapacityReason);
        foreach (var pair in _generatedEvents)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)
                throw new InvalidDataException("Generated event history contains an invalid name or count.");
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scheduled in _events)
        {
            var key = scheduled.PreservationKey;
            if (key.Length == 0) continue;
            if (!keys.Add(key)) throw new InvalidDataException("Duplicate scheduled event preservation keys.");
            var prefix = $"event:generated:{scheduled.RuleId}:";
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                (!int.TryParse(key.AsSpan(prefix.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var generation) ||
                    generation <= 0 || generation > _generatedEvents.GetValueOrDefault(scheduled.RuleId)))
                throw new InvalidDataException("Scheduled event identity exceeds its generation history.");
        }
    }

    private bool IsEventInterrupted(RuntimeEventRule rule) =>
        rule.InterruptResearch.Length != 0 && campaign.HasCompletedResearch(rule.InterruptResearch);

    private string? EventSchedulingReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        foreach (var scheduled in _events)
        {
            var rule = rules.Events[rules.Events.GetRequired(scheduled.RuleId)].Value;
            if (scheduled.Over || scheduled.SpawnCountdown <= 30)
            {
                if (rule.InterruptResearch.Length == 0) return EventPayloadReason;
                if (_pass.SiteResearchForecast is { } sites)
                {
                    // Countdown payloads follow every site. A grant can also disable
                    // earlier research, so never fall back to its pre-site completion.
                    if (!sites.GuaranteedResearch.Contains(rule.InterruptResearch)) return EventPayloadReason;
                }
                else if (highest >= CampaignTimeTrigger.OneDay)
                {
                    if (!GuaranteedDailyResearch().Contains(rule.InterruptResearch)) return EventPayloadReason;
                }
                else if (!IsEventInterrupted(rule)) return EventPayloadReason;
            }
        }

        // Bound all reachable producer choices before any current-tick mutation or RNG.
        // A day's research side effects are deduplicated per topic per base. Hourly fallback
        // production acquires available staff before the base's deferred removal pass.
        long maximumNew = 0;
        foreach (var owner in campaign.BaseStates)
        {
            if (highest >= CampaignTimeTrigger.OneDay && owner.Research.Any(project =>
                    (long)project.Spent + project.Assigned >= project.Cost))
            {
                foreach (var research in ResearchEventCandidates(owner))
                {
                    if (CheckEventProducer(research.SpawnedEvent, 1, ref maximumNew) is { } reason) return reason;
                    foreach (var pair in research.Events)
                        if (pair.Value > 0 && CheckEventProducer(pair.Key, 1, ref maximumNew) is { } weightedReason) return weightedReason;
                }
            }
            if (highest < CampaignTimeTrigger.OneHour) continue;
            var engineers = (long)owner.Engineers;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Kind == CampaignTransferKind.Engineer)
                    engineers += transfer.Quantity;
            foreach (var production in owner.Productions) engineers += production.Assigned;
            foreach (var production in owner.Productions)
            {
                var rule = rules.Manufacture[production.Rule].Value;
                if (rule.Time <= 0) continue; // Existing production semantics make no units for a zero-time rule.
                var work = production.IsFallback ? engineers : production.Assigned;
                var count = ((long)production.Spent + work) / rule.Time - production.Spent / rule.Time;
                if (!production.Infinite) count = Math.Min(count, (long)production.Amount - production.Spent / rule.Time);
                if (count <= 0) continue;
                foreach (var pair in rule.Events)
                    if (pair.Value > 0 && CheckEventProducer(pair.Key, count, ref maximumNew) is { } reason) return reason;
            }
        }
        // Each event choice above is counted, intentionally bounding every possible outcome.
        // This only restricts near-limit workloads; ordinary workloads need no staging or rollback.
        if ((long)_events.Count + maximumNew > MaximumScheduledEvents ||
            (long)_generatedEvents.Count + maximumNew > MaximumScheduledEvents) return EventCapacityReason;
        if (maximumNew != 0)
            foreach (var count in _generatedEvents.Values)
                if ((long)count + maximumNew > int.MaxValue) return EventCapacityReason;
        return null;
    }

    private string? CheckEventProducer(string id, long count, ref long maximumNew)
    {
        var rules = campaign.Content.RuntimeRules;
        if (!rules.Events.TryGet(id, out var handle)) return null;
        var rule = rules.Events[handle].Value;
        if (!WorldEventTiming.IsValid(rule.Timer, rule.TimerRandom)) return EventTimerReason;
        maximumNew += count;
        return null;
    }

    private IEnumerable<RuntimeResearchRule> ResearchEventCandidates(CampaignState.BaseState owner)
    {
        var rules = campaign.Content.RuntimeRules.Research;
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in owner.Research)
        {
            if ((long)project.Spent + project.Assigned < project.Cost) continue;
            candidates.Add(rules.GetExternalId(project.Rule));
            var rule = rules[project.Rule].Value;
            candidates.UnionWith(rule.GetOneFree);
            // Earlier discoveries can change protected eligibility in this same daily handler.
            foreach (var group in rule.GetOneFreeProtected) candidates.UnionWith(group.Topics);
        }
        // The research caller applies side effects only to completed projects and their chosen
        // getOneFree topics, once per rule per base, in catalog order.
        foreach (var rule in rules.Rules)
            if (candidates.Contains(rule.Id)) yield return rule.Value;
    }

    private void AdvanceEvents(CampaignState.TimeEffects _)
    {
        for (var index = _events.Count - 1; index >= 0; index--)
        {
            var scheduled = WorldEventTiming.Think(_events[index]);
            if (scheduled.Over)
            {
                var rule = campaign.Content.RuntimeRules.Events[
                    campaign.Content.RuntimeRules.Events.GetRequired(scheduled.RuleId)].Value;
                if (!IsEventInterrupted(rule)) throw new InvalidOperationException(EventPayloadReason);
                _events.RemoveAt(index);
            }
            else _events[index] = scheduled;
        }
    }
}
