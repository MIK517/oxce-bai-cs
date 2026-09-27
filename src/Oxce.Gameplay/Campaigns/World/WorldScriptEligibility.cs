using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Inputs at one scheduling phase; score/funds are supplied by its owning caller.</summary>
internal sealed record WorldScriptContext(int Month, int Difficulty, int Score, long Funds,
    bool DebugResearch, IReadOnlySet<string> Research, IReadOnlySet<string> Items,
    IReadOnlySet<string> Facilities, IReadOnlySet<string> SoldierTypes,
    IReadOnlySet<string> BaseRegions, IReadOnlySet<string> BaseCountries, IReadOnlySet<string> PactCountries,
    IReadOnlyDictionary<string, int> MissionRuns, IReadOnlyDictionary<string, int> NextIds);

/// <summary>GeoscapeState::determineAlienMissions eligibility, before the caller's RNG draw.</summary>
internal static class WorldScriptEligibility
{
    public static bool Matches(RuntimeScriptCriteria rule, WorldScriptContext state)
    {
        if (rule.FirstMonth > state.Month || rule.LastMonth != -1 && rule.LastMonth < state.Month ||
            state.Month >= 1 && (rule.MinimumScore > state.Score || rule.MaximumScore < state.Score ||
                rule.MinimumFunds > state.Funds || rule.MaximumFunds < state.Funds) ||
            rule.MinimumDifficulty > state.Difficulty || rule.MaximumDifficulty < state.Difficulty)
            return false;
        foreach (var trigger in rule.Research)
            if ((state.DebugResearch || state.Research.Contains(trigger.Rule.Id)) != trigger.Expected) return false;
        if (!CounterMatches(rule, rule.MissionVariable, state.MissionRuns.GetValueOrDefault(rule.MissionVariable)) ||
            !CounterMatches(rule, rule.MissionMarker, state.NextIds.TryGetValue(rule.MissionMarker, out var next)
                ? Math.Max(1, next - 1) : 0)) return false;
        return All(rule.Items, state.Items) && All(rule.Facilities, state.Facilities) &&
            All(rule.SoldierTypes, state.SoldierTypes) && All(rule.BaseRegions, state.BaseRegions) &&
            All(rule.BaseCountries, state.BaseCountries) && All(rule.PactCountries, state.PactCountries);
    }

    private static bool CounterMatches(RuntimeScriptCriteria rule, string name, int value) =>
        name.Length == 0 || ((rule.CounterMinimum <= 0 || value >= rule.CounterMinimum) &&
            (rule.CounterMaximum == -1 || value <= rule.CounterMaximum));

    private static bool All<TFamily>(IReadOnlyList<RuntimeScriptTrigger<TFamily>> triggers, IReadOnlySet<string> present)
    {
        foreach (var trigger in triggers)
            if (present.Contains(trigger.Rule.Id) != trigger.Expected) return false;
        return true;
    }
}
