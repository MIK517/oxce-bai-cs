namespace Oxce.Gameplay.Campaigns;

/// <summary>A SavedGame::increase/decreaseCustomCounter call in reference order.</summary>
internal readonly record struct CustomCounterChange(string Name, bool Increase);

/// <summary>A certain or possible counter change preceding a later world allocation.</summary>
internal readonly record struct CustomCounterForecast(CustomCounterChange Change, bool Guaranteed);

/// <summary>Gameplay-owned custom counters stored in the save's ids mapping.</summary>
internal interface ICampaignCustomCounters
{
    string? CapacityReason(IReadOnlyList<CustomCounterChange> changes, CampaignTimeTrigger highest);
    void Apply(CustomCounterChange change);
    (long Minimum, long Maximum) BeforeWorldCreation(string name, CampaignTimeTrigger highest,
        IReadOnlyList<CustomCounterForecast>? preceding = null);
}

internal static class CustomCounterValue
{
    internal static int Apply(int? current, bool increase) => increase
        ? checked((current ?? 1) + 1)
        : Math.Max(1, (current ?? 1) - 1);
}

public sealed partial class CampaignState : ICampaignCustomCounters
{
    internal ICampaignCustomCounters CustomCounters => _handlers.Operation<ICampaignCustomCounters>();

    (long Minimum, long Maximum) ICampaignCustomCounters.BeforeWorldCreation(string name, CampaignTimeTrigger highest,
        IReadOnlyList<CustomCounterForecast>? preceding)
    {
        var (minimum, maximum) = CounterAfterDailyResearch(name, highest);
        if (preceding is not null)
            foreach (var forecast in preceding)
            {
                if (forecast.Change.Name != name) continue;
                if (forecast.Change.Increase)
                {
                    maximum++;
                    if (forecast.Guaranteed) minimum++;
                }
                else
                {
                    minimum = Math.Max(1, minimum - 1);
                    if (forecast.Guaranteed) maximum = Math.Max(1, maximum - 1);
                }
            }
        return (minimum, maximum);
    }

    private (long Minimum, long Maximum) CounterAfterDailyResearch(string name, CampaignTimeTrigger highest)
    {
        long minimum = _nextIds.GetValueOrDefault(name, 1);
        var maximum = minimum;
        if (highest < CampaignTimeTrigger.OneDay) return (minimum, maximum);
        HashSet<string>? earlierSources = null;
        foreach (var owner in _bases)
        {
            HashSet<string>? possible = null;
            HashSet<string>? certain = null;
            foreach (var project in owner.Research)
            {
                if ((long)project.Spent + project.Assigned < project.Cost) continue;
                var source = _content.RuntimeRules.Research[project.Rule];
                (possible ??= []).Add(source.Id);
                if (earlierSources is null || !earlierSources.Contains(source.Id)) (certain ??= []).Add(source.Id);
                possible.UnionWith(source.Value.GetOneFree);
                foreach (var group in source.Value.GetOneFreeProtected) possible.UnionWith(group.Topics);
            }
            if (possible is null) continue;
            foreach (var source in _content.RuntimeRules.Research.Rules)
            {
                if (!possible.Contains(source.Id)) continue;
                var guaranteed = certain?.Contains(source.Id) == true;
                foreach (var counter in source.Value.IncreaseCounters)
                    if (counter == name) { maximum++; if (guaranteed) minimum++; }
                foreach (var counter in source.Value.DecreaseCounters)
                    if (counter == name) { minimum = Math.Max(1, minimum - 1); if (guaranteed) maximum = Math.Max(1, maximum - 1); }
            }
            (earlierSources ??= []).UnionWith(possible);
        }
        return (minimum, maximum);
    }

    string? ICampaignCustomCounters.CapacityReason(
        IReadOnlyList<CustomCounterChange> changes, CampaignTimeTrigger highest)
    {
        const string capacityReason = "Mission site custom counter exceeds the supported range.";
        if (changes.Count == 0) return null;
        var projected = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var change in changes)
            if (change.Name.Length != 0 && !projected.ContainsKey(change.Name))
                projected.Add(change.Name, _nextIds.GetValueOrDefault(change.Name, 1));

        // Daily research precedes the half-hour site loop. Each completing base applies each
        // completed topic's side effects at most once, in catalog order. Reserve increases
        // from every possible free topic, but credit decreases only from topics certain to
        // finish. An earlier base can remove a later base's project with the same topic.
        if (highest >= CampaignTimeTrigger.OneDay)
        {
            var possiblyRemovedByEarlierBases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var owner in _bases)
            {
                HashSet<string>? possibleSideEffects = null;
                var guaranteedSideEffects = new HashSet<string>(StringComparer.Ordinal);
                foreach (var project in owner.Research)
                {
                    if ((long)project.Spent + project.Assigned < project.Cost) continue;
                    var research = _content.RuntimeRules.Research[project.Rule];
                    possibleSideEffects ??= new(StringComparer.Ordinal);
                    possibleSideEffects.Add(research.Id);
                    if (!possiblyRemovedByEarlierBases.Contains(research.Id))
                        guaranteedSideEffects.Add(research.Id);
                    possibleSideEffects.UnionWith(research.Value.GetOneFree);
                    foreach (var group in research.Value.GetOneFreeProtected)
                        possibleSideEffects.UnionWith(group.Topics);
                }
                if (possibleSideEffects is null) continue;
                foreach (var rule in _content.RuntimeRules.Research.Rules)
                {
                    if (!possibleSideEffects.Contains(rule.Id)) continue;
                    foreach (var name in rule.Value.IncreaseCounters)
                        if (projected.TryGetValue(name, out var value))
                        {
                            if (value >= int.MaxValue) return capacityReason;
                            projected[name] = value + 1;
                        }
                    if (!guaranteedSideEffects.Contains(rule.Id)) continue;
                    foreach (var name in rule.Value.DecreaseCounters)
                        if (projected.TryGetValue(name, out var value))
                            projected[name] = Math.Max(1, value - 1);
                }
                // ApplyResearchSideEffects can remove all projects for its source topic,
                // including projects in bases that have not run their daily loop yet.
                possiblyRemovedByEarlierBases.UnionWith(possibleSideEffects);
            }
        }

        if (projected.Values.Any(static value => value > int.MaxValue)) return capacityReason;

        foreach (var change in changes)
        {
            if (change.Name.Length == 0) continue;
            var current = projected[change.Name];
            var next = change.Increase ? current + 1 : Math.Max(1, current - 1);
            if (next > int.MaxValue) return capacityReason;
            projected[change.Name] = next;
        }
        return null;
    }

    void ICampaignCustomCounters.Apply(CustomCounterChange change)
    {
        if (change.Name.Length == 0) return;
        _nextIds[change.Name] = CustomCounterValue.Apply(
            _nextIds.TryGetValue(change.Name, out var current) ? current : null, change.Increase);
    }
}
