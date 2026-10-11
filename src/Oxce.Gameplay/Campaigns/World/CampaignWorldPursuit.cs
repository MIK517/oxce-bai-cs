using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::time5Seconds craft pursuit and target-loss handling.</summary>
internal sealed partial class CampaignWorld
{
    private const string InterceptionReason = WorldCommandReasons.InterceptionBoundary;
    private const string LandingReason = WorldCommandReasons.LandingBoundary;

    private CampaignCommandResult DispatchTarget(DispatchCraftToTarget command)
    {
        ArgumentNullException.ThrowIfNull(command.Target);
        var (owner, index) = FindCraft(command.BaseId, command.CraftTypeId, command.CraftId);
        var craft = owner.Crafts[index];
        if (DispatchReason(owner, craft) is { } reason) return Blocked(reason);
        var target = command.Target;
        if (DestinationReason(target, out var resolved) is { } destinationReason) return Blocked(destinationReason);
        if (target.Kind == WorldTargetKind.Craft && target.Id == craft.Id && target.TypeId == command.CraftTypeId)
            return Blocked(WorldCommandReasons.TargetSelf);
        if (target.Kind == WorldTargetKind.Base && target.Id != owner.Id)
            return Blocked(WorldCommandReasons.ForeignBase);
        if (TargetDeployment(resolved) is { StartingCondition.Length: > 0 })
            return Blocked(WorldCommandReasons.StartingConditionBoundary);
        var state = craft.Logistics!;
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out var speed);
        owner.Crafts[index] = craft with
        {
            Logistics = SetDestination(state with { IsAutoPatrolling = state.IsAutoPatrolling && !rule.AutoPatrol },
                speed, resolved),
        };
        return new([new CraftDestinationChanged(owner.Id, command.CraftTypeId, craft.Id, resolved)]);
    }

    /// <summary>Resolves a visible destination to the reference a craft stores.</summary>
    private string? DestinationReason(WorldTargetReference target, out WorldTargetReference resolved)
    {
        resolved = target;
        if (!Enum.IsDefined(target.Kind)) return WorldCommandReasons.TargetKind;
        if (target.Kind == WorldTargetKind.Ufo && target.UniqueId <= 0)
            return WorldCommandReasons.UfoIdentity;
        if (Resolve(target) is not { } found || !IsVisibleTarget(found))
            return WorldCommandReasons.TargetGone;
        resolved = found.Kind switch
        {
            WorldTargetKind.Ufo => found with { TypeId = WorldTargetReference.UfoType, Id = TargetUfo(found)!.Id },
            WorldTargetKind.Base => found with { TypeId = WorldTargetReference.BaseType },
            WorldTargetKind.Waypoint => found with { TypeId = WorldTargetReference.WaypointType },
            _ => found,
        };
        return null;
    }

    private UfoSnapshot? TargetUfo(WorldTargetReference reference)
    {
        foreach (var ufo in _ufos)
            if (reference.UniqueId > 0 ? ufo.UniqueId == reference.UniqueId : ufo.Id == reference.Id) return ufo;
        return null;
    }

    private CampaignState.CraftState? TargetCraft(WorldTargetReference reference)
    {
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in owner.Crafts)
                if (craft.Id == reference.Id && campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule) == reference.TypeId)
                    return craft;
        return null;
    }

    private AlienBaseSnapshot? TargetAlienBase(WorldTargetReference reference)
    {
        foreach (var alienBase in _alienBases)
            if (FollowsAlienBase(reference, alienBase)) return alienBase;
        return null;
    }

    private bool IsVisibleTarget(WorldTargetReference reference) => reference.Kind switch
    {
        WorldTargetKind.Ufo => TargetUfo(reference) is { Detected: true, Status: not (UfoStatus.Destroyed or UfoStatus.IgnoreMe) },
        WorldTargetKind.Craft => TargetCraft(reference)?.Logistics is { Status: "STR_OUT" },
        WorldTargetKind.Base => campaign.BaseStates.Any(owner => owner.Id == reference.Id && owner.IsPlaced),
        WorldTargetKind.MissionSite => _sites.Any(site => site.Id == reference.Id && site.Detected &&
            MarkerName(site.DeploymentId) == reference.TypeId),
        WorldTargetKind.AlienBase => _alienBases.Any(alienBase => alienBase.Id == reference.Id && alienBase.Discovered &&
            MarkerName(alienBase.DeploymentId) == reference.TypeId),
        WorldTargetKind.Waypoint => true, // Resolve already checked existence.
        _ => false,
    };

    private RuntimeAlienDeploymentRule? TargetDeployment(WorldTargetReference reference)
    {
        var id = reference.Kind switch
        {
            WorldTargetKind.Ufo => TargetUfo(reference)?.RuleId,
            WorldTargetKind.MissionSite => _sites.Find(site => site.Id == reference.Id &&
                MarkerName(site.DeploymentId) == reference.TypeId)?.DeploymentId,
            WorldTargetKind.AlienBase => _alienBases.Find(alienBase => alienBase.Id == reference.Id &&
                MarkerName(alienBase.DeploymentId) == reference.TypeId)?.DeploymentId,
            _ => null,
        };
        return id is not null && campaign.Content.RuntimeRules.AlienDeployments.TryGet(id, out var rule)
            ? campaign.Content.RuntimeRules.AlienDeployments[rule].Value : null;
    }

    private bool CanLand(CampaignState.BaseState owner, CampaignState.CraftState craft, CraftLogisticsState state)
    {
        if (!campaign.Content.RuntimeRules.Crafts[craft.Rule].Value.AllowLanding ||
            campaign.Options.SuppressLandingWithoutEquipment && state.Items.Count == 0) return false;
        if (state.Vehicles.Count != 0) return true;
        var type = campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule);
        return owner.Soldiers.Any(soldier => soldier.Personal is { } personal &&
            personal.CraftType == type && personal.CraftId == craft.Id);
    }

    // Only deterministic position/status projection is needed here. Random waypoint selection
    // happens after the UFO reaches its old destination and cannot change that arrival position.
    // Preflight neither executes scripts nor consumes RNG.
    private (WorldPosition Position, UfoStatus Status, int Speed) ForecastUfo(UfoSnapshot ufo,
        CampaignTimeTrigger highest, bool needPosition = true) =>
        ForecastUfoStep(ProjectFlightTarget(UfoForFlight(ufo, highest), highest), highest, needPosition);

    private (WorldPosition Position, UfoStatus Status, int Speed) ForecastUfoStep(UfoSnapshot ufo,
        CampaignTimeTrigger highest, bool needPosition = true)
    {
        if (ufo.Status == UfoStatus.Landed)
            return ufo.SecondsRemaining == 5 ? (ufo.Position, UfoStatus.Flying,
                campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value.Speed(
                        ufo.TrajectoryPoint, campaign.Content.RuntimeRules.Ufos[
                            campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value.StatsForRace(
                                FindMission(ufo.MissionId)!.Race).SpeedMaximum)) : (ufo.Position, ufo.Status, ufo.Speed);
        if (ufo.Status != UfoStatus.Flying) return (ufo.Position, ufo.Status, ufo.Speed);
        var speedRadian = WorldGeometry.RadianSpeed(ufo.Speed);
        var distance = UfoDestinationDistance(ufo);
        var destination = ufo.Destination!.Position;
        // Status-only callers skip MovingTarget::move. The speed vector is only used for a step
        // that does not reach the destination; an arriving step snaps to it.
        var position = !needPosition ? ufo.Position : WorldGeometry.Move(ufo.Position, destination, speedRadian,
            distance > speedRadian ? WorldGeometry.SpeedVector(ufo.Position, destination, speedRadian) : default, distance);
        if (distance > speedRadian)
            return (position, ufo.Status, ufo.Speed);
        if (ufo.Hunting || ufo.Escorting) return (position, ufo.Status, ufo.Speed);
        var mission = ForecastMission(ufo.MissionId, highest);
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        if (mission.Interrupted || ufo.TrajectoryPoint + 1 >= trajectory.Waypoints.Count)
            return (position, UfoStatus.Destroyed, 0);
        if (IsSiteConversion(ufo, mission, trajectory))
            return (position, MissionRule(mission).RespawnUfoAfterSiteDespawn && _pass.SuppressedUfoSites?.Contains(ufo.UniqueId) != true
                ? UfoStatus.IgnoreMe : UfoStatus.Destroyed, 0);
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var landing = trajectory.Altitude(ufo.TrajectoryPoint + 1) == 0;
        return (position, landing ? UfoStatus.Landed : UfoStatus.Flying,
            landing ? 0 : trajectory.Speed(ufo.TrajectoryPoint + 1, rule.StatsForRace(mission.Race).SpeedMaximum));
    }

    /// <summary>Whether any UFO's arrival ends this tick's flight pass; independent of the craft asking.</summary>
    private bool AnyUfoEndsFlightPass(CampaignTimeTrigger highest)
    {
        foreach (var candidate in _ufos)
            if (EndsUfoFlightPass(candidate, highest)) return true;
        return false;
    }

    private bool EndsUfoFlightPass(UfoSnapshot ufo, CampaignTimeTrigger highest) =>
        UfoForFlight(ufo, highest).Status == UfoStatus.Flying &&
        ForecastUfo(ufo, highest, needPosition: false).Status == UfoStatus.Destroyed;

    private string? CraftArrivalReason(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, CampaignTimeTrigger highest, ref bool? terminalUfoArrival)
    {
        if (state.Destination is { Kind: WorldTargetKind.Craft } escort && escort.Id == craft.Id &&
            escort.TypeId == campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule))
            return WorldCommandReasons.TargetSelf;
        if (state.Destination is not { } destination ||
            destination.Kind is WorldTargetKind.Base or WorldTargetKind.Waypoint or WorldTargetKind.Craft) return null;
        if (highest >= CampaignTimeTrigger.OneDay && destination.Kind == WorldTargetKind.AlienBase)
        {
            var guaranteed = GuaranteedDailyResearch();
            foreach (var alienBase in _alienBases)
                if (FollowsAlienBase(destination, alienBase) && BaseDestroyedAfterDailyResearch(alienBase, guaranteed))
                    return null;
        }
        // processMissionSite runs before time5Seconds craft movement on a half-hour tick.
        if (highest >= CampaignTimeTrigger.ThirtyMinutes && destination.Kind == WorldTargetKind.MissionSite &&
            ReturnsFromSiteBeforeMovement(destination, highest)) return null;
        if (highest >= CampaignTimeTrigger.TenMinutes &&
            AdvanceCraftFuel(owner, craft, state).Destination is { Kind: WorldTargetKind.Base }) return null;
        if (!TryGetCraftDestinationPosition(destination, out var targetPosition)) return null;
        (WorldPosition Position, UfoStatus Status, int Speed)? projectedUfo = null;
        if (destination.Kind == WorldTargetKind.Ufo)
        {
            var original = TargetUfo(destination);
            // Dormant UFOs miss the radar pass before processMissionSite. Only a
            // later hunter retarget can reveal that hidden contact before craft movement.
            var ufo = original is null ? null : UfoForFlight(original, highest);
            if (original is { Status: UfoStatus.IgnoreMe, Detected: false } && ufo?.Detected != true) return null;
            // Detection may reveal an imported hidden destination at the half-hour boundary.
            if (ufo is null || !ufo.Detected && highest < CampaignTimeTrigger.ThirtyMinutes ||
                ufo.Status is UfoStatus.Destroyed or UfoStatus.IgnoreMe) return null;
            // Regional attachment has no exact target position. Its owning
            // preflight has proved every pursuing craft outside the full movement
            // envelope (or returning home), so no arrival can occur this tick.
            if (_pass.InexactNewLeaderFlights?.Contains(ufo.UniqueId) == true) return null;
            // Conversion's new site position and reset speed are forecast by the world capability.
            if (ArrivesAtSite(ufo, highest)) return null;
            projectedUfo = ForecastUfo(ufo, highest);
            targetPosition = projectedUfo.Value.Position;
        }
        // A terminal UFO arrival skips the entire craft pass, regardless of list order.
        // The answer is the same for every craft, so the craft-movement preflight computes it once.
        terminalUfoArrival ??= AnyUfoEndsFlightPass(highest);
        if (terminalUfoArrival.Value) return null;
        var from = new WorldPosition(state.Longitude, state.Latitude);
        var position = state.Takeoff > 0 ? from : WorldGeometry.Move(from, targetPosition, WorldGeometry.RadianSpeed(state.Speed));
        if (!WorldGeometry.ReachedDestination(position, targetPosition)) return null;
        if (projectedUfo is { Status: UfoStatus.Flying } projected)
        {
            CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                state.Weapons, campaign.Content.RuntimeRules, out var maximum);
            return projected.Speed <= maximum ? InterceptionReason : null;
        }
        if (!CanLand(owner, craft, state)) return null;
        if (destination.Kind == WorldTargetKind.AlienBase &&
            TargetAlienBase(destination) is { Discovered: false } undiscoveredBase)
        {
            if (ArrivalBaseDiscoveryReason(undiscoveredBase, highest, out var possible) is { } discoveryReason) return discoveryReason;
            if (!possible) return null;
        }
        return LandingReason;
    }

    // Facility sight runs after daily construction; patrol sight runs after ten-minute
    // fuel. Reserve deployment if either can reveal this saved target before arrival.
    // No RNG/script execution or speculative state mutation is needed.
    private string? ArrivalBaseDiscoveryReason(AlienBaseSnapshot alienBase, CampaignTimeTrigger highest, out bool possible)
    {
        possible = false;
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        var key = (MarkerName(alienBase.DeploymentId), alienBase.Id);
        if (_arrivalBaseDiscovery.TryGetValue(key, out possible)) return null;
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            if (++_arrivalBaseDiscoveryWork > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (highest >= CampaignTimeTrigger.OneDay)
            {
                var distance = WorldGeometry.Distance(new(owner.Longitude, owner.Latitude), alienBase.Position);
                foreach (var facility in owner.Facilities)
                {
                    if (++_arrivalBaseDiscoveryWork > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                    var rule = rules.Facilities[facility.Rule].Value;
                    if (WorldAlienBaseSight.FacilityChance(distance, rule.SightRange, rule.SightChance,
                            Math.Max(0, facility.BuildTime - 1)) is > 0)
                    {
                        possible = true;
                        _arrivalBaseDiscovery[key] = true;
                        return null;
                    }
                }
            }
            foreach (var craft in owner.Crafts)
            {
                if (++_arrivalBaseDiscoveryWork > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                var state = _pass.HuntingCraftForecast is { } forecast
                    ? forecast.GetValueOrDefault((rules.Crafts.GetExternalId(craft.Rule), craft.Id))
                    : craft.Logistics;
                if (state is not { Status: "STR_OUT", Destination: null }) continue;
                _arrivalBaseDiscoveryWork += (_pass.HuntingCraftForecast is null ? 4L : 1L) * state.Weapons.Count;
                if (_arrivalBaseDiscoveryWork > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (_pass.HuntingCraftForecast is null) state = AdvanceCraftFuel(owner, craft, state);
                if (state.Destination is not null) continue;
                if (!CraftLogistics.TryEffectiveSightRange(rules.Crafts[craft.Rule].Value, state.Weapons, rules, out var range))
                    return "Craft weapon sight bonuses exceed the supported range.";
                var distance = WorldGeometry.Distance(new(state.Longitude, state.Latitude), alienBase.Position);
                if (WorldAlienBaseSight.CraftChance(distance, range) is not > 0) continue;
                // Production precedes patrol discovery. Without a hunter, no other
                // caller may have requested the ordered craft-removal projection.
                if (_pass.HuntingCraftForecast is null && highest >= CampaignTimeTrigger.OneHour && owner.Productions.Count != 0)
                {
                    if (ForecastHuntingCrafts(highest, ref _arrivalBaseDiscoveryWork) is { } forecastReason) return forecastReason;
                    state = _pass.HuntingCraftForecast!.GetValueOrDefault((rules.Crafts.GetExternalId(craft.Rule), craft.Id));
                    if (state is not { Status: "STR_OUT", Destination: null }) continue;
                    _arrivalBaseDiscoveryWork += state.Weapons.Count;
                    if (_arrivalBaseDiscoveryWork > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                    if (!CraftLogistics.TryEffectiveSightRange(rules.Crafts[craft.Rule].Value, state.Weapons, rules, out range))
                        return "Craft weapon sight bonuses exceed the supported range.";
                    distance = WorldGeometry.Distance(new(state.Longitude, state.Latitude), alienBase.Position);
                    if (WorldAlienBaseSight.CraftChance(distance, range) is not > 0) continue;
                }
                possible = true;
                _arrivalBaseDiscovery[key] = true;
                return null;
            }
        }
        _arrivalBaseDiscovery[key] = false;
        return null;
    }

    private CraftLogisticsState RepairCraftPursuit(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, CampaignState.TimeEffects effects)
    {
        if (state.Destination is not { } target) return state;
        var exists = TryGetCraftDestinationPosition(target, out var targetPosition);
        var ufo = target.Kind == WorldTargetKind.Ufo ? TargetUfo(target) : null;
        var lost = !exists || target.Kind == WorldTargetKind.Ufo &&
            (ufo is null || !ufo.Detected || ufo.Status is UfoStatus.Destroyed or UfoStatus.IgnoreMe);
        if (!lost) return state;
        var returnHome = ufo?.Status is UfoStatus.Destroyed or UfoStatus.IgnoreMe ||
            ufo is { Detected: false, Status: UfoStatus.Landed, TrajectoryId: RuntimeUfoTrajectoryRule.RetaliationAssaultRun } ||
            !exists && target.Kind != WorldTargetKind.Waypoint;
        CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            state.Weapons, campaign.Content.RuntimeRules, out var maximum);
        effects.Notify(new CraftTargetLost(owner.Id, campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule),
            craft.Id, exists ? target with { Longitude = targetPosition.Longitude, Latitude = targetPosition.Latitude } : target, returnHome));
        if (returnHome) return SetDestination(state, maximum, BaseReference(owner));
        return state with
        {
            Destination = null,
            Speed = maximum / 2,
            SpeedRadian = WorldGeometry.RadianSpeed(maximum / 2),
            SpeedLongitude = 0,
            SpeedLatitude = 0
        };
    }

    private CraftLogisticsState ArriveAtWorldTarget(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, WorldTargetReference destination)
    {
        var returnHome = destination.Kind switch
        {
            WorldTargetKind.Craft => TargetCraft(destination)?.Logistics?.Status != "STR_OUT",
            WorldTargetKind.Ufo => TargetUfo(destination)?.Status == UfoStatus.Crashed && !CanLand(owner, craft, state),
            WorldTargetKind.MissionSite => !CanLand(owner, craft, state),
            WorldTargetKind.AlienBase => TargetAlienBase(destination) is
            { Discovered: true } && !CanLand(owner, craft, state),
            _ => false,
        };
        if (!returnHome) return state;
        CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            state.Weapons, campaign.Content.RuntimeRules, out var maximum);
        return SetDestination(state, maximum, BaseReference(owner));
    }
}
