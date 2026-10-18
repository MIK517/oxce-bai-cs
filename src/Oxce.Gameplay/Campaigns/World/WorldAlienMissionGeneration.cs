using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>RuleAlienMission timelines and ordinal WeightedOptions selection for base-generated missions.</summary>
internal static class WorldAlienMissionGeneration
{
    public static IReadOnlyDictionary<string, ulong> WeightsAt(
        IReadOnlyList<RuntimeWeightedTimeline> timeline, ulong month, bool race)
    {
        for (var index = timeline.Count - 1; index >= 0; index--)
            if (month >= timeline[index].FirstMonth) return timeline[index].Weights;
        // generateRace returns empty before its first entry. generateRegion has no
        // such fallback; define its invalid dereference as a deliberate input error.
        if (race) return EmptyWeights;
        throw new InvalidDataException("Alien base mission region timeline has no entry for the current month.");
    }

    private static readonly IReadOnlyDictionary<string, ulong> EmptyWeights =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>());

    public static string Choose(IReadOnlyDictionary<string, ulong> weights, IRandomSource random)
    {
        var total = Total(weights);
        if (total == 0) return string.Empty;
        var roll = (ulong)random.NextInclusive(1, total);
        foreach (var choice in weights.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (roll <= choice.Value) return choice.Key;
            roll -= choice.Value;
        }
        throw new InvalidOperationException("Random source returned an invalid alien mission roll.");
    }

    public static int Total(IReadOnlyDictionary<string, ulong> weights)
    {
        ulong total = 0;
        foreach (var choice in weights)
        {
            if (choice.Value > (ulong)int.MaxValue - total)
                throw new InvalidDataException(ForecastLimits.AlienBaseMissionWeightsRange);
            total += choice.Value;
        }
        return (int)total;
    }
}
