namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private bool? _researchCountersPresent;

    private bool TryForecastDailySharedCounters(ref long work, out IReadOnlyList<CustomCounterChange> counters)
    {
        var supported = ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast);
        if (!supported && work <= 1_000_000)
            supported = ResearchPreflight.TryForecastDailyWorldStocks(ref work, out forecast);
        counters = supported ? forecast.CounterChanges : [];
        return supported;
    }

    private string? PreflightDailyResearchCounters(ref long work)
    {
        const string rangeReason = "Research custom counters exceed the supported range.";
        const string workReason = "Research custom counters exceed the supported work limit.";
        if (_researchCountersPresent is null)
        {
            var present = false;
            foreach (var entry in _content.RuntimeRules.Research.Rules)
            {
                if (++work > 1_000_000) return workReason;
                if (entry.Value.IncreaseCounters.Count == 0 && entry.Value.DecreaseCounters.Count == 0) continue;
                present = true;
                break;
            }
            _researchCountersPresent = present;
        }
        if (_researchCountersPresent == false) return null;
        var finishingBases = 0;
        foreach (var owner in _bases)
        {
            if (++work > 1_000_000) return workReason;
            var finishes = false;
            foreach (var project in owner.Research)
            {
                if (++work > 1_000_000) return workReason;
                finishes |= (long)project.Spent + project.Assigned >= project.Cost;
            }
            if (finishes) finishingBases++;
        }
        if (finishingBases == 0) return null;
        Dictionary<string, long> projected = new(StringComparer.Ordinal);
        if (TryForecastDailySharedCounters(ref work, out var counters))
        {
            foreach (var change in counters)
            {
                if (++work > 1_000_000) return workReason;
                if (change.Name.Length == 0) continue;
                var current = projected.GetValueOrDefault(change.Name, _nextIds.GetValueOrDefault(change.Name, 1));
                var next = change.Increase ? current + 1 : Math.Max(1, current - 1);
                if (next > int.MaxValue) return rangeReason;
                projected[change.Name] = next;
            }
            return null;
        }
        if (work > 1_000_000) return workReason;
        // Unknown reward choices can contribute an increase in any finishing
        // base. Do not credit decreases or cancellation from an unselected path.
        var finishing = FinishingResearchTopics();
        var possible = new HashSet<string>(finishing, StringComparer.Ordinal);
        foreach (var id in finishing)
        {
            var rule = _content.RuntimeRules.Research[_content.RuntimeRules.Research.GetRequired(id)].Value;
            work += 1L + rule.GetOneFree.Count + rule.GetOneFreeProtected.Count;
            if (work > 1_000_000) return workReason;
            possible.UnionWith(rule.GetOneFree);
            foreach (var group in rule.GetOneFreeProtected)
            {
                work += group.Topics.Count;
                if (work > 1_000_000) return workReason;
                possible.UnionWith(group.Topics);
            }
        }
        // Lookup/automatic discoveries call addFinishedResearch, which does
        // not execute primary counter effects.
        foreach (var id in possible)
        {
            if (!_content.RuntimeRules.Research.TryGet(id, out var handle)) continue;
            foreach (var name in _content.RuntimeRules.Research[handle].Value.IncreaseCounters)
            {
                work += finishingBases;
                if (work > 1_000_000) return workReason;
                if (name.Length == 0) continue;
                var next = projected.GetValueOrDefault(name, _nextIds.GetValueOrDefault(name, 1)) + finishingBases;
                if (next > int.MaxValue) return rangeReason;
                projected[name] = next;
            }
        }
        return null;
    }
}
