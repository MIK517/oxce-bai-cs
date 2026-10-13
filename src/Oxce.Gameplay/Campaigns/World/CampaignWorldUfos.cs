using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Ordinary UFO flight, landing and takeoff. Reference: Ufo::think/calculateSpeed,
/// AlienMission::ufoReachedWaypoint/ufoLifting and GeoscapeState::time5Seconds.
/// Special mission outcomes and combat arrivals remain guarded.
/// </summary>
internal sealed partial class CampaignWorld
{
    // GeoscapeState::time5Seconds returns when arrival destroys a UFO.
    private bool _ufoArrivalEndedTick;

    /// <summary>
    /// Ufo::getCraftStats().shieldCapacity, including the mission race bonus. Evaluated on
    /// demand so UFOs created or re-raced by later handlers can never be missed.
    /// </summary>
    private bool ValidUfoShields(UfoSnapshot ufo, AlienMissionSnapshot mission)
    {
        var rules = campaign.Content.RuntimeRules.Ufos;
        var rule = rules[rules.GetRequired(ufo.RuleId)].Value;
        var bonus = rule.RaceBonus(mission.Race);
        var capacity = (long)rule.Stats.ShieldCapacity + bonus.ShieldCapacity;
        var recharge = (long)rule.Stats.ShieldRechargeInGeoscape + bonus.ShieldRechargeInGeoscape;
        return capacity is >= 0 and <= int.MaxValue && recharge is >= int.MinValue and <= int.MaxValue &&
            WorldUfoShields.IsValid(ufo.Shield, (int)capacity, (int)recharge);
    }

    private UfoSnapshot RechargeUfoShield(UfoSnapshot ufo)
    {
        var rules = campaign.Content.RuntimeRules.Ufos;
        var rule = rules[rules.GetRequired(ufo.RuleId)].Value;
        var bonus = rule.RaceBonus(FindMission(ufo.MissionId)!.Race);
        var shield = WorldUfoShields.Recharge(ufo.Shield,
            checked(rule.Stats.ShieldCapacity + bonus.ShieldCapacity),
            checked(rule.Stats.ShieldRechargeInGeoscape + bonus.ShieldRechargeInGeoscape), campaign.Random);
        return shield == ufo.Shield ? ufo : ufo with { Shield = shield };
    }

    private void RegisterUfoOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldCrashExpiry,
            "crashed UFO expiry", ExpireCrashedUfos);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfos,
            "world UFO movement", MoveUfos);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfoCleanup,
            "destroyed UFO cleanup", RemoveDestroyedUfos);
        registry.Timed(CampaignTimeTrigger.TenMinutes, CampaignTimeOrder.TenMinutesWorldBaseDetection,
            "UFO detection of XCOM bases", DetectXcomBases);
    }

    private string? UfoMovementReason(CampaignTimeTrigger highest)
    {
        // A restored live count can become insufficient after another terminal arrival.
        // Keep this guard live and allocation-free; destroyed UFOs normally survive one tick.
        if (HasDestroyedUfoAfterTransitions(highest))
        {
            if ((long)_missions.Count * _ufos.Count > MaximumAlienBaseChecks)
                return "Destroyed UFO cleanup exceeds the supported work limit.";
            var terminal = 0;
            foreach (var target in _ufos)
            {
                if (!UfoDestroyedAfterTimers(target, highest)) continue;
                terminal++;
                if ((long)terminal * _ufos.Count > MaximumAlienBaseChecks)
                    return "Destroyed UFO follower cleanup exceeds the supported work limit.";
                foreach (var follower in _ufos)
                    if (follower.Escorting && FollowsUfo(follower.Destination, target) &&
                        follower.OriginalWaypoint is not { IsNormalized: true }) return HuntStateReason;
            }
            if ((long)terminal * _ufos.Count > MaximumAlienBaseChecks)
                return "Destroyed UFO follower cleanup exceeds the supported work limit.";
            foreach (var mission in _missions)
            {
                var destroyed = 0;
                foreach (var ufo in _ufos)
                    if (UfoDestroyedAfterTimers(ufo, highest) && ufo.MissionId == mission.Id)
                        destroyed++;
                if (destroyed > mission.LiveUfos)
                    return "Destroyed UFO count exceeds its mission's live count.";
            }
        }
        long siteLinkWork = 0;
        var movementEnded = false;
        for (var index = 0; index < _ufos.Count; index++)
        {
            if (_ufos[index] is { Status: UfoStatus.Crashed, InBattlescape: true })
                return "Crashed UFO battlescape transitions require tactical simulation.";
            var ufo = SharedFlightAt(index, highest, out var destinationDistance);
            if (_ufos[index].Status == UfoStatus.Crashed && ufo.Status == UfoStatus.Destroyed)
            {
                siteLinkWork += _sites.Count;
                if (siteLinkWork > MaximumAlienBaseChecks) return "Destroyed UFO cleanup exceeds the supported work limit.";
                foreach (var site in _sites)
                    if (site.UfoUniqueId == ufo.UniqueId)
                        return "Crashed UFO cannot expire while a mission site references it.";
            }
            if (ufo.Status == UfoStatus.Crashed) continue;
            // GeoscapeState skips ignored UFOs until their associated site lifts them.
            if (ufo.Status == UfoStatus.IgnoreMe) continue;
            if (ufo.Status == UfoStatus.Destroyed)
            {
                // The ten-minute scan also inspects these before five-second cleanup.
                continue;
            }
            if (ufo.Status is not (UfoStatus.Flying or UfoStatus.Landed) || ufo.InBattlescape)
                return "UFO state requires world simulation.";
            // Ufo::load and AlienMission::spawnUfo own this waypoint; unlike a player
            // waypoint, it is never assigned a STR_WAY_POINT identity.
            if (ufo.Destination is not { } destination)
                return "UFO destination requires world simulation.";
            if (!ufo.Hunting && !ufo.Escorting && destination is not { Kind: WorldTargetKind.Waypoint, Id: 0 })
                return "UFO destination requires world simulation.";
            // Ordinary saves validate this link at restore, and supported time handlers
            // cannot remove a mission while one of its UFOs is still flying.
            if (campaign.MonthsPassed == -1 && FindMission(ufo.MissionId) is null)
                return "UFO mission link requires world simulation.";
            if (FindMission(ufo.MissionId) is { } shieldMission && !ValidUfoShields(ufo, shieldMission))
                return "UFO shield arithmetic exceeds the supported range.";
            if (ufo.Speed < 0)
                return "UFO speed is invalid.";
            if (ufo.Status == UfoStatus.Landed)
            {
                if (ufo.Altitude != WorldAltitudes.Ground || ufo.SecondsRemaining < 5 || ufo.SecondsRemaining % 5 != 0)
                    return "Landed UFO timer or altitude is invalid.";
                var mission = FindMission(ufo.MissionId)!;
                if (!TryUfoLiftSpeed(ufo, mission, out _))
                    return "UFO speed is invalid.";
                if (ufo.SecondsRemaining == 5 && UfoLiftTargetReason(ufo, highest, ref siteLinkWork) is { } liftReason)
                    return liftReason;
                continue;
            }
            if (ufo.Hunting || ufo.Escorting)
            {
                if (ufo.OriginalWaypoint is not { IsNormalized: true }) return HuntStateReason;
                if (ufo.Hunting && destination.Kind != WorldTargetKind.Craft ||
                    ufo.Escorting && destination.Kind != WorldTargetKind.Ufo &&
                    !ReferenceEquals(_pass.NewLeaderFlightForecast?.GetValueOrDefault(ufo.UniqueId), ufo))
                    return HuntStateReason;
                if (ufo.Escorting && TargetUfo(destination) is { } escorted)
                {
                    siteLinkWork += 2L * _ufos.Count;
                    if (siteLinkWork > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                }
                // A hunter-killer reaching its craft starts an interception (branch 5 S4); the craft pass
                // then neither moves, recharges shields nor arrives this tick. While another hunter-killer
                // fight is open the attacker only waits, so the craft pass runs as usual.
                if (!movementEnded && ufo.Hunting && !_interceptions.Any(static d => d.UfoIsAttacking) && ForecastHuntingTarget(destination) is { Logistics: { } targetState } targetCraft &&
                    targetState.Damage < CraftLogistics.EffectiveDamageMaximum(targetState,
                        campaign.Content.RuntimeRules.Crafts[targetCraft.Rule].Value, campaign.Content.RuntimeRules) &&
                    WorldGeometry.Distance(ufo.Position, destination.Position) <= WorldGeometry.RadianSpeed(ufo.Speed))
                    _pass.HunterAttackForecast = true;
                continue;
            }
            if (!movementEnded && EndsUfoFlightPass(index, highest)) movementEnded = true;
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            // MovingTarget::move snaps to this stationary waypoint when distance is
            // not greater than one step, including the reference's NaN fallback.
            if (!(destinationDistance > speedRadian))
            {
                _pass.ArrivalLossFollowerScanRequired = true;
                if (FindMission(ufo.MissionId) is null) return "UFO mission link requires world simulation.";
                var mission = ForecastMission(ufo.MissionId, highest);
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (mission.AlienBase is not null)
                {
                    siteLinkWork += _alienBases.Count;
                    if (siteLinkWork > MaximumAlienBaseChecks)
                        return "UFO operation-base lookup exceeds the supported work limit.";
                }
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count)
                {
                    var rule = MissionRule(mission);
                    if (rule.Objective == RuntimeMissionObjective.Retaliation && campaign.Options.AggressiveRetaliation &&
                        trajectory.Zone(ufo.TrajectoryPoint + 1) != 5)
                    {
                        var region = campaign.Content.RuntimeRules.Regions[campaign.Content.RuntimeRules.Regions.GetRequired(mission.RegionId)].Value;
                        siteLinkWork += (long)campaign.BaseStates.Count * (1L + region.Areas.Count);
                        if (siteLinkWork > MaximumAlienBaseChecks) return "Aggressive retaliation base selection exceeds the supported work limit.";
                    }
                    if (IsBaseAssaultArrival(ufo, trajectory) && !IsSiteConversion(ufo, mission, trajectory))
                    {
                        // The base assault ends this five-second pass (branch 5 S7). Its waypoint draws read
                        // the next zone, which CanAdvanceOrdinaryWaypoint would otherwise have validated.
                        ValidateWaypointAreas(campaign.Content.RuntimeRules.Regions[
                            campaign.Content.RuntimeRules.Regions.GetRequired(mission.RegionId)].Value, trajectory.Zone(ufo.TrajectoryPoint + 1));
                        if (BaseAssaultReason(ufo, mission, destination.Position) is { } assaultReason) return assaultReason;
                    }
                    else if (!CanAdvanceOrdinaryWaypoint(ufo, mission, trajectory))
                        return "UFO waypoint arrival requires mission simulation.";
                    // Crafts react to the new CRASHED status later in this time5Seconds pass: a landing
                    // confirmation or the return home (branch 5 S6). Escorts losing a crashed leader
                    // are not simulated yet.
                    if (CrashesOnGroundArrival(ufo, mission, trajectory) && HasEscortFollowers(ufo))
                        return "Crashed UFO arrival with escorts requires escort simulation.";
                    var regionForWaypoint = campaign.Content.RuntimeRules.Regions[
                        campaign.Content.RuntimeRules.Regions.GetRequired(mission.RegionId)].Value;
                    var wave = rule.Waves[MissionWaveIndex(ufo, mission, rule)];
                    _pass.UfoWaypointLandWork += 2 * OrdinaryWaypointLandWork(mission, rule, wave, trajectory,
                        regionForWaypoint, ufo.TrajectoryPoint + 1);
                    if (_pass.UfoWaypointLandWork > MaximumSpawnLandWork)
                        return "UFO waypoint land selection exceeds the supported work limit.";
                }
            }
        }
        return null;
    }

    private void MoveUfos(CampaignState.TimeEffects effects)
    {
        _ufoArrivalEndedTick = false;
        _ufoIsAttackingThisTick = false;
        // GeoscapeState::time5Seconds returns once the campaign has ended.
        if (campaign.Ending != 0)
        {
            _ufoArrivalEndedTick = true;
            return;
        }
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status == UfoStatus.Crashed)
            {
                _ufos[index] = WorldCrashedUfo.FiveSeconds(ufo);
                continue;
            }
            if (ufo.Status == UfoStatus.Landed)
            {
                _ufos[index] = ufo.SecondsRemaining == 5 ? LiftUfo(ufo) :
                    ufo with { SecondsRemaining = ufo.SecondsRemaining - 5 };
                continue;
            }
            if (ufo.Status != UfoStatus.Flying) continue;
            if ((ufo.Hunting || ufo.Escorting) && Resolve(ufo.Destination) is { } target &&
                !SameTargetCoordinates(ufo.Destination!, target.Longitude, target.Latitude))
                ufo = ufo with { Destination = target };
            var destination = ufo.Destination!;
            var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
            var vector = WorldGeometry.SpeedVector(ufo.Position, destination.Position, speedRadian);
            var distance = WorldGeometry.Distance(ufo.Position, destination.Position);
            var moved = WorldGeometry.Move(ufo.Position, destination.Position, speedRadian, vector, distance);
            if (ufo.Hunting && !(distance > speedRadian))
            {
                // A hunter-killer reached its craft: the attack starts and the UFO skips its shield recharge.
                var arrived = _ufos[index];
                _ufos[index] = ufo with
                {
                    Longitude = moved.Longitude,
                    Latitude = moved.Latitude,
                    SpeedRadian = speedRadian,
                    SpeedLongitude = vector.Longitude,
                    SpeedLatitude = vector.Latitude,
                    Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
                };
                if (TryHunterKillerAttack(index)) continue;
                _ufos[index] = arrived;
            }
            if (!ufo.Hunting && !ufo.Escorting && !(distance > speedRadian))
            {
                var mission = FindMission(ufo.MissionId)!;
                var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
                if (!mission.Interrupted && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count)
                {
                    if (IsBaseAssaultArrival(ufo, trajectory) && !IsSiteConversion(ufo, mission, trajectory))
                    {
                        ArriveForBaseAssault(index, ufo, mission, trajectory, moved, effects);
                        _ufoArrivalEndedTick = true;
                        break;
                    }
                    if (!CanAdvanceOrdinaryWaypoint(ufo, mission, trajectory))
                        throw new InvalidOperationException("UFO arrival changed after world preflight.");
                    var arrived = AdvanceOrdinaryWaypoint(ufo, mission, trajectory, moved);
                    if (IsSiteConversion(ufo, mission, trajectory))
                    {
                        _ufos[index] = ConvertUfoToSite(arrived, mission, effects);
                        if (_ufos[index].Status == UfoStatus.Destroyed)
                        {
                            _ufoArrivalEndedTick = true;
                            break;
                        }
                        _ufos[index] = RechargeUfoShield(_ufos[index]);
                        continue;
                    }
                    if (CreatesLandingBase(ufo, mission, trajectory))
                    {
                        var rule = MissionRule(mission);
                        var handle = WorldObjectiveBaseSpawn.Deployment(campaign.Content.RuntimeRules, rule,
                            new(0, 0, 0, 0, 0, ""), campaign.Random);
                        SpawnMissionAlienBase(mission, rule, handle, arrived.Position);
                    }
                    // The switch entered its FLYING branch, so this also recharges on landing.
                    _ufos[index] = RechargeUfoShield(arrived);
                    if (campaign.Options.UfoLandingAlert && arrived.Status == UfoStatus.Landed &&
                        arrived.Detected && arrived.LandId != 0)
                        effects.Notify(new UfoLanded(arrived.UniqueId, arrived.LandId));
                    continue;
                }
                // Ufo::think stops at the waypoint; AlienMission::ufoReachedWaypoint
                // destroys an interrupted UFO or one at the trajectory's last point.
                if (ufo.Detected && HasArrivalContactFollowers(ufo))
                    effects.Notify(new UfoContactLost(ufo.UniqueId) { MarkerId = ufo.Id });
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
            _ufos[index] = RechargeUfoShield(ufo with
            {
                Longitude = moved.Longitude,
                Latitude = moved.Latitude,
                SpeedRadian = speedRadian,
                SpeedLongitude = vector.Longitude,
                SpeedLatitude = vector.Latitude,
                Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
            });
        }
    }

    private string? ArrivalContactLossReason(CampaignTimeTrigger highest)
    {
        if (!_pass.ArrivalLossFollowerScanRequired) return null;
        // Destruction returns from the entire flight pass, so at most one loss
        // scan can execute. Mission preflight has already bounded saved/new UFOs.
        var maximumUfos = highest >= CampaignTimeTrigger.ThirtyMinutes || _pass.GeneratedWaveMissions is { Count: > 0 }
            ? 100_000L : _ufos.Count;
        long work = campaign.BaseStates.Count + maximumUfos;
        if (campaign.ProductionPreflight.CraftPopulationBound(highest, ref work, out var crafts) is { } reason) return reason;
        return work + crafts > MaximumAlienBaseChecks ? "UFO arrival follower checks exceed the supported work limit." : null;
    }

    private bool HasEscortFollowers(UfoSnapshot ufo)
    {
        foreach (var follower in _ufos)
            if (FollowsUfo(follower.Destination, ufo)) return true;
        return false;
    }

    private bool HasArrivalContactFollowers(UfoSnapshot ufo)
    {
        // MovingTarget::setDestination registers both UFO and craft followers.
        foreach (var follower in _ufos)
            if (FollowsUfo(follower.Destination, ufo)) return true;
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in owner.Crafts)
                if (FollowsUfo(craft.Logistics?.Destination, ufo)) return true;
        return false;
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
        var waveIndex = MissionWaveIndex(ufo, mission, missionRule);
        if ((uint)waveIndex >= (uint)missionRule.Waves.Count) return false;
        var wave = missionRule.Waves[waveIndex];
        var zone = trajectory.Zone(nextWaypoint);
        // AlienMission::getWaypoint only takes its mission-site path for an objective
        // wave in the mission's spawn zone. The other objective flags do not affect
        // ordinary regional waypoint selection.

        var conversion = IsSiteConversion(ufo, mission, trajectory);
        if (altitude == 0 && ufo.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun && !conversion)
            return false;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        ValidateWaypointAreas(region, zone);
        if (mission.MissionSiteZoneArea != -1 && wave.Objective && zone == missionRule.SpawnZone)
            _ = SelectedMissionArea(region, zone, mission.MissionSiteZoneArea);
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        if (altitude == 0)
        {
            if (!conversion && LandingAllowed(ufo.Destination!.Position, ufoRule) &&
                (trajectory.GroundTimer <= 0 || trajectory.GroundTimer > int.MaxValue / 5)) return false;
        }
        return trajectory.Speed(nextWaypoint, ufoRule.StatsForRace(mission.Race).SpeedMaximum) >= 0;
    }

    /// <summary>
    /// Ufo::setAltitude marks a damaged UFO CRASHED instead of LANDED on any ground
    /// waypoint (Ufo::isCrashed). Site conversion replaces that transient status.
    /// </summary>
    private bool CrashesOnGroundArrival(UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory)
    {
        var next = ufo.TrajectoryPoint + 1;
        if (next >= trajectory.Waypoints.Count || trajectory.Altitude(next) != 0 ||
            IsSiteConversion(ufo, mission, trajectory)) return false;
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        return IsCrashed(ufo, ufoRule, ufoRule.StatsForRace(mission.Race).DamageMaximum);
    }

    /// <summary>Ufo::isCrashed: destroyed, or past half damage unless kamikaze or unmanned.</summary>
    private static bool IsCrashed(UfoSnapshot ufo, RuntimeUfoRule rule, int damageMaximum) =>
        ufo.Damage >= damageMaximum || !rule.Unmanned && ufo.HuntBehavior != 1 && ufo.Damage > damageMaximum / 2;

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
        var missionRule = MissionRule(mission);
        var wave = missionRule.Waves[MissionWaveIndex(ufo, mission, missionRule)];
        _ = OrdinaryWaypoint(mission, trajectory, nextWaypoint, region, ufoRule, wave);
        if (campaign.Options.AggressiveRetaliation && missionRule.Objective == RuntimeMissionObjective.Retaliation &&
            trajectory.Zone(nextWaypoint) != 5)
            foreach (var owner in campaign.BaseStates)
                if (WorldGeometry.InsideRegion(region, new(owner.Longitude, owner.Latitude)))
                {
                    // The reference shadows this result with its second getWaypoint call.
                    var radius = Math.PI / 12;
                    _ = WorldGeometry.RandomDouble(campaign.Random, owner.Longitude - radius, owner.Longitude + radius);
                    _ = WorldGeometry.RandomDouble(campaign.Random, owner.Latitude - radius, owner.Latitude + radius);
                    break;
                }
        var randomPosition = OrdinaryWaypoint(mission, trajectory, nextWaypoint, region, ufoRule, wave);
        // AlienMission::ufoReachedWaypoint assigns the raw regional point through
        // Target::setLongitude/setLatitude, which wrap coordinates across the globe.
        var nextPosition = WorldPosition.Create(randomPosition.Longitude, randomPosition.Latitude);
        var landed = trajectory.Altitude(nextWaypoint) == 0;
        var conversion = IsSiteConversion(ufo, mission, trajectory);
        var landingAllowed = landed && LandingAllowed(position, ufoRule);
        // The ground branch still assigns the landing timer and marker to a crashed UFO.
        var crashed = landed && !conversion &&
            IsCrashed(ufo, ufoRule, ufoRule.StatsForRace(mission.Race).DamageMaximum);
        var speed = landed ? 0 : trajectory.Speed(nextWaypoint, ufoRule.StatsForRace(mission.Race).SpeedMaximum);
        var speedRadian = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(position, nextPosition, speedRadian);
        return ufo with
        {
            TrajectoryPoint = nextWaypoint,
            Longitude = position.Longitude,
            Latitude = position.Latitude,
            Altitude = WorldAltitudes.All[trajectory.Altitude(nextWaypoint)],
            Status = crashed ? UfoStatus.Crashed : landed ? UfoStatus.Landed : UfoStatus.Flying,
            LandId = conversion ? ufo.LandId : !landed ? 0 : landingAllowed && ufo.Detected && ufo.LandId == 0
                ? campaign.NextId("STR_LANDING_SITE") : ufo.LandId,
            SecondsRemaining = landed && !conversion ? (landingAllowed ? checked(trajectory.GroundTimer * 5) : 5) : ufo.SecondsRemaining,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, nextPosition.Longitude, nextPosition.Latitude),
            Speed = speed,
            SpeedRadian = speedRadian,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
        };
    }

    private WorldPosition OrdinaryWaypoint(AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory,
        int point, RuntimeRegionRule region, RuntimeUfoRule ufoRule, RuntimeMissionWave wave)
    {
        var rules = campaign.Content.RuntimeRules;
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        // getWaypoint validates the zone before choosing the final operation-base destination.
        var areas = WorldGeometry.MissionAreas(region, trajectory.Zone(point));
        if (mission.MissionSiteZoneArea != -1 && wave.Objective && trajectory.Zone(point) == missionRule.SpawnZone)
        {
            var area = SelectedMissionArea(region, missionRule.SpawnZone, mission.MissionSiteZoneArea);
            return wave.ObjectiveOnTheLandingSite
                ? WorldGeometry.MissionSiteLandPoint(region, rules.Campaign.Globe, missionRule.SpawnZone,
                    mission.MissionSiteZoneArea, ufoRule.FakeWaterLandingChance, campaign.Random)
                : new WorldPosition(area.LongitudeMinimum, area.LatitudeMinimum);
        }
        if (missionRule.OperationType != RuntimeMissionOperationType.Space &&
            point >= trajectory.Waypoints.Count - 1 && MissionSourceBase(mission) is { } source)
            return source.Position;
        return point + 1 < trajectory.Waypoints.Count && trajectory.Altitude(point + 1) == 0
            ? WorldGeometry.LandPoint(region, rules.Campaign.Globe, trajectory.Zone(point),
                ufoRule.FakeWaterLandingChance, campaign.Random)
            : WorldGeometry.RandomPoint(region, trajectory.Zone(point), -1, campaign.Random);
    }

    private bool LandingAllowed(WorldPosition position, RuntimeUfoRule rule) =>
        WorldGeometry.InsideLand(campaign.Content.RuntimeRules.Campaign.Globe, position) &&
        (!WorldGeometry.InsideFakeUnderwaterTexture(campaign.Content.RuntimeRules.Campaign.Globe, position) ||
            rule.FakeWaterLandingChance > 0);

    private bool ArrivesOnGround(UfoSnapshot ufo, CampaignTimeTrigger highest)
    {
        if (ufo.Hunting || ufo.Escorting || ufo.Status != UfoStatus.Flying ||
            UfoDestinationDistance(ufo) > WorldGeometry.RadianSpeed(ufo.Speed))
            return false;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        var next = ufo.TrajectoryPoint + 1;
        var mission = ForecastMission(ufo.MissionId, highest);
        if (IsSiteConversion(ufo, mission, trajectory)) return false;
        return next < trajectory.Waypoints.Count && trajectory.Altitude(next) == 0 &&
            !mission.Interrupted;
    }

    private double UfoDestinationDistance(UfoSnapshot ufo)
    {
        var destination = ufo.Destination!;
        var key = (BitConverter.DoubleToInt64Bits(ufo.Longitude), BitConverter.DoubleToInt64Bits(ufo.Latitude),
            BitConverter.DoubleToInt64Bits(destination.Longitude), BitConverter.DoubleToInt64Bits(destination.Latitude));
        if (!_pass.UfoDestinationDistances.TryGetValue(key, out var distance))
        {
            distance = WorldGeometry.Distance(ufo.Position, destination.Position);
            _pass.UfoDestinationDistances.Add(key, distance);
        }
        return distance;
    }

    private AlienMissionSnapshot? FindMission(int id)
    {
        // Avoid captured predicates in per-tick preflight.
        foreach (var mission in _missions)
            if (mission.Id == id) return mission;
        return null;
    }

    // Shared by preflight and execution, without allocating a projected UFO each
    // tick of a long ground stay. Validate both the effective maximum and percentage result.
    private bool TryUfoLiftSpeed(UfoSnapshot ufo, AlienMissionSnapshot mission, out int speed)
    {
        speed = 0;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        if (!rules.UfoTrajectories.TryGet(ufo.TrajectoryId, out var trajectoryId)) return false;
        var trajectory = rules.UfoTrajectories[trajectoryId].Value;
        if ((uint)ufo.TrajectoryPoint >= (uint)trajectory.Waypoints.Count) return false;
        var maximum = (long)rule.Stats.SpeedMaximum + rule.RaceBonus(mission.Race).SpeedMaximum;
        if (maximum is < 0 or > int.MaxValue) return false;
        var result = maximum * trajectory.Waypoints[ufo.TrajectoryPoint].SpeedPercentage / 100;
        if (result is < 0 or > int.MaxValue) return false;
        speed = (int)result;
        return true;
    }

    private bool TryLiftUfo(UfoSnapshot ufo, bool fromSite, out UfoSnapshot lifted, out int score)
    {
        lifted = ufo;
        score = 0;
        var mission = FindMission(ufo.MissionId);
        if (mission is null || !TryUfoLiftSpeed(ufo, mission, out var speed)) return false;
        var rules = campaign.Content.RuntimeRules;
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        (lifted, score) = WorldUfoLift.Advance(ufo, speed, missionRule.Points, missionRule.Objective, fromSite);
        return true;
    }

    private string? UfoLiftTargetReason(UfoSnapshot ufo, CampaignTimeTrigger highest, ref long work)
    {
        if (!ufo.Hunting && !ufo.Escorting) return null;
        if (ufo.Destination is { Kind: WorldTargetKind.Ufo }) work += _ufos.Count;
        else if (ufo.Destination is { Kind: WorldTargetKind.Craft })
        {
            if (campaign.ProductionPreflight.CraftPopulationBound(highest, ref work, out var crafts) is { } reason) return reason;
            work += crafts + campaign.BaseStates.Count;
        }
        return work > MaximumAlienBaseChecks ? "UFO takeoff target lookup exceeds the supported work limit." : null;
    }

    private UfoSnapshot LiftUfo(UfoSnapshot ufo, bool fromSite = false)
    {
        // Native target pointers see earlier actors after movement. setSpeed
        // recalculates the lift vector without moving the landed UFO itself.
        if ((ufo.Hunting || ufo.Escorting) && ufo.Destination is { Kind: WorldTargetKind.Ufo or WorldTargetKind.Craft } &&
            Resolve(ufo.Destination) is { } target &&
            !SameTargetCoordinates(ufo.Destination, target.Longitude, target.Latitude))
            ufo = ufo with { Destination = target };
        if (!TryLiftUfo(ufo, fromSite, out var lifted, out var score))
            throw new InvalidOperationException("UFO takeoff changed after world preflight.");
        if (score != 0) ScoreUfoActivity(ufo.Position, score, campaign.Content.RuntimeRules);
        // ufoLifting retains the landing ID until the next airborne waypoint, and
        // does not move or recharge shields in this landed branch of time5Seconds.
        return lifted;
    }

    private static int MissionWaveIndex(
        UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule)
        => WorldMissionWave.Infer(ufo.MissionWaveNumber, mission.NextWave, missionRule.Waves.Count);

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
            for (var followerIndex = 0; followerIndex < _ufos.Count; followerIndex++)
                if (_ufos[followerIndex].Escorting && FollowsUfo(_ufos[followerIndex].Destination, ufo))
                    _ufos[followerIndex] = WorldHunting.Restore(_ufos[followerIndex]);
            _ufos.RemoveAt(index);
        }
    }
}
