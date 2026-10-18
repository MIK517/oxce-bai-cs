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
        var region = regions.GetRequiredValue(selectedRegion);
        if (region.MissionRegion is { } remap) region = regions[remap].Value;
        var regional = rule.OperationType is RuntimeMissionOperationType.RegionExistingBase or
            RuntimeMissionOperationType.RegionNewBaseIfNecessary;
        return regional ? _alienBases.Where(candidate => WorldGeometry.InsideRegion(region, candidate.Position)) : _alienBases;
    }

    private AlienMissionSnapshot InitializeMissionOperation(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.OperationType == RuntimeMissionOperationType.Space || mission.AlienBase is not null) return mission;
        var (selected, spawn) = PlanMissionOperation(mission, rule);
        if (selected is not null)
            return mission with
            {
                AlienBase = new(WorldTargetKind.AlienBase, MarkerName(selected.DeploymentId), selected.Id,
                selected.Longitude, selected.Latitude)
            };
        if (spawn is not { } point) return mission with { Interrupted = true };
        var deployment = campaign.Content.RuntimeRules.AlienDeployments[rule.OperationBaseType!.Value].Value;
        var alienBase = SpawnMissionAlienBase(mission, rule, rule.OperationBaseType!.Value, point);
        return mission with
        {
            AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, alienBase.Id,
                alienBase.Position.Longitude, alienBase.Position.Latitude),
        };
    }

    /// <summary>
    /// AlienMission::start's operation base, with every random draw but no change: an existing base chosen
    /// from the candidates, or the point a new base would be spawned at; neither means the mission is
    /// interrupted.
    /// </summary>
    private (AlienBaseSnapshot? Selected, WorldPosition? Spawn) PlanMissionOperation(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        // setRegion has already followed one remap. Avoid following a second one.
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions.GetRequiredValue(mission.RegionId);
        var regional = rule.OperationType is RuntimeMissionOperationType.RegionExistingBase or
            RuntimeMissionOperationType.RegionNewBaseIfNecessary;
        var candidates = rule.OperationType == RuntimeMissionOperationType.RegionNewBase ? [] :
            (regional ? _alienBases.Where(candidate => WorldGeometry.InsideRegion(region, candidate.Position)) : _alienBases).ToArray();
        if (candidates.Length != 0) return (candidates[campaign.Random.NextInclusive(0, candidates.Length - 1)], null);
        if (rule.OperationType is RuntimeMissionOperationType.RegionExistingBase or RuntimeMissionOperationType.EarthExistingBase)
            return (null, null);
        var deployment = rules.AlienDeployments[rule.OperationBaseType!.Value].Value;
        return (null, WorldOperationBaseSpawn.Select(region, rules.Campaign.Globe, rule.OperationSpawnZone,
            deployment.FakeUnderwaterSpawnChance, rules.Campaign.AllowAlienBasesOnWrongTextures, campaign.Random));
    }
}
