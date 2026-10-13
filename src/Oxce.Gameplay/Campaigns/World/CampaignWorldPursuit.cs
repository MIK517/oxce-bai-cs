using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::time5Seconds craft pursuit and target-loss handling.</summary>
internal sealed partial class CampaignWorld
{
    private const string InterceptionReason = WorldCommandReasons.InterceptionBoundary;

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
    private (WorldPosition Position, UfoStatus Status, int Speed) ForecastUfoStep(UfoSnapshot ufo,
        CampaignTimeTrigger highest, bool needPosition = true, double? knownDistance = null)
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
        var distance = knownDistance ?? UfoDestinationDistance(ufo);
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
        // A base assault ends the five-second pass like a terminal arrival (branch 5 S7).
        if (IsBaseAssaultArrival(ufo, trajectory)) return (position, UfoStatus.Destroyed, 0);
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var landing = trajectory.Altitude(ufo.TrajectoryPoint + 1) == 0;
        return (position, landing ? UfoStatus.Landed : UfoStatus.Flying,
            landing ? 0 : trajectory.Speed(ufo.TrajectoryPoint + 1, rule.StatsForRace(mission.Race).SpeedMaximum));
    }

    /// <summary>Whether the saved UFO at <paramref name="index"/> ends this tick's flight pass (its arrival
    /// destroys it), through the shared flight projection. The step's status is not shared: a suppressed
    /// site conversion changes it during the pass.</summary>
    private bool EndsUfoFlightPass(int index, CampaignTimeTrigger highest)
    {
        var ufo = SharedFlightAt(index, highest, out var distance);
        return ufo.Status == UfoStatus.Flying &&
            ForecastUfoStep(ufo, highest, needPosition: false, distance).Status == UfoStatus.Destroyed;
    }

    // Arrivals need no refusal since branch 5 S4-S6: a flying UFO opens an interception and a landing
    // target queues a confirmation. Only a craft told to escort itself is refused.
    private string? CraftArrivalReason(CampaignState.CraftState craft, CraftLogisticsState state) =>
        state.Destination is { Kind: WorldTargetKind.Craft } escort && escort.Id == craft.Id &&
        escort.TypeId == campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule) ? WorldCommandReasons.TargetSelf : null;

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
        var offersLastPosition = ufo is { Detected: false } && !(ufo.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun &&
            ufo.Status is UfoStatus.Landed or UfoStatus.Destroyed);
        effects.Notify(new CraftTargetLost(owner.Id, campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule),
            craft.Id, exists ? target with { Longitude = targetPosition.Longitude, Latitude = targetPosition.Latitude } : target, returnHome)
        {
            OffersLastPosition = offersLastPosition,
        });
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

    /// <summary>
    /// The craft pass reaching a UFO that is not flying, a mission site, an alien base or a craft. A craft
    /// that may land queues a landing confirmation; otherwise it returns home where the reference does.
    /// </summary>
    private CraftLogisticsState ArriveAtWorldTarget(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, WorldTargetReference destination, CampaignState.TimeEffects effects)
    {
        var returnHome = false;
        switch (destination.Kind)
        {
            case WorldTargetKind.Craft:
                returnHome = TargetCraft(destination)?.Logistics?.Status != "STR_OUT";
                break;
            case WorldTargetKind.Ufo when TargetUfo(destination) is { Status: UfoStatus.Landed or UfoStatus.Crashed or UfoStatus.Destroyed } ufo:
                if (CanLand(owner, craft, state))
                {
                    if (!CraftInDogfight(owner.Id, campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id))
                        QueueLanding(owner, craft, destination, ufo.Position, -1, effects);
                }
                else returnHome = ufo.Status != UfoStatus.Landed;
                break;
            case WorldTargetKind.MissionSite when _sites.Find(site => site.Id == destination.Id &&
                MarkerName(site.DeploymentId) == destination.TypeId) is { } site:
                if (CanLand(owner, craft, state)) QueueLanding(owner, craft, destination, site.Position, site.Texture, effects);
                else returnHome = true;
                break;
            case WorldTargetKind.AlienBase when TargetAlienBase(destination) is { Discovered: true } alienBase:
                if (CanLand(owner, craft, state)) QueueLanding(owner, craft, destination, alienBase.Position, -1, effects);
                else returnHome = true;
                break;
        }
        if (!returnHome) return state;
        CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            state.Weapons, campaign.Content.RuntimeRules, out var maximum);
        return SetDestination(state, maximum, BaseReference(owner));
    }
}
