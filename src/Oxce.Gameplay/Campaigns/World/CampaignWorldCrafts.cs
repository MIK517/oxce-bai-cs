using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Send a ready craft to a new globe waypoint, as in GeoscapeCraftState.</summary>
public sealed record DispatchCraftToWaypoint(int BaseId, string CraftTypeId, int CraftId,
    double Longitude, double Latitude)
    : ICampaignCommand;

/// <summary>Recall an airborne craft to its owner base.</summary>
public sealed record RecallCraft(int BaseId, string CraftTypeId, int CraftId) : ICampaignCommand;

/// <summary>Patrol at the craft's current location and remember it for automatic relaunch.</summary>
public sealed record PatrolCraft(int BaseId, string CraftTypeId, int CraftId) : ICampaignCommand;

public sealed record CraftDestinationChanged(int BaseId, string CraftTypeId, int CraftId,
    WorldTargetReference? Destination)
    : ICampaignEvent;

public sealed record CraftArrivedAtWaypoint(int BaseId, string CraftTypeId, int CraftId, int WaypointId)
    : ICampaignEvent;

public sealed record CraftLowFuel(int BaseId, string CraftTypeId, int CraftId) : ICampaignEvent;

/// <summary>
/// Craft-only world operations. Reference: Craft::setDestination, think, returnToBase,
/// consumeFuel and GeoscapeState::time5Seconds/time10Minutes at 4df3a5e.
/// </summary>
internal sealed partial class CampaignWorld
{
    private readonly HashSet<int> _followedWaypointIds = [];

    private void RegisterCraftOperations(CampaignCapabilityRegistry registry)
    {
        registry.Command<DispatchCraftToWaypoint>(Dispatch);
        registry.Command<RecallCraft>(Recall);
        registry.Command<PatrolCraft>(Patrol);
        registry.Preflight(CampaignPreflightOrder.CraftMovement, "craft movement", (_, _) => CraftMovementReason());
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldCrafts,
            "world craft movement", MoveCrafts);
        registry.Timed(CampaignTimeTrigger.TenMinutes, CampaignTimeOrder.TenMinutesWorldCraftFuel,
            "world craft fuel", ConsumeCraftFuel);
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesAutoPatrol,
            "world craft auto-patrol", RelaunchAutoPatrol);
    }

    private CampaignCommandResult Dispatch(DispatchCraftToWaypoint command)
    {
        var position = new WorldPosition(command.Longitude, command.Latitude);
        if (!position.IsNormalized) throw new ArgumentOutOfRangeException(nameof(command));
        var (owner, index) = FindCraft(command.BaseId, command.CraftTypeId, command.CraftId);
        var craft = owner.Crafts[index];
        var state = craft.Logistics;
        if (state is null) return Blocked("Craft dispatch requires resolved craft state.");
        if (!owner.IsPlaced) return Blocked("A craft cannot depart from an unplaced base.");
        if (state.Status != "STR_READY" && state.Status != "STR_OUT")
            return Blocked("The craft is not ready to depart.");
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var speed))
            return Blocked(SpeedRangeReason);
        // ConfirmDestinationState::btnOkClick checks armor, cargo, then pilots.
        if (!campaign.HasAllowedArmorsOnboard(owner, craft))
            return Blocked("The craft carries armor forbidden by its rules.");
        if (CraftLogistics.TooManyItemsOnboard(state, rule, campaign.Content.RuntimeRules))
            return Blocked("The craft carries too many items.");
        if (!campaign.HasRequiredPilots(owner, craft)) return Blocked("The craft does not have enough pilots.");
        var id = campaign.NextId(WorldTargetReference.WaypointType);
        var waypoint = new WaypointSnapshot(id, position.Longitude, position.Latitude);
        _waypoints.Add(waypoint);
        var destination = WorldTargetReference.ForWaypoint(id, position);
        owner.Crafts[index] = craft with
        { Logistics = SetDestination(state with { IsAutoPatrolling = false }, speed, destination) };
        return new CampaignCommandResult([new CraftDestinationChanged(owner.Id, command.CraftTypeId,
            craft.Id, destination)]);
    }

    private CampaignCommandResult Patrol(PatrolCraft command)
    {
        var (owner, index) = FindCraft(command.BaseId, command.CraftTypeId, command.CraftId);
        var craft = owner.Crafts[index];
        if (craft.Logistics is not { Status: "STR_OUT" } state)
            return Blocked("The craft is not airborne.");
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var speedMaximum))
            return Blocked(SpeedRangeReason);
        // Craft::setDestination(0) halves the maximum speed, which can legally be zero.
        var speed = speedMaximum / 2;
        owner.Crafts[index] = craft with
        {
            Logistics = state with
            {
                Destination = null,
                Speed = speed,
                SpeedRadian = WorldGeometry.RadianSpeed(speed),
                SpeedLongitude = 0,
                SpeedLatitude = 0,
                IsAutoPatrolling = rule.AutoPatrol,
                AutoPatrolLongitude = rule.AutoPatrol ? state.Longitude : state.AutoPatrolLongitude,
                AutoPatrolLatitude = rule.AutoPatrol ? state.Latitude : state.AutoPatrolLatitude,
            },
        };
        return new CampaignCommandResult([new CraftDestinationChanged(owner.Id, command.CraftTypeId,
            craft.Id, null)]);
    }

    private CampaignCommandResult Recall(RecallCraft command)
    {
        var (owner, index) = FindCraft(command.BaseId, command.CraftTypeId, command.CraftId);
        var craft = owner.Crafts[index];
        var state = craft.Logistics;
        if (state is not { Status: "STR_OUT" }) return Blocked("The craft is not airborne.");
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        // Craft::returnToBase has no speed condition, so recall always stays available.
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var speed))
            return Blocked(SpeedRangeReason);
        var destination = BaseReference(owner);
        owner.Crafts[index] = craft with
        { Logistics = SetDestination(state with { IsAutoPatrolling = false }, speed, destination) };
        return new CampaignCommandResult([new CraftDestinationChanged(owner.Id, command.CraftTypeId,
            craft.Id, destination)]);
    }

    private (CampaignState.BaseState Owner, int Index) FindCraft(int baseId, string craftTypeId, int craftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(craftTypeId);
        if (!campaign.BaseStates.Any(baseState => baseState.Id == baseId))
            throw new ArgumentOutOfRangeException(nameof(baseId));
        if (!TryFindCraft(baseId, craftTypeId, craftId, out var owner, out var index))
            throw new ArgumentOutOfRangeException(nameof(craftId));
        return (owner, index);
    }

    private bool TryFindCraft(int baseId, string craftTypeId, int craftId,
        out CampaignState.BaseState owner, out int index)
    {
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            if (bases[baseIndex].Id != baseId) continue;
            owner = bases[baseIndex];
            for (index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Id == craftId &&
                    campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule) == craftTypeId)
                    return true;
            }
        }
        owner = null!;
        index = -1;
        return false;
    }

    /// <summary>
    /// A stationary craft is not a blocked campaign: the reference simply never reaches a
    /// destination it cannot fly to. Time stops only for state this slice cannot simulate
    /// (a pursuit or landing target) or cannot trust (out-of-range craft state).
    /// </summary>
    private string? CraftMovementReason()
    {
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var owner = bases[baseIndex];
            for (var craftIndex = 0; craftIndex < owner.Crafts.Count; craftIndex++)
            {
                var craft = owner.Crafts[craftIndex];
                if (craft.Logistics is not { } state) continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                if (state.IsAutoPatrolling)
                {
                    if (!new WorldPosition(state.AutoPatrolLongitude, state.AutoPatrolLatitude).IsNormalized)
                        return "Craft auto-patrol coordinates are invalid.";
                    if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                            campaign.Content.RuntimeRules, out _))
                        return SpeedRangeReason;
                }
                if (state.Status != "STR_OUT") continue;
                if (!owner.IsPlaced) return "An airborne craft belongs to a base that is not placed.";
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                        campaign.Content.RuntimeRules, out _))
                    return SpeedRangeReason;
                if (state.Fuel < 0 || state.Takeoff < 0)
                    return "Airborne craft fuel or takeoff state is out of range.";
                if (!new WorldPosition(state.Longitude, state.Latitude).IsNormalized)
                    return "Airborne craft coordinates are invalid.";
                if (state.Destination is { } destination && !TryGetCraftDestinationPosition(destination, out _))
                    return "Craft pursuit and landing require world simulation.";
            }
        }
        return null;
    }

    /// <summary>Craft::setDestination: a craft leaving its base takes off for 60 five-second ticks.</summary>
    private static CraftLogisticsState SetDestination(CraftLogisticsState state, int speedMaximum,
        WorldTargetReference destination) => state with
        {
            Status = "STR_OUT",
            Destination = destination,
            Takeoff = state.Status == "STR_OUT" ? state.Takeoff : 60,
            Speed = speedMaximum,
            SpeedRadian = WorldGeometry.RadianSpeed(speedMaximum),
            SpeedLongitude = 0,
            SpeedLatitude = 0,
        };

    private const string SpeedRangeReason = "Craft speed exceeds the supported range.";

    private void MoveCrafts(CampaignState.TimeEffects effects)
    {
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var owner = bases[baseIndex];
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { Status: "STR_OUT" } state) continue;
                var takingOff = state.Takeoff > 0;
                if (takingOff)
                    state = state with { Takeoff = state.Takeoff - 1 };
                if (state.Destination is { } destination)
                {
                    if (TryGetCraftDestinationPosition(destination, out var to))
                    {
                        var from = new WorldPosition(state.Longitude, state.Latitude);
                        if (!takingOff)
                        {
                            var speedRadian = WorldGeometry.RadianSpeed(state.Speed);
                            var vector = WorldGeometry.SpeedVector(from, to, speedRadian);
                            var moved = WorldGeometry.Move(from, to, speedRadian, vector);
                            state = state with
                            {
                                Longitude = moved.Longitude,
                                Latitude = moved.Latitude,
                                SpeedRadian = speedRadian,
                                SpeedLongitude = vector.Longitude,
                                SpeedLatitude = vector.Latitude,
                            };
                        }
                        if (WorldGeometry.ReachedDestination(new WorldPosition(state.Longitude, state.Latitude), to))
                        {
                            if (destination.Kind == WorldTargetKind.Base)
                            {
                                state = CheckupOnReturn(state, craft, campaign.Content.RuntimeRules);
                            }
                            else if (destination.Kind == WorldTargetKind.Waypoint)
                            {
                                if (!CraftLogistics.TryEffectiveSpeedMaximum(
                                    campaign.Content.RuntimeRules.Crafts[craft.Rule].Value, state.Weapons,
                                    campaign.Content.RuntimeRules, out var maximum))
                                    throw new InvalidOperationException("Craft speed changed after world preflight.");
                                var speed = maximum / 2;
                                state = state with
                                {
                                    Destination = null,
                                    Speed = speed,
                                    SpeedRadian = WorldGeometry.RadianSpeed(speed),
                                    SpeedLongitude = 0,
                                    SpeedLatitude = 0,
                                };
                                if (!state.IsAutoPatrolling)
                                    effects.Notify(new CraftArrivedAtWaypoint(owner.Id,
                                        campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id,
                                        destination.Id));
                            }
                        }
                    }
                }
                // Nothing changed for a craft that is parked, patrolling or waiting out its takeoff.
                if (!ReferenceEquals(state, craft.Logistics)) owner.Crafts[index] = craft with { Logistics = state };
            }
        }
        // GeoscapeState::time5Seconds deletes waypoints without followers. Gather the
        // followers once so cleanup does not rescan every craft for every waypoint.
        if (_waypoints.Count == 0) return;
        _followedWaypointIds.Clear();
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var crafts = bases[baseIndex].Crafts;
            for (var craftIndex = 0; craftIndex < crafts.Count; craftIndex++)
                if (crafts[craftIndex].Logistics?.Destination is { Kind: WorldTargetKind.Waypoint } destination)
                    _followedWaypointIds.Add(destination.Id);
        }
        for (var index = _waypoints.Count - 1; index >= 0; index--)
            if (!_followedWaypointIds.Contains(_waypoints[index].Id)) _waypoints.RemoveAt(index);
    }

    private bool TryGetCraftDestinationPosition(WorldTargetReference destination, out WorldPosition position)
    {
        // Other target kinds need pursuit, interception or landing handlers before time can advance.
        if (destination.Kind == WorldTargetKind.Base)
        {
            foreach (var owner in campaign.BaseStates)
                if (owner.Id == destination.Id)
                {
                    position = new WorldPosition(owner.Longitude, owner.Latitude);
                    return true;
                }
        }
        else if (destination.Kind == WorldTargetKind.Waypoint)
        {
            foreach (var waypoint in _waypoints)
                if (waypoint.Id == destination.Id)
                {
                    position = new WorldPosition(waypoint.Longitude, waypoint.Latitude);
                    return true;
                }
        }
        position = default;
        return false;
    }

    private static CraftLogisticsState CheckupOnReturn(CraftLogisticsState state, CampaignState.CraftState craft,
        RuntimeRuleCatalog rules)
    {
        var arrived = CraftLogistics.Arrive(state, rules.Crafts[craft.Rule].Value, rules,
            state.Longitude, state.Latitude);
        return arrived with
        {
            Destination = null,
            LowFuel = false,
            MissionComplete = false,
            Takeoff = 0,
            InterceptionOrder = 0,
            Speed = 0,
            SpeedRadian = 0,
            SpeedLongitude = 0,
            SpeedLatitude = 0,
        };
    }

    private void ConsumeCraftFuel(CampaignState.TimeEffects effects)
    {
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var owner = bases[baseIndex];
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { Status: "STR_OUT" } state) continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                        campaign.Content.RuntimeRules, out var speedMaximum))
                    throw new InvalidOperationException("Craft speed changed after world preflight.");
                var consumption = state.Destination is null && rule.PatrolWithoutFuel ? 0 :
                    WorldFlight.FuelConsumption(rule.RefuelItem is not null, speedMaximum, state.Speed, 0);
                if (consumption != 0) state = state with { Fuel = Math.Max(0, state.Fuel - consumption) };
                // Craft::getFuelLimit divides by zero here, so its C++ result is undefined.
                // Keep the craft stationary and recallable instead of inventing a low-fuel return.
                if (speedMaximum > 0 && !state.LowFuel && state.Fuel <= WorldFlight.FuelLimit(
                        rule.RefuelItem is not null, speedMaximum,
                        WorldGeometry.Distance(new WorldPosition(state.Longitude, state.Latitude),
                            new WorldPosition(owner.Longitude, owner.Latitude))))
                {
                    state = SetDestination(state with { LowFuel = true }, speedMaximum, BaseReference(owner));
                    if (!state.IsAutoPatrolling)
                        effects.Notify(new CraftLowFuel(owner.Id,
                            campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id));
                }
                if (!ReferenceEquals(state, craft.Logistics)) owner.Crafts[index] = craft with { Logistics = state };
            }
        }
    }

    private static WorldTargetReference BaseReference(CampaignState.BaseState owner) =>
        new(WorldTargetKind.Base, WorldTargetReference.BaseType, owner.Id, owner.Longitude, owner.Latitude);

    private void RelaunchAutoPatrol(CampaignState.TimeEffects effects)
    {
        foreach (var (baseId, craftTypeId, craftId) in effects.AutoPatrolCandidates)
        {
            if (!TryFindCraft(baseId, craftTypeId, craftId, out var owner, out var index)) continue;
            var craft = owner.Crafts[index];
            if (craft.Logistics is not { Status: "STR_READY", IsAutoPatrolling: true } state) continue;
            var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
            if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                    campaign.Content.RuntimeRules, out var speed))
                throw new InvalidOperationException("Craft speed changed after world preflight.");
            var waypointId = campaign.NextId(WorldTargetReference.WaypointType);
            var position = new WorldPosition(state.AutoPatrolLongitude, state.AutoPatrolLatitude);
            _waypoints.Add(new WaypointSnapshot(waypointId, position.Longitude, position.Latitude));
            owner.Crafts[index] = craft with
            {
                Logistics = SetDestination(state, speed, WorldTargetReference.ForWaypoint(waypointId, position)),
            };
        }
        effects.AutoPatrolCandidates.Clear();
    }

    private static CampaignCommandResult Blocked(string reason) =>
        new([new CampaignActionBlocked(reason)]);
}
