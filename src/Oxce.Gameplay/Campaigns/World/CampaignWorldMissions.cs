using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// AlienMission::think's half-hour countdown, ordinary airborne UFO waves, and waves
/// without a UFO or site. Other outcomes remain guarded until their creation and script
/// consequences have handlers.
/// </summary>
internal sealed partial class CampaignWorld
{
    private void RegisterMissionOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.OneDay, CampaignTimeOrder.DayWorldMissionInterruption,
            "alien mission research interruption", InterruptMissionsDaily);
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldMissions,
            "alien mission countdown", AdvanceMissionCountdowns);
    }

    private string? MissionSchedulingReason(CampaignTimeTrigger highest, IReadOnlySet<string>? guaranteedResearch)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var ufoSpawns = 0;
        var work = 0;
        long sourceBaseWork = 0;
        long cleanupWork = 0;
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = MissionAfterDailyResearch(_missions[index], guaranteedResearch);
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
            {
                if (MissionExpiryReason(mission, rule, ref cleanupWork) is { } expiryReason) return expiryReason;
                continue;
            }
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                mission.NextWave >= rule.Waves.Count)
                continue;
            if (mission.SpawnCountdown > 30) continue;
            foreach (var step in DueWaveSteps(mission, rule))
            {
                if (++work > MaximumImmediateWaveSteps) return WaveWorkReason;
                if (step.AlienBase is not null)
                {
                    sourceBaseWork += _alienBases.Count;
                    if (sourceBaseWork > MaximumAlienBaseChecks) return "Alien mission source base lookups exceed the supported work limit.";
                }
                if (DeferredWaveCompletion(step, rule, rule.Waves[step.NextWave]))
                    return "Alien mission wave completion requires objective simulation.";
                if (CanAdvanceNoObjectWave(step, rule))
                {
                    var finishes = step.NextWave + 1 == rule.Waves.Count &&
                        (ulong)step.NextUfoCounter + 1 >= rule.Waves[step.NextWave].UfoCount;
                    if (finishes && step.LiveUfos == 0 &&
                        MissionExpiryReason(step, rule, ref cleanupWork) is { } expiryReason) return expiryReason;
                    continue;
                }
                if (CanSpawnOrdinaryUfoWave(step, rule))
                {
                    ufoSpawns++;
                    continue;
                }
                return "Alien mission wave spawning requires world simulation.";
            }
        }
        if ((long)_ufos.Count + ufoSpawns > 100_000) return "World UFO population exceeds the supported limit.";
        return CanAllocateUfoIds(ufoSpawns) ? null : "UFO unique IDs are exhausted or collide with a saved UFO.";
    }

    private string? MissionExpiryReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, ref long work)
    {
        // Check both existing completion and completion forecast within this tick.
        // Saved live counts alone cannot prove that deleting the mission is safe.
        work += _ufos.Count + (long)campaign.BaseStates.Count;
        if (work > MaximumAlienBaseChecks) return "Retaliation mission cleanup exceeds the supported work limit.";
        foreach (var ufo in _ufos)
            if (ufo.MissionId == mission.Id) return "Alien mission cannot expire while UFOs still reference it.";
        return rule.Objective != RuntimeMissionObjective.Retaliation &&
            campaign.BaseStates.Any(owner => owner.RetaliationMissionId == mission.Id)
            ? "A non-retaliation mission cannot expire while a base links it as retaliation." : null;
    }

    private void AdvanceMissionCountdowns(CampaignState.TimeEffects _)
    {
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = _missions[index];
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                mission.NextWave >= rule.Waves.Count)
                continue;
            if (mission.SpawnCountdown > 30)
                _missions[index] = mission with { SpawnCountdown = mission.SpawnCountdown - 30 };
            else
            {
                for (var work = 0; ; work++)
                {
                    if (work >= MaximumImmediateWaveSteps)
                        throw new InvalidOperationException("Mission wave work changed after preflight.");
                    var spawnsUfo = CanSpawnOrdinaryUfoWave(mission, rule);
                    if (!spawnsUfo && !CanAdvanceNoObjectWave(mission, rule))
                        throw new InvalidOperationException("Alien mission wave changed after world preflight.");
                    var wave = rule.Waves[mission.NextWave];
                    if (spawnsUfo) _ufos.Add(SpawnOrdinaryUfo(mission, wave));
                    var counter = (ulong)mission.NextUfoCounter + 1;
                    var nextWave = mission.NextWave;
                    if (counter >= wave.UfoCount)
                    {
                        counter = 0;
                        nextWave++;
                    }
                    var countdown = nextWave < rule.Waves.Count
                        ? WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[nextWave].SpawnTimer), campaign.Random)
                        : mission.SpawnCountdown;
                    mission = mission with
                    {
                        NextWave = nextWave,
                        NextUfoCounter = checked((int)counter),
                        SpawnCountdown = countdown,
                        LiveUfos = spawnsUfo ? checked(mission.LiveUfos + 1) : mission.LiveUfos,
                    };
                    _missions[index] = mission;
                    // Recursion tests the original timer, not the rounded countdown.
                    if (nextWave == rule.Waves.Count || rule.Waves[nextWave].SpawnTimer != 0) break;
                }
            }
        }
        for (var index = _missions.Count - 1; index >= 0; index--)
        {
            var mission = _missions[index];
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
            {
                if (rule.Objective == RuntimeMissionObjective.Retaliation)
                    foreach (var owner in campaign.BaseStates)
                        if (owner.RetaliationMissionId == mission.Id) owner.RetaliationMissionId = null;
                _missions.RemoveAt(index);
            }
        }
    }

    private static bool IsOver(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule) =>
        mission.Interrupted && mission.LiveUfos == 0 ||
        !(rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration) &&
        mission.NextWave == rule.Waves.Count && mission.LiveUfos == 0;

    private bool CanAdvanceNoObjectWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective is RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (SuppressedSupplyWave(mission, rule, wave))
            return wave.Trajectory is not null && CanAdvanceWaveCounters(mission, rule, wave);
        if (DeferredWaveCompletion(mission, rule, wave) || wave.Ufo is not null || wave.Trajectory is null ||
            rule.Objective == RuntimeMissionObjective.Site && wave.Objective ||
            wave.Deployment is { } deployment && campaign.Content.RuntimeRules.AlienDeployments[deployment].Value.MarkerName.Length != 0)
            return false;
        return CanAdvanceWaveCounters(mission, rule, wave);
    }

    private bool SuppressedSupplyWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Supply &&
        (wave.Ufo is null ? wave.Deployment is null : wave.Objective && MissionSourceBase(mission) is null);

    private static bool DeferredWaveCompletion(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave)
    {
        var finishes = mission.NextWave + 1 == rule.Waves.Count && (ulong)mission.NextUfoCounter + 1 >= wave.UfoCount;
        return finishes && (rule.Objective == RuntimeMissionObjective.Infiltration ||
            rule.Objective == RuntimeMissionObjective.Base && !wave.ObjectiveOnTheLandingSite);
    }

    private bool CanSpawnOrdinaryUfoWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective is RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation ||
            mission.LiveUfos == int.MaxValue)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (DeferredWaveCompletion(mission, rule, wave) || wave.Ufo is not { } ufoHandle || wave.Trajectory is not { } trajectoryHandle ||
            SuppressedSupplyWave(mission, rule, wave))
            return false;
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[ufoHandle].Value;
        var hunterKillerPercentage = wave.HunterKillerPercentage == -1
            ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage;
        if (hunterKillerPercentage > 0 && !(rule.Objective == RuntimeMissionObjective.Supply && wave.Objective)) return false;
        var trajectory = rules.UfoTrajectories[trajectoryHandle].Value;
        if (trajectory.Waypoints.Count < 2 ||
            trajectory.Altitude(0) != 0 && !IsAirborne(trajectory.Altitude(0)) ||
            trajectory.Altitude(1) != 0 && !IsAirborne(trajectory.Altitude(1)))
            return false;
        if (rule.Objective != RuntimeMissionObjective.Supply && mission.MissionSiteZoneArea != -1 && wave.Objective &&
            (trajectory.Zone(0) == rule.SpawnZone || trajectory.Zone(1) == rule.SpawnZone))
            return false;
        var stats = ufoRule.StatsForRace(mission.Race);
        var speed = trajectory.Speed(0, stats.SpeedMaximum);
        if (trajectory.Altitude(0) == 0 &&
            (rule.Objective == RuntimeMissionObjective.Supply || stats.DamageMaximum <= 0 ||
             trajectory.GroundTimer <= 0 || trajectory.GroundTimer > int.MaxValue / 5))
            return false;
        // The half-hour UFO handler scores and detects this ship after spawning.
        if (ufoRule.Scripts.Count != 0 ||
            !WorldUfoShields.IsValid(-1, stats.ShieldCapacity, stats.ShieldRechargeInGeoscape) || speed < 0)
            return false;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        if (!TryOrdinarySpawnPosition(mission, rule, trajectory, region, out var position) ||
            !TryOrdinarySpawnDestination(mission, rule, trajectory, region, out var destination) ||
            !(WorldGeometry.Distance(position, destination) > WorldGeometry.RadianSpeed(speed)))
            return false;
        return CanAdvanceWaveCounters(mission, rule, wave);
    }

    private AlienBaseSnapshot? MissionSourceBase(AlienMissionSnapshot mission)
    {
        if (mission.AlienBase is null) return null;
        foreach (var candidate in _alienBases)
            if (FollowsAlienBase(mission.AlienBase, candidate)) return candidate;
        return null;
    }

    private bool TryOrdinarySpawnPosition(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region, out WorldPosition position)
    {
        var source = rule.OperationType == RuntimeMissionOperationType.Space ? null : MissionSourceBase(mission);
        if (source is not null && rule.Objective == RuntimeMissionObjective.Supply)
        {
            position = source.Position;
            return position.IsNormalized;
        }
        if (!TryGetFixedWaypoint(region, trajectory.Zone(0), out position)) return false;
        if (source is not null) position = source.Position;
        return position.IsNormalized;
    }

    private bool TryOrdinarySpawnDestination(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region, out WorldPosition position)
    {
        if (rule.Objective == RuntimeMissionObjective.Supply && rule.Waves[mission.NextWave].Objective &&
            trajectory.Altitude(1) == 0 && MissionSourceBase(mission) is { } supplyBase)
        {
            position = supplyBase.Position;
            return position.IsNormalized;
        }
        _ = WorldGeometry.MissionAreas(region, trajectory.Zone(1));
        if (rule.Objective != RuntimeMissionObjective.Supply && rule.OperationType != RuntimeMissionOperationType.Space &&
            trajectory.Waypoints.Count == 2 && MissionSourceBase(mission) is { } source)
        {
            position = source.Position;
            return position.IsNormalized;
        }
        return TryGetFixedWaypoint(region, trajectory.Zone(1), out position);
    }

    private static bool TryGetFixedWaypoint(RuntimeRegionRule region, int zone, out WorldPosition position)
    {
        var areas = WorldGeometry.MissionAreas(region, zone);
        if (areas.Count != 1 || !areas[0].IsPoint)
        {
            position = default;
            return false;
        }
        position = WorldPosition.Create(areas[0].LongitudeMinimum, areas[0].LatitudeMinimum);
        return position.IsNormalized;
    }

    private bool CanAllocateUfoIds(int count)
    {
        if (count == 0) return true;
        var first = campaign.PeekNextId("STR_UFO_UNIQUE");
        var afterLast = (long)first + count;
        if (afterLast > int.MaxValue) return false;
        foreach (var ufo in _ufos)
            if (ufo.UniqueId >= first && ufo.UniqueId < afterLast)
                return false;
        return true;
    }

    private UfoSnapshot SpawnOrdinaryUfo(AlienMissionSnapshot mission, RuntimeMissionWave wave)
    {
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[wave.Ufo!.Value].Value;
        var trajectory = rules.UfoTrajectories[wave.Trajectory!.Value].Value;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        var source = missionRule.OperationType == RuntimeMissionOperationType.Space ? null : MissionSourceBase(mission);
        var position = source is not null && missionRule.Objective == RuntimeMissionObjective.Supply
            ? source.Position
            : NormalizeWaypoint(WorldGeometry.RandomPoint(region, trajectory.Zone(0), -1, campaign.Random));
        if (source is not null) position = source.Position;
        // Fixed-point eligibility implies getLandPoint's city bypass when the
        // following leg lands, consuming the same regional selection.
        var destination = NormalizeWaypoint(missionRule.Objective == RuntimeMissionObjective.Supply
            ? SupplySpawnDestination(mission, wave, trajectory, region, ufoRule)
            : OrdinaryWaypoint(mission, trajectory, 1, region, ufoRule));
        var stats = ufoRule.StatsForRace(mission.Race);
        var speed = trajectory.Speed(0, stats.SpeedMaximum);
        var speedRadian = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(position, destination, speedRadian);
        return new UfoSnapshot(campaign.NextId("STR_UFO_UNIQUE"), wave.UfoType, mission.Id,
            wave.TrajectoryId, 0, position.Longitude, position.Latitude,
            trajectory.Altitude(0) == 0 ? UfoStatus.Landed : UfoStatus.Flying,
            WorldAltitudes.All[trajectory.Altitude(0)])
        {
            MissionWaveNumber = mission.NextWave,
            SecondsRemaining = trajectory.Altitude(0) == 0 ? checked(trajectory.GroundTimer * 5) : 0,
            Speed = speed,
            SpeedRadian = speedRadian,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, destination.Longitude, destination.Latitude),
        };
    }

    private WorldPosition SupplySpawnDestination(AlienMissionSnapshot mission, RuntimeMissionWave wave,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region, RuntimeUfoRule ufoRule)
    {
        if (trajectory.Altitude(1) != 0)
            return WorldGeometry.RandomPoint(region, trajectory.Zone(1), -1, campaign.Random);
        if (wave.Objective) return MissionSourceBase(mission)!.Position;
        return WorldGeometry.LandPoint(region, campaign.Content.RuntimeRules.Campaign.Globe,
            trajectory.Zone(1), ufoRule.FakeWaterLandingChance, campaign.Random);
    }

    private static WorldPosition NormalizeWaypoint(WorldPosition position) =>
        WorldPosition.Create(position.Longitude, position.Latitude);

    private static bool IsAirborne(int altitude) => altitude > 0 && altitude < WorldAltitudes.All.Count;

    private static bool CanAdvanceWaveCounters(
        AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave)
    {
        var counter = (ulong)mission.NextUfoCounter + 1;
        if (counter > int.MaxValue && counter < wave.UfoCount) return false;
        var nextWave = counter >= wave.UfoCount ? mission.NextWave + 1 : mission.NextWave;
        if (nextWave == rule.Waves.Count) return true;
        var timer = rule.Waves[nextWave].SpawnTimer;
        // DueWaveSteps validates the entire immediate chain before any mutation/RNG.
        if (timer > int.MaxValue) return false;
        var steps = (long)(timer / 30);
        return (steps / 2 + steps) * 30 <= int.MaxValue;
    }

    private const int MaximumImmediateWaveSteps = 4096;
    private const string WaveWorkReason = "Immediate mission waves exceed the supported work limit.";

    /// <summary>Forecasts only counters, never object creation, scripts or random choices.</summary>
    private IEnumerable<AlienMissionSnapshot> DueWaveSteps(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        for (var work = 0; ; work++)
        {
            if (work >= MaximumImmediateWaveSteps) throw new InvalidDataException(WaveWorkReason);
            yield return mission;
            var wave = rule.Waves[mission.NextWave];
            var counter = (ulong)mission.NextUfoCounter + 1;
            var nextWave = mission.NextWave;
            if (counter >= wave.UfoCount) { counter = 0; nextWave++; }
            if (nextWave == rule.Waves.Count || rule.Waves[nextWave].SpawnTimer != 0) yield break;
            mission = mission with
            {
                NextWave = nextWave,
                NextUfoCounter = checked((int)counter),
                SpawnCountdown = 0,
                LiveUfos = wave.Ufo is not null && !SuppressedSupplyWave(mission, rule, wave)
                    ? checked(mission.LiveUfos + 1) : mission.LiveUfos
            };
        }
    }
}
