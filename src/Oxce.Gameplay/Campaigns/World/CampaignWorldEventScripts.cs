using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Monthly subsystem seam. The caller supplies score/funds after monthly eligibility calculations.</summary>
internal interface ICampaignWorldEventScripts
{
    CampaignCommandResult Schedule(int score, long funds);
}

/// <summary>Internal fixture entry point; does not advance a month or enable campaign month rollover.</summary>
internal sealed record ScheduleMonthlyWorldEvents(int Score, long Funds) : ICampaignCommand;

internal sealed partial class CampaignWorld : ICampaignWorldEventScripts
{
    private void RegisterEventScripts(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldEventScripts>(this);
        registry.Command<ScheduleMonthlyWorldEvents>(command =>
            ((ICampaignWorldEventScripts)this).Schedule(command.Score, command.Funds));
    }

    CampaignCommandResult ICampaignWorldEventScripts.Schedule(int score, long funds)
    {
        var state = campaign.WorldScriptContext(score, funds, _strategy.MissionRuns);
        var eligible = campaign.Content.RuntimeRules.EventScripts.Rules
            .Where(entry => WorldScriptEligibility.Matches(entry.Value.Criteria, state)).ToArray();
        if (eligible.Length > 4096) return Blocked("Event scripts exceed the supported work limit.");
        // Preflight every possible choice before the first execution-odds roll or mutation.
        if (EventScriptReason(eligible, state.Month) is { } reason) return Blocked(reason);
        return RunEventScriptCommands(eligible, state.Month);
    }

    private CampaignCommandResult RunEventScriptCommands(RuntimeRule<RuntimeEventScriptRule>[] eligible, int month)
    {
        var relevant = eligible.Where(entry => Percent(entry.Value.Criteria.ExecutionOdds)).ToArray();
        foreach (var entry in relevant)
        {
            var selected = WorldEventScriptSelection.Select(entry.Value, month, _generatedEvents, campaign.Random);
            foreach (var choice in selected) ((ICampaignEventScheduling)this).Schedule(choice.Id);
        }
        ValidateEventHistory();
        return new CampaignCommandResult([]);
    }

    private string? EventScriptReason(IReadOnlyList<RuntimeRule<RuntimeEventScriptRule>> eligible, int month)
    {
        long maximumNew = 0;
        var potentialIds = new HashSet<string>(StringComparer.Ordinal);
        var scriptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in eligible)
        {
            var rule = entry.Value;
            if (rule.Criteria.ExecutionOdds <= 0) continue;
            scriptIds.Clear();
            var sequential = false;
            foreach (var choice in rule.Sequential)
            {
                if (_generatedEvents.ContainsKey(choice.Id)) continue;
                if (Check(choice, required: true) is { } reason) return reason;
                sequential = true;
                // Only an earlier script can suppress this candidate during this pass.
                // This script selects all three paths before it spawns any events.
                if (!potentialIds.Contains(choice.Id)) break;
            }
            if (sequential) maximumNew++;
            var random = rule.Random.Where(choice => !_generatedEvents.ContainsKey(choice.Rule.Id)).ToArray();
            ValidateWeights(random);
            foreach (var choice in random)
                if (Check(choice.Rule, required: true) is { } reason) return reason;
            if (random.Length != 0) maximumNew++;
            var repeated = WorldScriptSelection.ForMonth(rule.Events, unchecked((ulong)month), allowEmptyTimeline: true);
            ValidateWeights(repeated);
            foreach (var choice in repeated)
                if (Check(choice.Rule, required: false) is { } reason) return reason;
            if (repeated.Any(choice => choice.Rule.IsResolved)) maximumNew++;
            potentialIds.UnionWith(scriptIds);
        }
        if (_events.Count + maximumNew > MaximumScheduledEvents ||
            _generatedEvents.Count + potentialIds.Count(id => !_generatedEvents.ContainsKey(id)) > MaximumScheduledEvents)
            return EventCapacityReason;
        foreach (var id in potentialIds)
            if (_generatedEvents.GetValueOrDefault(id) + maximumNew > int.MaxValue) return EventCapacityReason;
        return null;

        string? Check(RuntimeRuleReference<EventRuleFamily> choice, bool required)
        {
            if (choice.Handle is not { } handle)
                return required ? $"Required event '{choice.Id}' does not exist." : null;
            scriptIds.Add(choice.Id);
            var rule = campaign.Content.RuntimeRules.Events[handle].Value;
            return WorldEventTiming.IsValid(rule.Timer, rule.TimerRandom) ? null : EventTimerReason;
        }
    }

    private static void ValidateWeights(IReadOnlyList<RuntimeScriptWeight<EventRuleFamily>> choices)
    {
        ulong total = 0;
        foreach (var choice in choices)
        {
            if (choice.Weight > (ulong)int.MaxValue - total)
                throw new InvalidDataException("Strategic script weights exceed the supported range.");
            total += choice.Weight;
        }
    }
}
