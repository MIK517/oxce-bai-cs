using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World;

internal readonly record struct WorldSiteScriptRegion(string Id, IReadOnlyList<MissionZone> Zones, bool Occupied);
internal readonly record struct WorldSiteScriptCandidate(string Region, int Area);

/// <summary>GeoscapeState::processCommand's ordered site-region and area pruning.</summary>
internal static class WorldMissionSiteSelection
{
    internal static IReadOnlyList<WorldSiteScriptCandidate> Candidates(
        IReadOnlyList<WorldSiteScriptRegion> regions, int spawnZone, bool directDeployment, bool missionSiteType,
        AlienStrategyState strategy, string variable)
    {
        var result = new List<WorldSiteScriptCandidate>();
        foreach (var region in regions)
        {
            if (region.Occupied || region.Zones.Count <= spawnZone) continue;
            if (spawnZone < 0) throw new InvalidDataException("Cannot spawn mission site, invalid spawn zone.");
            var areas = region.Zones[spawnZone].Areas;
            for (var index = 0; index < areas.Count; index++)
            {
                var area = areas[index];
                if (area.IsPoint ? strategy.ValidMissionLocation(variable, region.Id, index) :
                    area.Texture < 0 || missionSiteType || directDeployment)
                    result.Add(new(region.Id, index));
            }
        }
        return result;
    }
    internal static IReadOnlyList<WorldSiteScriptCandidate>? SelectMission(
        string firstMission, IReadOnlyList<string> missions,
        Func<string, IReadOnlyList<WorldSiteScriptCandidate>> candidates, out string selected)
    {
        var index = -1;
        for (var current = 0; current < missions.Count; current++)
            if (missions[current] == firstMission) { index = current; break; }
        if (index < 0) throw new InvalidDataException("Site script generated a mission outside its candidate list.");
        for (var attempt = 0; attempt < missions.Count; attempt++)
        {
            selected = missions[index];
            var available = candidates(selected);
            if (available.Count != 0) return available;
            index = (index + 1) % missions.Count;
        }
        selected = "";
        return null;
    }

    internal static int SelectArea(IReadOnlyList<WorldSiteScriptCandidate> candidates, string region, IRandomSource random)
    {
        var first = -1;
        var last = -1;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].Region == region)
            {
                if (first == -1) first = index;
                last = index;
            }
            else if (first != -1) break;
        }
        if (first == -1) throw new InvalidOperationException("Site script region lost its preflighted candidates.");
        return candidates[random.NextInclusive(first, last)].Area;
    }

}
