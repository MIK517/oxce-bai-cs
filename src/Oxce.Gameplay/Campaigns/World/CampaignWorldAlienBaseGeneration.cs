using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string BaseGenerationImmediateReason =
        "Alien base mission can spawn during its creation tick; immediate generated waves require world simulation.";

    private string? AlienBaseGenerationReason(AlienBaseSnapshot alienBase, RuntimeAlienDeploymentRule deployment,
        RuntimeAlienMissionRule mission, ref long work, string missionId = "", bool forecastImmediate = false)
    {
        if (alienBase.GenMissionCount == int.MaxValue)
            return "Alien base generation counter is exhausted.";
        if (!Enum.IsDefined(mission.Objective) || !Enum.IsDefined(mission.OperationType) || mission.Waves.Count == 0)
            return "Alien base generation requires a valid mission objective, operation and first wave.";
        var timer = mission.Waves[0].SpawnTimer;
        var steps = timer / 30;
        if (timer > int.MaxValue || (steps / 2 + steps) * 30 > int.MaxValue)
            return "Alien base mission initial timer exceeds the supported range.";
        if (steps / 2 <= 1 && !forecastImmediate &&
            _pass.DailyGeneratedWaveProofs?.Contains((alienBase.Id, alienBase.DeploymentId, missionId)) != true &&
            mission.Objective is not (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation) && ImmediateNoObjectReason(alienBase, deployment, mission, ref work,
                BaseGenerationImmediateReason) is { } immediateReason) return immediateReason;
        if (!deployment.GenMissionRaceFromAlienBase && mission.HasRaceWeights)
        {
            var weights = WorldAlienMissionGeneration.WeightsAt(mission.RaceWeights,
                unchecked((ulong)campaign.MonthsPassed), race: true);
            work += weights.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            _ = WorldAlienMissionGeneration.Total(weights);
        }
        var regions = new List<string>();
        if (mission.TargetBaseOdds > 0)
        {
            if (campaign.BaseStates.Count == 0) return "Alien base mission has no player base to target.";
            foreach (var owner in campaign.BaseStates)
            {
                work += campaign.RegionStates.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (LocateGenerationRegion(new(owner.Longitude, owner.Latitude)) is not { } region)
                    return "Alien base mission cannot locate a player base region.";
                regions.Add(region);
            }
        }
        if (mission.TargetBaseOdds < 100)
        {
            if (mission.HasRegionWeights)
            {
                var weights = WorldAlienMissionGeneration.WeightsAt(mission.RegionWeights,
                    unchecked((ulong)campaign.MonthsPassed), race: false);
                work += weights.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (WorldAlienMissionGeneration.Total(weights) == 0)
                    return "Alien base mission selects an empty region.";
                foreach (var choice in weights)
                    if (choice.Value != 0) regions.Add(choice.Key);
            }
            else
            {
                work += campaign.RegionStates.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (LocateGenerationRegion(alienBase.Position) is not { } region)
                    return "Alien base mission cannot locate its source base region.";
                regions.Add(region);
            }
        }
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        var rules = campaign.Content.RuntimeRules;
        foreach (var name in regions)
        {
            if (!rules.Regions.TryGet(name, out var handle))
                return "Alien base mission references an undefined region.";
            var regionId = name;
            var region = rules.Regions[handle].Value;
            // setRegion follows exactly one missionRegion indirection.
            if (region.MissionRegionId.Length != 0)
            {
                if (region.MissionRegion is not { } fallback)
                    return "Alien base mission references an undefined fallback region.";
                region = rules.Regions[fallback].Value;
                regionId = rules.Regions.GetExternalId(fallback);
            }
            if (steps / 2 <= 1 && mission.Objective is (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation) &&
                ImmediateNoObjectReason(alienBase, deployment, mission, ref work, BaseGenerationImmediateReason, regionId) is { } retaliationReason)
                return retaliationReason;
            if (mission.Objective == RuntimeMissionObjective.Site &&
                (mission.SpawnZone < 0 || mission.SpawnZone >= region.MissionZones.Count))
                return "Alien base site mission has an invalid spawn zone.";
            if (forecastImmediate && CollectGeneratedWaveForecasts(alienBase, deployment, mission, missionId,
                    regionId, hunting: false, ref work) is { } waveReason) return waveReason;
        }
        if (forecastImmediate) (_pass.DailyGeneratedWaveProofs ??= []).Add((alienBase.Id, alienBase.DeploymentId, missionId));
        return null;
    }

    private string? LocateGenerationRegion(WorldPosition position)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var state in campaign.RegionStates)
            if (WorldGeometry.InsideRegion(rules.Regions[state.Rule].Value, position)) return rules.Regions[state.Rule].Id;
        return null;
    }

    private void GenerateAlienBaseMission(int index)
    {
        var alienBase = _alienBases[index];
        var deployment = AlienBaseRule(alienBase);
        var missionName = WorldAlienMissionGeneration.Choose(deployment.GenMission, campaign.Random);
        if (missionName.Length == 0 || alienBase.GenMissionCount >= deployment.GenMissionLimit ||
            !Percent(deployment.GenMissionFrequency)) return;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(missionName)].Value;
        string regionId;
        if (Percent(rule.TargetBaseOdds))
        {
            // Preserve base list order and duplicate regions, as in GenerateSupplyMission.
            var owner = campaign.BaseStates[campaign.Random.NextInclusive(0, campaign.BaseStates.Count - 1)];
            regionId = LocateGenerationRegion(new(owner.Longitude, owner.Latitude))!;
        }
        else if (rule.HasRegionWeights)
            regionId = WorldAlienMissionGeneration.Choose(WorldAlienMissionGeneration.WeightsAt(rule.RegionWeights,
                unchecked((ulong)campaign.MonthsPassed), race: false), campaign.Random);
        else regionId = LocateGenerationRegion(alienBase.Position)!;
        var region = rules.Regions[rules.Regions.GetRequired(regionId)].Value;
        if (region.MissionRegionId.Length != 0) regionId = region.MissionRegionId;
        var id = campaign.NextId("ALIEN_MISSIONS");
        var race = !deployment.GenMissionRaceFromAlienBase && rule.HasRaceWeights
            ? WorldAlienMissionGeneration.Choose(WorldAlienMissionGeneration.WeightsAt(rule.RaceWeights,
                unchecked((ulong)campaign.MonthsPassed), race: true), campaign.Random)
            : alienBase.Race;
        var area = -1;
        if (rule.Objective == RuntimeMissionObjective.Site)
        {
            var areas = rules.Regions[rules.Regions.GetRequired(regionId)].Value.MissionZones[rule.SpawnZone].Areas;
            if (areas.Count != 0) area = campaign.Random.NextInclusive(0, areas.Count - 1);
        }
        var countdown = WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[0].SpawnTimer), campaign.Random);
        _alienBases[index] = alienBase with { GenMissionCount = checked(alienBase.GenMissionCount + 1) };
        _missions.Add(new AlienMissionSnapshot(id, missionName, regionId, race, 0, 0, countdown, 0, area)
        {
            AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, alienBase.Id,
                alienBase.Longitude, alienBase.Latitude),
        });
    }
}
