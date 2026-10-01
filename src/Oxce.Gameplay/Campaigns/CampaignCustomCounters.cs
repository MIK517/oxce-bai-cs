namespace Oxce.Gameplay.Campaigns;

/// <summary>A SavedGame::increase/decreaseCustomCounter call in reference order.</summary>
internal readonly record struct CustomCounterChange(string Name, bool Increase);

/// <summary>Gameplay-owned custom counters stored in the save's ids mapping.</summary>
internal interface ICampaignCustomCounters
{
    string? CapacityReason(IReadOnlyList<CustomCounterChange> changes, CampaignTimeTrigger highest);
    void Apply(CustomCounterChange change);
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
        // completed topic's side effects at most once, in catalog order. A free topic can be
        // selected before its parent, so reserve all of that parent's possible rewards too.
        if (highest >= CampaignTimeTrigger.OneDay)
            foreach (var owner in _bases)
            {
                HashSet<string>? possibleSideEffects = null;
                foreach (var project in owner.Research)
                {
                    if ((long)project.Spent + project.Assigned < project.Cost) continue;
                    var research = _content.RuntimeRules.Research[project.Rule];
                    possibleSideEffects ??= new(StringComparer.Ordinal);
                    possibleSideEffects.Add(research.Id);
                    possibleSideEffects.UnionWith(research.Value.GetOneFree);
                    foreach (var group in research.Value.GetOneFreeProtected)
                        possibleSideEffects.UnionWith(group.Topics);
                }
                if (possibleSideEffects is null) continue;
                foreach (var rule in _content.RuntimeRules.Research.Rules)
                {
                    if (!possibleSideEffects.Contains(rule.Id)) continue;
                    foreach (var name in rule.Value.IncreaseCounters)
                        if (projected.TryGetValue(name, out var value)) projected[name] = value + 1;
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
