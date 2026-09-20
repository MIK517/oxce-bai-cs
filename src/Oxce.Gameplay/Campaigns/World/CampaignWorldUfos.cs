namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The bounded flying-UFO transit path. Reference: Ufo::think/calculateSpeed and
/// GeoscapeState::time5Seconds. Arrival, interception and ten-minute detection stay guarded.
/// </summary>
internal sealed partial class CampaignWorld
{
    // GeoscapeState::time5Seconds returns when arrival destroys a UFO.
    private bool _ufoArrivalEndedTick;

    private void RegisterUfoOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfos,
            "world UFO movement", MoveUfos);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfoCleanup,
            "destroyed UFO cleanup", RemoveDestroyedUfos);
    }

    private string? UfoMovementReason(CampaignTimeTrigger highest)
    {
        if (_ufos.Exists(static ufo => ufo.Status == UfoStatus.Destroyed))
        {
            for (var missionIndex = 0; missionIndex < _missions.Count; missionIndex++)
            {
                var mission = _missions[missionIndex];
                var destroyed = 0;
                for (var ufoIndex = 0; ufoIndex < _ufos.Count; ufoIndex++)
                    if (_ufos[ufoIndex].Status == UfoStatus.Destroyed && _ufos[ufoIndex].MissionId == mission.Id)
                        destroyed++;
                if (destroyed > mission.LiveUfos)
                    return "Destroyed UFO count exceeds its mission's live count.";
            }
        }
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status == UfoStatus.Destroyed) continue;
            if (ufo.Status != UfoStatus.Flying || ufo.InBattlescape || ufo.Hunting ||
                ufo.Escorting || ufo.HunterKiller || ufo.Escort)
                return "UFO state requires world simulation.";
            if (highest >= CampaignTimeTrigger.TenMinutes)
                return "UFO detection and retargeting require world simulation.";
            // Ufo::load and AlienMission::spawnUfo own this waypoint; unlike a player
            // waypoint, it is never assigned a STR_WAY_POINT identity.
            if (ufo.Destination is not { Kind: WorldTargetKind.Waypoint, Id: 0 } destination)
                return "UFO destination requires world simulation.";
            AlienMissionSnapshot? mission = null;
            for (var missionIndex = 0; missionIndex < _missions.Count; missionIndex++)
                if (_missions[missionIndex].Id == ufo.MissionId)
                {
                    mission = _missions[missionIndex];
                    break;
                }
            if (mission is null) return "UFO mission link requires world simulation.";
            var rule = campaign.Content.RuntimeRules.Ufos[
                campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value;
            var shieldCapacity = (long)rule.Stats.ShieldCapacity + rule.RaceBonus(mission.Race).ShieldCapacity;
            if (shieldCapacity != 0 || ufo.Shield is not (-1 or 0))
                return "UFO shield handling requires world simulation.";
            if (ufo.Speed < 0)
                return "UFO speed is invalid.";
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            // MovingTarget::move snaps to this stationary waypoint when distance is
            // not greater than one step, including the reference's NaN fallback.
            if (!(WorldGeometry.Distance(ufo.Position, destination.Position) > speedRadian))
            {
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count)
                    return "UFO waypoint arrival requires mission simulation.";
            }
        }
        return null;
    }

    private void MoveUfos(CampaignState.TimeEffects _)
    {
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

    private void RemoveDestroyedUfos(CampaignState.TimeEffects _)
    {
        if (_ufoArrivalEndedTick)
        {
            _ufoArrivalEndedTick = false;
            return;
        }
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
