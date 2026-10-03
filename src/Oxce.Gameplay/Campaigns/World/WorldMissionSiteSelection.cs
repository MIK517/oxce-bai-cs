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
}
