using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Ordinary UFO flight, landing and takeoff. Reference: Ufo::think/calculateSpeed,
/// AlienMission::ufoReachedWaypoint/ufoLifting and GeoscapeState::time5Seconds.
/// Special arrivals and hunting/escort retargeting remain guarded.
/// </summary>
internal sealed partial class CampaignWorld
{
    // GeoscapeState::time5Seconds returns when arrival destroys a UFO.
    private bool _ufoArrivalEndedTick;

    /// <summary>
    /// Ufo::getCraftStats().shieldCapacity, including the mission race bonus. Evaluated on
    /// demand so UFOs created or re-raced by later handlers can never be missed.
    /// </summary>
    private bool HasShieldCapacity(UfoSnapshot ufo, AlienMissionSnapshot mission)
    {
        var rules = campaign.Content.RuntimeRules.Ufos;
        var rule = rules[rules.GetRequired(ufo.RuleId)].Value;
        return (long)rule.Stats.ShieldCapacity + rule.RaceBonus(mission.Race).ShieldCapacity != 0;
    }

    private void RegisterUfoOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfos,
            "world UFO movement", MoveUfos);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfoCleanup,
            "destroyed UFO cleanup", RemoveDestroyedUfos);
        registry.Timed(CampaignTimeTrigger.TenMinutes, CampaignTimeOrder.TenMinutesWorldBaseDetection,
            "UFO detection of XCOM bases", DetectXcomBases);
    }

    private string? UfoMovementReason()
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
                // The ten-minute scan also inspects these before five-second cleanup.
                continue;
            }
            if (ufo.Status is not (UfoStatus.Flying or UfoStatus.Landed) || ufo.InBattlescape || ufo.Hunting ||
                ufo.Escorting || ufo.HunterKiller || ufo.Escort)
                return "UFO state requires world simulation.";
            // Ufo::load and AlienMission::spawnUfo own this waypoint; unlike a player
            // waypoint, it is never assigned a STR_WAY_POINT identity.
            if (ufo.Destination is not { Kind: WorldTargetKind.Waypoint, Id: 0 } destination)
                return "UFO destination requires world simulation.";
            // Ordinary saves validate this link at restore, and supported time handlers
            // cannot remove a mission while one of its UFOs is still flying.
            if (campaign.MonthsPassed == -1 && FindMission(ufo.MissionId) is null)
                return "UFO mission link requires world simulation.";
            if (ufo.Shield is not (-1 or 0) ||
                FindMission(ufo.MissionId) is { } shieldMission && HasShieldCapacity(ufo, shieldMission))
                return "UFO shield handling requires world simulation.";
            if (ufo.Speed < 0)
                return "UFO speed is invalid.";
            if (ufo.Status == UfoStatus.Landed)
            {
                if (ufo.Altitude != WorldAltitudes.Ground || ufo.SecondsRemaining < 5 || ufo.SecondsRemaining % 5 != 0)
                    return "Landed UFO timer or altitude is invalid.";
                var mission = FindMission(ufo.MissionId)!;
                var rules = campaign.Content.RuntimeRules;
                var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
                if (missionRule.Objective != RuntimeMissionObjective.Score ||
                    missionRule.OperationType != RuntimeMissionOperationType.Space)
                    return "UFO takeoff requires mission simulation.";
                var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
                var maximumSpeed = checked(rule.Stats.SpeedMaximum + rule.RaceBonus(mission.Race).SpeedMaximum);
                if (trajectory.Speed(ufo.TrajectoryPoint, maximumSpeed) < 0)
                    return "UFO speed is invalid.";
                continue;
            }
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            // MovingTarget::move snaps to this stationary waypoint when distance is
            // not greater than one step, including the reference's NaN fallback.
            if (!(WorldGeometry.Distance(ufo.Position, destination.Position) > speedRadian))
            {
                var mission = FindMission(ufo.MissionId);
                if (mission is null) return "UFO mission link requires world simulation.";
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count &&
                    !CanAdvanceOrdinaryWaypoint(ufo, mission, trajectory))
                    return "UFO waypoint arrival requires mission simulation.";
            }
        }
        return null;
    }

    private void MoveUfos(CampaignState.TimeEffects effects)
    {
        _ufoArrivalEndedTick = false;
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status == UfoStatus.Landed)
            {
                _ufos[index] = ufo.SecondsRemaining == 5 ? LiftUfo(ufo) :
                    ufo with { SecondsRemaining = ufo.SecondsRemaining - 5 };
                continue;
            }
            if (ufo.Status != UfoStatus.Flying) continue;
            var destination = ufo.Destination!;
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            var vector = WorldGeometry.SpeedVector(ufo.Position, destination.Position, speedRadian);
            var moved = WorldGeometry.Move(ufo.Position, destination.Position, speedRadian, vector);
            if (!(WorldGeometry.Distance(ufo.Position, destination.Position) > speedRadian))
            {
                var mission = FindMission(ufo.MissionId)!;
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count)
                {
                    if (!CanAdvanceOrdinaryWaypoint(ufo, mission, trajectory))
                        throw new InvalidOperationException("UFO arrival changed after world preflight.");
                    var arrived = AdvanceOrdinaryWaypoint(ufo, mission, trajectory, moved);
                    _ufos[index] = arrived;
                    if (campaign.Options.UfoLandingAlert && arrived.Status == UfoStatus.Landed &&
                        arrived.Detected && arrived.LandId != 0)
                        effects.Notify(new UfoLanded(arrived.UniqueId, arrived.LandId));
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

    private bool CanAdvanceOrdinaryWaypoint(
        UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory)
    {
        var nextWaypoint = ufo.TrajectoryPoint + 1;
        var altitude = trajectory.Altitude(nextWaypoint);
        if (altitude < 0 || altitude >= WorldAltitudes.All.Count)
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
        if (altitude == 0 && (ufo.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun ||
            mission.MissionSiteZoneArea != -1 && wave.Objective && trajectory.Zone(ufo.TrajectoryPoint) == missionRule.SpawnZone))
            return false;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        ValidateWaypointAreas(region, zone);
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        if (altitude == 0)
        {
            var damageMaximum = ufoRule.StatsForRace(mission.Race).DamageMaximum;
            if (ufo.Damage >= damageMaximum ||
                !ufoRule.Unmanned && ufo.HuntBehavior != 1 && ufo.Damage > damageMaximum / 2)
                return false; // Ufo::setAltitude would create a crash, owned by the later outcome slice.
            if (LandingAllowed(ufo.Destination!.Position, ufoRule) &&
                (trajectory.GroundTimer <= 0 || trajectory.GroundTimer > int.MaxValue / 5)) return false;
        }
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

    private UfoSnapshot AdvanceOrdinaryWaypoint(
        UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory, WorldPosition position)
    {
        var rules = campaign.Content.RuntimeRules;
        var nextWaypoint = ufo.TrajectoryPoint + 1;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        // The pinned ufoReachedWaypoint calls getWaypoint twice: the first result is
        // discarded on the ordinary path, and the second becomes the destination.
        // Preserve both selections, including land retries and their random choices.
        var randomPosition = default(WorldPosition);
        for (var selection = 0; selection < 2; selection++)
            randomPosition = nextWaypoint + 1 < trajectory.Waypoints.Count && trajectory.Altitude(nextWaypoint + 1) == 0
                ? WorldGeometry.LandPoint(region, rules.Campaign.Globe, trajectory.Zone(nextWaypoint),
                    ufoRule.FakeWaterLandingChance, campaign.Random)
                : WorldGeometry.RandomPoint(region, trajectory.Zone(nextWaypoint), -1, campaign.Random);
        // AlienMission::ufoReachedWaypoint assigns the raw regional point through
        // Target::setLongitude/setLatitude, which wrap coordinates across the globe.
        var nextPosition = WorldPosition.Create(randomPosition.Longitude, randomPosition.Latitude);
        var landed = trajectory.Altitude(nextWaypoint) == 0;
        var landingAllowed = landed && LandingAllowed(position, ufoRule);
        var speed = landed ? 0 : trajectory.Speed(nextWaypoint, ufoRule.StatsForRace(mission.Race).SpeedMaximum);
        var speedRadian = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(position, nextPosition, speedRadian);
        return ufo with
        {
            TrajectoryPoint = nextWaypoint,
            Longitude = position.Longitude,
            Latitude = position.Latitude,
            Altitude = WorldAltitudes.All[trajectory.Altitude(nextWaypoint)],
            Status = landed ? UfoStatus.Landed : UfoStatus.Flying,
            LandId = !landed ? 0 : landingAllowed && ufo.Detected && ufo.LandId == 0
                ? campaign.NextId("STR_LANDING_SITE") : ufo.LandId,
            SecondsRemaining = landed ? (landingAllowed ? checked(trajectory.GroundTimer * 5) : 5) : ufo.SecondsRemaining,
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

    private bool LandingAllowed(WorldPosition position, RuntimeUfoRule rule) =>
        WorldGeometry.InsideLand(campaign.Content.RuntimeRules.Campaign.Globe, position) &&
        (!WorldGeometry.InsideFakeUnderwaterTexture(campaign.Content.RuntimeRules.Campaign.Globe, position) ||
            rule.FakeWaterLandingChance > 0);

    private bool ArrivesOnGround(UfoSnapshot ufo)
    {
        if (ufo.Status != UfoStatus.Flying ||
            WorldGeometry.Distance(ufo.Position, ufo.Destination!.Position) > WorldGeometry.RadianSpeed(ufo.Speed))
            return false;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        var next = ufo.TrajectoryPoint + 1;
        return next < trajectory.Waypoints.Count && trajectory.Altitude(next) == 0 &&
            !FindMission(ufo.MissionId)!.Interrupted;
    }

    private AlienMissionSnapshot? FindMission(int id)
    {
        // Avoid captured predicates in per-tick preflight.
        foreach (var mission in _missions)
            if (mission.Id == id) return mission;
        return null;
    }

    private UfoSnapshot LiftUfo(UfoSnapshot ufo)
    {
        var rules = campaign.Content.RuntimeRules;
        var mission = FindMission(ufo.MissionId)!;
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        if (missionRule.Points > 0) ScoreUfoActivity(ufo.Position, missionRule.Points, rules);
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        var speed = trajectory.Speed(ufo.TrajectoryPoint,
            checked(rule.Stats.SpeedMaximum + rule.RaceBonus(mission.Race).SpeedMaximum));
        var radians = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(ufo.Position, ufo.Destination!.Position, radians);
        // ufoLifting retains the landing ID until the next airborne waypoint, and
        // does not move or recharge shields in this landed branch of time5Seconds.
        return ufo with
        {
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.VeryLow,
            SecondsRemaining = 0,
            Speed = speed,
            SpeedRadian = radians,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
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
            _ufos.RemoveAt(index);
        }
    }
}
