using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? MissionOperationReason(RuntimeAlienMissionRule rule, string selectedRegion)
    {
        if (rule.OperationType is RuntimeMissionOperationType.Space or RuntimeMissionOperationType.RegionNewBase) return null;
        var candidates = MissionOperationBases(rule, selectedRegion).ToArray();
        var identities = new HashSet<(string Marker, int Id)>();
        var duplicates = new HashSet<(string Marker, int Id)>();
        foreach (var candidate in _alienBases)
            if (!identities.Add((MarkerName(candidate.DeploymentId), candidate.Id)))
                duplicates.Add((MarkerName(candidate.DeploymentId), candidate.Id));
        foreach (var candidate in candidates)
            if (duplicates.Contains((MarkerName(candidate.DeploymentId), candidate.Id)))
                return "Mission operation base identity is ambiguous.";
        return null;
    }

    private IEnumerable<AlienBaseSnapshot> MissionOperationBases(RuntimeAlienMissionRule rule, string selectedRegion)
    {
        var regions = campaign.Content.RuntimeRules.Regions;
        var region = regions[regions.GetRequired(selectedRegion)].Value;
        if (region.MissionRegion is { } remap) region = regions[remap].Value;
        var regional = rule.OperationType is RuntimeMissionOperationType.RegionExistingBase or
            RuntimeMissionOperationType.RegionNewBaseIfNecessary;
        return regional ? _alienBases.Where(candidate => WorldGeometry.InsideRegion(region, candidate.Position)) : _alienBases;
    }

    private AlienMissionSnapshot InitializeMissionOperation(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.OperationType == RuntimeMissionOperationType.Space || mission.AlienBase is not null) return mission;
        // setRegion has already followed one remap. Avoid following a second one.
        var regions = campaign.Content.RuntimeRules.Regions;
        var region = regions[regions.GetRequired(mission.RegionId)].Value;
        var regional = rule.OperationType is RuntimeMissionOperationType.RegionExistingBase or
            RuntimeMissionOperationType.RegionNewBaseIfNecessary;
        var candidates = rule.OperationType == RuntimeMissionOperationType.RegionNewBase ? [] :
            (regional ? _alienBases.Where(candidate => WorldGeometry.InsideRegion(region, candidate.Position)) : _alienBases).ToArray();
        if (candidates.Length == 0)
            return rule.OperationType is RuntimeMissionOperationType.RegionExistingBase or RuntimeMissionOperationType.EarthExistingBase
                ? mission with { Interrupted = true } : CreateMissionOperationBase(mission, rule, region);
        var selected = candidates[campaign.Random.NextInclusive(0, candidates.Length - 1)];
        return mission with
        {
            AlienBase = new(WorldTargetKind.AlienBase, MarkerName(selected.DeploymentId), selected.Id,
            selected.Longitude, selected.Latitude)
        };
    }
}
