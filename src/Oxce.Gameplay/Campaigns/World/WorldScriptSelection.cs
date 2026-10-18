using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>RuleMissionScript/RuleEventScript reverse timeline scans and WeightedOptions::choose.</summary>
public static class WorldScriptSelection
{
    public static RuntimeRuleReference<TFamily>? Generate<TFamily>(
        IReadOnlyList<RuntimeScriptTimeline<TFamily>> timeline, ulong month, IRandomSource random,
        bool allowEmptyTimeline = false)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(random);
        return Choose(ForMonth(timeline, month, allowEmptyTimeline), random);
    }

    internal static IReadOnlyList<RuntimeScriptWeight<TFamily>> ForMonth<TFamily>(
        IReadOnlyList<RuntimeScriptTimeline<TFamily>> timeline, ulong month, bool allowEmptyTimeline)
    {
        if (timeline.Count == 0 && allowEmptyTimeline) return []; // RuleEventScript::generate
        for (var index = timeline.Count - 1; index >= 0; index--)
            if (month >= timeline[index].FirstMonth) return timeline[index].Choices;
        // The reference dereferences rend() for this malformed input. Fail before drawing RNG.
        throw new InvalidDataException("Strategic script timeline has no entry for the requested month.");
    }

    public static IReadOnlyList<RuntimeScriptWeight<TFamily>> Available<TFamily>(
        IReadOnlyList<RuntimeScriptTimeline<TFamily>> timeline, int month)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.Count == 0) throw new InvalidDataException("Strategic script listing requires a nonempty timeline.");
        // getMissionTypes/getRegions use a signed comparison and fall back to the first entry.
        for (var index = timeline.Count - 1; index > 0; index--)
            if (month >= unchecked((int)timeline[index].FirstMonth)) return timeline[index].Choices;
        return timeline[0].Choices;
    }

    public static RuntimeRuleReference<TFamily>? Choose<TFamily>(
        IReadOnlyList<RuntimeScriptWeight<TFamily>> choices, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(random);
        ulong total = 0;
        foreach (var choice in choices)
        {
            if (choice.Weight > (ulong)int.MaxValue - total)
                throw new InvalidDataException(ForecastLimits.StrategicScriptWeightsRange);
            total += choice.Weight;
        }
        if (total == 0) return null;
        var roll = (ulong)random.NextInclusive(1, (int)total);
        foreach (var choice in choices)
        {
            if (roll <= choice.Weight) return choice.Rule;
            roll -= choice.Weight;
        }
        throw new InvalidOperationException("Random source returned an invalid strategic script roll.");
    }
}
