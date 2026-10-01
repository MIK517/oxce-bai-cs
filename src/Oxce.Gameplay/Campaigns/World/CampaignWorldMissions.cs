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
        long cleanupWork = 0;
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = MissionAfterDailyResearch(_missions[index], guaranteedResearch);
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
            {
                // A malformed saved count must not delete a mission that the later
                // detection/movement handlers still need, after time has advanced.
                foreach (var ufo in _ufos)
                    if (ufo.MissionId == mission.Id)
                        return "Alien mission cannot expire while UFOs still reference it.";
                cleanupWork += campaign.BaseStates.Count;
                if (cleanupWork > MaximumAlienBaseChecks) return "Retaliation mission cleanup exceeds the supported work limit.";
                if (rule.Objective != RuntimeMissionObjective.Retaliation &&
                    campaign.BaseStates.Any(owner => owner.RetaliationMissionId == mission.Id))
                    return "A non-retaliation mission cannot expire while a base links it as retaliation.";
                continue;
            }
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                mission.NextWave >= rule.Waves.Count)
                continue;
            if (mission.SpawnCountdown > 30) continue;
            foreach (var step in DueWaveSteps(mission, rule))
            {
                if (++work > MaximumImmediateWaveSteps) return WaveWorkReason;
                if (CanAdvanceNoObjectWave(step, rule)) continue;
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

    private static bool CanAdvanceNoObjectWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective != RuntimeMissionObjective.Score ||
            rule.OperationType != RuntimeMissionOperationType.Space)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (wave.UfoType.Length != 0 || wave.Ufo is not null || wave.Deployment is not null ||
            wave.Trajectory is null || wave.Objective || wave.ObjectiveOnTheLandingSite ||
            wave.ObjectiveOnXcomBase)
            return false;
        return CanAdvanceWaveCounters(mission, rule, wave);
    }

    private bool CanSpawnOrdinaryUfoWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective != RuntimeMissionObjective.Score ||
            rule.OperationType != RuntimeMissionOperationType.Space || mission.LiveUfos == int.MaxValue)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (wave.Ufo is not { } ufoHandle || wave.Trajectory is not { } trajectoryHandle ||
            wave.Objective || wave.ObjectiveOnTheLandingSite || wave.ObjectiveOnXcomBase)
            return false;
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[ufoHandle].Value;
        var hunterKillerPercentage = wave.HunterKillerPercentage == -1
            ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage;
        if (hunterKillerPercentage > 0) return false;
        var trajectory = rules.UfoTrajectories[trajectoryHandle].Value;
        if (trajectory.Waypoints.Count < 2 ||
            !IsAirborne(trajectory.Altitude(0)) || !IsAirborne(trajectory.Altitude(1)))
            return false;
        var stats = ufoRule.StatsForRace(mission.Race);
        var speed = trajectory.Speed(0, stats.SpeedMaximum);
        // The half-hour UFO handler scores and detects this ship after spawning.
        if (ufoRule.Scripts.Count != 0 ||
            !WorldUfoShields.IsValid(-1, stats.ShieldCapacity, stats.ShieldRechargeInGeoscape) || speed < 0)
            return false;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        if (!TryGetFixedWaypoint(region, trajectory.Zone(0), out var position) ||
            !TryGetFixedWaypoint(region, trajectory.Zone(1), out var destination) ||
            !(WorldGeometry.Distance(position, destination) > WorldGeometry.RadianSpeed(speed)))
            return false;
        return CanAdvanceWaveCounters(mission, rule, wave);
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
        var position = NormalizeWaypoint(WorldGeometry.RandomPoint(region, trajectory.Zone(0), -1, campaign.Random));
        // Fixed-point eligibility implies getLandPoint's city bypass when the
        // following leg lands, consuming the same regional selection.
        var destination = NormalizeWaypoint(WorldGeometry.RandomPoint(region, trajectory.Zone(1), -1, campaign.Random));
        var stats = ufoRule.StatsForRace(mission.Race);
        var speed = trajectory.Speed(0, stats.SpeedMaximum);
        var speedRadian = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(position, destination, speedRadian);
        return new UfoSnapshot(campaign.NextId("STR_UFO_UNIQUE"), wave.UfoType, mission.Id,
            wave.TrajectoryId, 0, position.Longitude, position.Latitude, UfoStatus.Flying,
            WorldAltitudes.All[trajectory.Altitude(0)])
        {
            MissionWaveNumber = mission.NextWave,
            Speed = speed,
            SpeedRadian = speedRadian,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, destination.Longitude, destination.Latitude),
        };
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
    private static IEnumerable<AlienMissionSnapshot> DueWaveSteps(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
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
                LiveUfos = wave.Ufo is not null ? checked(mission.LiveUfos + 1) : mission.LiveUfos
            };
        }
    }
}
