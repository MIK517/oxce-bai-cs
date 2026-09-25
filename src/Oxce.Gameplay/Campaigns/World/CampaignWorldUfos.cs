using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The bounded flying-UFO transit path. Reference: Ufo::think/calculateSpeed and
/// GeoscapeState::time5Seconds/time10Minutes. Arrival, interception and half-hour
/// detection stay guarded where their handlers are still missing.
/// </summary>
internal sealed partial class CampaignWorld
{
    // GeoscapeState::time5Seconds returns when arrival destroys a UFO.
    private bool _ufoArrivalEndedTick;
    private readonly HashSet<int> _ufosWithShieldCapacity = [];

    // The bounded wave handler only creates zero-capacity UFOs. Future handlers that
    // create shielded UFOs or change a mission's race must update this capability index.
    private void CacheUfoShieldCapabilities()
    {
        _ufosWithShieldCapacity.Clear();
        foreach (var ufo in _ufos)
        {
            var mission = _missions.Find(candidate => candidate.Id == ufo.MissionId);
            if (mission is null) continue; // Validate handles ordinary saves; pre-campaign links are optional.
            var rules = campaign.Content.RuntimeRules.Ufos;
            var rule = rules[rules.GetRequired(ufo.RuleId)].Value;
            if ((long)rule.Stats.ShieldCapacity + rule.RaceBonus(mission.Race).ShieldCapacity != 0)
                _ufosWithShieldCapacity.Add(ufo.UniqueId);
        }
    }

    private void RegisterUfoOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfos,
            "world UFO movement", MoveUfos);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfoCleanup,
            "destroyed UFO cleanup", RemoveDestroyedUfos);
    }

    private string? UfoMovementReason(CampaignTimeTrigger highest)
    {
        // A restored live count can become insufficient after another terminal arrival.
        // Keep this guard live and allocation-free; destroyed UFOs normally survive one tick.
        if (_ufos.Exists(static ufo => ufo.Status == UfoStatus.Destroyed))
        {
            foreach (var mission in _missions)
            {
                var destroyed = 0;
                foreach (var ufo in _ufos)
                    if (ufo.Status == UfoStatus.Destroyed && ufo.MissionId == mission.Id)
                        destroyed++;
                if (destroyed > mission.LiveUfos)
                    return "Destroyed UFO count exceeds its mission's live count.";
            }
        }
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status == UfoStatus.Destroyed)
            {
                // DetectXCOMBase inspects the list before five-second cleanup, even for
                // a UFO already marked destroyed.
                if (highest >= CampaignTimeTrigger.TenMinutes && ufo.TrajectoryPoint > 1)
                    return "UFO detection and retargeting require world simulation.";
                continue;
            }
            if (ufo.Status != UfoStatus.Flying || ufo.InBattlescape || ufo.Hunting ||
                ufo.Escorting || ufo.HunterKiller || ufo.Escort)
                return "UFO state requires world simulation.";
            // At points 0-1 DetectXCOMBase returns before scanning bases. Without a
            // hunter-killer or alien base, the remaining ten-minute UFO handlers do no work.
            if (highest >= CampaignTimeTrigger.ThirtyMinutes ||
                highest >= CampaignTimeTrigger.TenMinutes && ufo.TrajectoryPoint > 1)
                return "UFO detection and retargeting require world simulation.";
            // Ufo::load and AlienMission::spawnUfo own this waypoint; unlike a player
            // waypoint, it is never assigned a STR_WAY_POINT identity.
            if (ufo.Destination is not { Kind: WorldTargetKind.Waypoint, Id: 0 } destination)
                return "UFO destination requires world simulation.";
            // Ordinary saves validate this link at restore, and supported time handlers
            // cannot remove a mission while one of its UFOs is still flying.
            if (campaign.MonthsPassed == -1 && _missions.All(mission => mission.Id != ufo.MissionId))
                return "UFO mission link requires world simulation.";
            if (_ufosWithShieldCapacity.Contains(ufo.UniqueId) || ufo.Shield is not (-1 or 0))
                return "UFO shield handling requires world simulation.";
            if (ufo.Speed < 0)
                return "UFO speed is invalid.";
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            // MovingTarget::move snaps to this stationary waypoint when distance is
            // not greater than one step, including the reference's NaN fallback.
            if (!(WorldGeometry.Distance(ufo.Position, destination.Position) > speedRadian))
            {
                var mission = _missions.Find(candidate => candidate.Id == ufo.MissionId);
                if (mission is null) return "UFO mission link requires world simulation.";
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count &&
                    !CanAdvanceAirborneWaypoint(ufo, mission, trajectory))
                    return "UFO waypoint arrival requires mission simulation.";
            }
        }
        return null;
    }

    private void MoveUfos(CampaignState.TimeEffects _)
    {
        _ufoArrivalEndedTick = false;
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status != UfoStatus.Flying) continue;
            var destination = ufo.Destination!;
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            var vector = WorldGeometry.SpeedVector(ufo.Position, destination.Position, speedRadian);
            var moved = WorldGeometry.Move(ufo.Position, destination.Position, speedRadian, vector);
            if (!(WorldGeometry.Distance(ufo.Position, destination.Position) > speedRadian))
            {
                var mission = _missions.Find(candidate => candidate.Id == ufo.MissionId)!;
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count)
                {
                    if (!CanAdvanceAirborneWaypoint(ufo, mission, trajectory))
                        throw new InvalidOperationException("UFO arrival changed after world preflight.");
                    _ufos[index] = AdvanceAirborneWaypoint(ufo, mission, trajectory, moved);
                    continue;
                }
                // Ufo::think stops at the waypoint; AlienMission::ufoReachedWaypoint
                // destroys an interrupted UFO or one at the trajectory's last point.
                _ufos[index] = ufo with
                {
                    Longitude = moved.Longitude,
                    Latitude = moved.Latitude,
                    Speed = 0,
                    SpeedRadian = 0,
                    SpeedLongitude = 0,
                    SpeedLatitude = 0,
                    Direction = "STR_NONE_UC",
                    Detected = false,
                    Status = UfoStatus.Destroyed,
                };
                _ufoArrivalEndedTick = true;
                break;
            }
            _ufos[index] = ufo with
            {
                Longitude = moved.Longitude,
                Latitude = moved.Latitude,
                SpeedRadian = speedRadian,
                SpeedLongitude = vector.Longitude,
                SpeedLatitude = vector.Latitude,
                Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
                Shield = ufo.Shield == -1 ? 0 : ufo.Shield,
            };
        }
    }

    private bool CanAdvanceAirborneWaypoint(
        UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory)
    {
        var nextWaypoint = ufo.TrajectoryPoint + 1;
        var altitude = trajectory.Altitude(nextWaypoint);
        if (altitude <= 0 || altitude >= WorldAltitudes.All.Count ||
            nextWaypoint + 1 < trajectory.Waypoints.Count && trajectory.Altitude(nextWaypoint + 1) == 0)
            return false;
        var rules = campaign.Content.RuntimeRules;
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        if (missionRule.Objective != RuntimeMissionObjective.Score ||
            missionRule.OperationType != RuntimeMissionOperationType.Space)
            return false;
        var waveIndex = MissionWaveIndex(ufo, mission, missionRule);
        if ((uint)waveIndex >= (uint)missionRule.Waves.Count) return false;
        var wave = missionRule.Waves[waveIndex];
        var zone = trajectory.Zone(nextWaypoint);
        // AlienMission::getWaypoint only takes its mission-site path for an objective
        // wave in the mission's spawn zone. The other objective flags do not affect
        // ordinary regional waypoint selection.
        if (mission.MissionSiteZoneArea != -1 && wave.Objective && zone == missionRule.SpawnZone)
            return false;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        ValidateWaypointAreas(region, zone);
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        return trajectory.Speed(nextWaypoint, ufoRule.StatsForRace(mission.Race).SpeedMaximum) >= 0;
    }

    private static void ValidateWaypointAreas(RuntimeRegionRule region, int zone)
    {
        foreach (var area in WorldGeometry.MissionAreas(region, zone))
        {
            ValidateWaypointPosition(area.LongitudeMinimum, area.LatitudeMinimum);
            ValidateWaypointPosition(area.LongitudeMaximum, area.LatitudeMaximum);
        }
    }

    private static void ValidateWaypointPosition(double longitude, double latitude)
    {
        var position = WorldPosition.Create(longitude, latitude);
        if (!position.IsNormalized)
            throw new InvalidDataException("A mission area can generate an invalid UFO waypoint.");
    }

    private UfoSnapshot AdvanceAirborneWaypoint(
        UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory, WorldPosition position)
    {
        var rules = campaign.Content.RuntimeRules;
        var nextWaypoint = ufo.TrajectoryPoint + 1;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var randomPosition = WorldGeometry.RandomPoint(region, trajectory.Zone(nextWaypoint), -1, campaign.Random);
        // AlienMission::ufoReachedWaypoint assigns the raw regional point through
        // Target::setLongitude/setLatitude, which wrap coordinates across the globe.
        var nextPosition = WorldPosition.Create(randomPosition.Longitude, randomPosition.Latitude);
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var speed = trajectory.Speed(nextWaypoint, ufoRule.StatsForRace(mission.Race).SpeedMaximum);
        var speedRadian = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(position, nextPosition, speedRadian);
        return ufo with
        {
            TrajectoryPoint = nextWaypoint,
            Longitude = position.Longitude,
            Latitude = position.Latitude,
            Altitude = WorldAltitudes.All[trajectory.Altitude(nextWaypoint)],
            LandId = 0,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, nextPosition.Longitude, nextPosition.Latitude),
            Speed = speed,
            SpeedRadian = speedRadian,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
            Shield = ufo.Shield == -1 ? 0 : ufo.Shield,
        };
    }

    private static int MissionWaveIndex(
        UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule)
    {
        var wave = ufo.MissionWaveNumber > -1 ? ufo.MissionWaveNumber : mission.NextWave - 1;
        return wave < 0 ? missionRule.Waves.Count - 1 : wave;
    }

    private void RemoveDestroyedUfos(CampaignState.TimeEffects _)
    {
        if (_ufoArrivalEndedTick) return;
        for (var index = _ufos.Count - 1; index >= 0; index--)
        {
            var ufo = _ufos[index];
            if (ufo.Status != UfoStatus.Destroyed) continue;
            for (var missionIndex = 0; missionIndex < _missions.Count; missionIndex++)
            {
                var mission = _missions[missionIndex];
                if (mission.Id != ufo.MissionId) continue;
                if (mission.LiveUfos <= 0)
                    throw new InvalidDataException("A destroyed UFO has no live mission count to release.");
                _missions[missionIndex] = mission with { LiveUfos = mission.LiveUfos - 1 };
                break;
            }
            _ufosWithShieldCapacity.Remove(ufo.UniqueId);
            _ufos.RemoveAt(index);
        }
    }
}
