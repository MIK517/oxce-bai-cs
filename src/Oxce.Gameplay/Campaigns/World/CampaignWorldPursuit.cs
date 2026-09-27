using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::time5Seconds craft pursuit and target-loss handling.</summary>
internal sealed partial class CampaignWorld
{
    private const string InterceptionReason = "Craft interception requires strategic combat (Phase 6 branch 5).";
    private const string LandingReason = "Craft landing requires deployment (Phase 6 branch 5).";

    private CampaignCommandResult DispatchTarget(DispatchCraftToTarget command)
    {
        ArgumentNullException.ThrowIfNull(command.Target);
        var (owner, index) = FindCraft(command.BaseId, command.CraftTypeId, command.CraftId);
        var craft = owner.Crafts[index];
        if (DispatchReason(owner, craft) is { } reason) return Blocked(reason);
        var target = command.Target;
        if (!Enum.IsDefined(target.Kind)) return Blocked("The destination kind is invalid.");
        if (target.Kind == WorldTargetKind.Ufo && target.UniqueId <= 0)
            return Blocked("UFO dispatch requires its stable unique identity.");
        if (Resolve(target) is not { } resolved || !IsVisibleTarget(resolved))
            return Blocked("The destination no longer exists or is not visible.");
        resolved = resolved.Kind switch
        {
            WorldTargetKind.Ufo => resolved with { TypeId = WorldTargetReference.UfoType, Id = TargetUfo(resolved)!.Id },
            WorldTargetKind.Base => resolved with { TypeId = WorldTargetReference.BaseType },
            WorldTargetKind.Waypoint => resolved with { TypeId = WorldTargetReference.WaypointType },
            _ => resolved,
        };
        if (target.Kind == WorldTargetKind.Craft && target.Id == craft.Id && target.TypeId == command.CraftTypeId)
            return Blocked("A craft cannot escort itself.");
        if (target.Kind == WorldTargetKind.Base && target.Id != owner.Id)
            return Blocked("A craft can return only to its own base; use a transfer to change bases.");
        if (TargetDeployment(resolved) is { StartingCondition.Length: > 0 })
            return Blocked("Target starting conditions require deployment validation (Phase 6 branch 5).");
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
    private (WorldPosition Position, UfoStatus Status, int Speed) ForecastUfo(UfoSnapshot ufo)
    {
        if (ufo.Status == UfoStatus.Landed)
            return ufo.SecondsRemaining == 5 ? (ufo.Position, UfoStatus.Flying,
                campaign.Content.RuntimeRules.UfoTrajectories[
                    campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value.Speed(
                        ufo.TrajectoryPoint, campaign.Content.RuntimeRules.Ufos[
                            campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value.StatsForRace(
                                FindMission(ufo.MissionId)!.Race).SpeedMaximum)) : (ufo.Position, ufo.Status, ufo.Speed);
        if (ufo.Status != UfoStatus.Flying) return (ufo.Position, ufo.Status, ufo.Speed);
        var position = WorldGeometry.Move(ufo.Position, ufo.Destination!.Position, WorldGeometry.RadianSpeed(ufo.Speed));
        if (WorldGeometry.Distance(ufo.Position, ufo.Destination.Position) > WorldGeometry.RadianSpeed(ufo.Speed))
            return (position, ufo.Status, ufo.Speed);
        var mission = FindMission(ufo.MissionId)!;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        if (mission.Interrupted || ufo.TrajectoryPoint + 1 >= trajectory.Waypoints.Count)
            return (position, UfoStatus.Destroyed, 0);
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var landing = trajectory.Altitude(ufo.TrajectoryPoint + 1) == 0;
        return (position, landing ? UfoStatus.Landed : UfoStatus.Flying,
            landing ? 0 : trajectory.Speed(ufo.TrajectoryPoint + 1, rule.StatsForRace(mission.Race).SpeedMaximum));
    }

    private string? CraftArrivalReason(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, CampaignTimeTrigger highest)
    {
        if (state.Destination is { Kind: WorldTargetKind.Craft } escort && escort.Id == craft.Id &&
            escort.TypeId == campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule))
            return "A craft cannot escort itself.";
        if (state.Destination is not { } destination ||
            destination.Kind is WorldTargetKind.Base or WorldTargetKind.Waypoint or WorldTargetKind.Craft) return null;
        if (highest >= CampaignTimeTrigger.TenMinutes &&
            AdvanceCraftFuel(owner, craft, state).Destination is { Kind: WorldTargetKind.Base }) return null;
        if (!TryGetCraftDestinationPosition(destination, out var targetPosition)) return null;
        (WorldPosition Position, UfoStatus Status, int Speed)? projectedUfo = null;
        if (destination.Kind == WorldTargetKind.Ufo)
        {
            var ufo = TargetUfo(destination);
            // Detection may reveal an imported hidden destination at the half-hour boundary.
            if (ufo is null || !ufo.Detected && highest < CampaignTimeTrigger.ThirtyMinutes ||
                ufo.Status is UfoStatus.Destroyed or UfoStatus.IgnoreMe) return null;
            projectedUfo = ForecastUfo(ufo);
            targetPosition = projectedUfo.Value.Position;
        }
        // A terminal UFO arrival skips the entire craft pass, regardless of list order.
        foreach (var candidate in _ufos)
            if (candidate.Status == UfoStatus.Flying && ForecastUfo(candidate).Status == UfoStatus.Destroyed) return null;
        var from = new WorldPosition(state.Longitude, state.Latitude);
        var position = state.Takeoff > 0 ? from : WorldGeometry.Move(from, targetPosition, WorldGeometry.RadianSpeed(state.Speed));
        if (!WorldGeometry.ReachedDestination(position, targetPosition)) return null;
        if (projectedUfo is { Status: UfoStatus.Flying } projected)
        {
            CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                state.Weapons, campaign.Content.RuntimeRules, out var maximum);
            return projected.Speed <= maximum ? InterceptionReason : null;
        }
        return CanLand(owner, craft, state) ? LandingReason : null;
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
            WorldTargetKind.MissionSite or WorldTargetKind.AlienBase => !CanLand(owner, craft, state),
            _ => false,
        };
        if (!returnHome) return state;
        CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            state.Weapons, campaign.Content.RuntimeRules, out var maximum);
        return SetDestination(state, maximum, BaseReference(owner));
    }
}
