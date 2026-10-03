using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>AlienMission::selectXcomBase and skip-scouting selection in spawnUfo.</summary>
internal static class WorldRetaliation
{
    internal static List<T> Candidates<T>(IReadOnlyList<T> bases, RuntimeRegionRule region,
        RuntimeMissionObjective objective, bool skipScouting, bool multi,
        Func<T, WorldPosition> position, Func<T, bool> discovered)
    {
        var result = new List<T>();
        var retaliation = objective == RuntimeMissionObjective.Retaliation;
        foreach (var owner in bases)
        {
            if (!WorldGeometry.InsideRegion(region, position(owner))) continue;
            if (retaliation && !skipScouting && !discovered(owner)) continue;
            result.Add(owner);
            if (retaliation && (skipScouting || !multi)) break;
        }
        return result;
    }

    internal static int? SelectIndex(int count, IRandomSource random) =>
        count == 0 ? null : count == 1 ? 0 : random.NextInclusive(0, count - 1);
}
