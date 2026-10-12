using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Send a ready craft to a new globe waypoint, as in GeoscapeCraftState.</summary>
public sealed record DispatchCraftToWaypoint(int BaseId, string CraftTypeId, int CraftId,
    double Longitude, double Latitude)
    : ICampaignCommand;

/// <summary>Dispatch to a visible target using stable identity rather than marker coordinates.</summary>
public sealed record DispatchCraftToTarget(int BaseId, string CraftTypeId, int CraftId,
    WorldTargetReference Target) : ICampaignCommand;

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

/// <summary>The craft stopped pursuit; the last position can be confirmed as a new waypoint.</summary>
public sealed record CraftTargetLost(int BaseId, string CraftTypeId, int CraftId,
    WorldTargetReference Target, bool ReturningToBase) : ICampaignEvent
{
    /// <summary>
    /// GeoscapeState::time5Seconds opens GeoscapeCraftState at the UFO's last position: the pursued
    /// UFO left radar (even when it was also destroyed or ignored), except a retaliation assault run
    /// that landed or was destroyed.
    /// </summary>
    public bool OffersLastPosition { get; init; }
}

/// <summary>
/// Craft-only world operations. Reference: Craft::setDestination, think, returnToBase,
/// consumeFuel and GeoscapeState::time5Seconds/time10Minutes at 4df3a5e.
/// </summary>
internal sealed partial class CampaignWorld
{
    private readonly HashSet<int> _followedWaypointIds = [];
    private readonly Dictionary<(string Marker, int Id), bool> _arrivalBaseDiscovery = [];
    private long _arrivalBaseDiscoveryWork;
    private readonly HashSet<(string TypeId, int Id)> _possibleCraftReturns = [];
    private readonly HashSet<(string TypeId, int Id)> _returnedCraftTargets = [];

    private void RegisterCraftOperations(CampaignCapabilityRegistry registry)
    {
        registry.Command<DispatchCraftToWaypoint>(Dispatch);
        registry.Command<DispatchCraftToTarget>(DispatchTarget);
        registry.Command<RecallCraft>(Recall);
        registry.Command<PatrolCraft>(Patrol);
        registry.Command<DispatchCraftWing>(DispatchWing);
        registry.Preflight(CampaignPreflightOrder.CraftMovement, "craft movement", (_, highest) => CraftMovementReason(highest));
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
        if (DispatchReason(owner, craft) is { } reason) return Blocked(reason);
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        CraftLogistics.TryEffectiveSpeedMaximum(rule, state!.Weapons, campaign.Content.RuntimeRules, out var speed);
        if (!CanAllocateWaypointIds(1)) return Blocked(WaypointIdReason);
        var id = campaign.NextId(WorldTargetReference.WaypointType);
        var waypoint = new WaypointSnapshot(id, position.Longitude, position.Latitude);
        _waypoints.Add(waypoint);
        var destination = WorldTargetReference.ForWaypoint(id, position);
        // ConfirmDestinationState::btnOkClick cancels auto-patrol only for crafts that can use it.
        owner.Crafts[index] = craft with
        {
            Logistics = SetDestination(state with { IsAutoPatrolling = state.IsAutoPatrolling && !rule.AutoPatrol },
                speed, destination),
        };
        return new CampaignCommandResult([new CraftDestinationChanged(owner.Id, command.CraftTypeId,
            craft.Id, destination)]);
    }

    private string? DispatchReason(CampaignState.BaseState owner, CampaignState.CraftState craft) =>
        LaunchReason(owner, craft) ?? ArmorReason(owner, craft) ?? ItemsReason(craft) ?? PilotsReason(owner, craft);

    /// <summary>InterceptState::lstCraftsLeftClick's allowStart plus the port's speed guard.</summary>
    private string? LaunchReason(CampaignState.BaseState owner, CampaignState.CraftState craft)
    {
        if (craft.Logistics is not { } state) return WorldCommandReasons.Unresolved;
        if (!owner.IsPlaced) return WorldCommandReasons.BaseUnplaced;
        // InterceptState::lstCraftsLeftClick: a READY craft always starts. An airborne craft,
        // or any craft under craftLaunchAlways, starts only while it is not returning for
        // fuel or after completing its mission.
        if (state.Status != "STR_READY")
        {
            if (state.Status != "STR_OUT" && !campaign.Options.CraftLaunchAlways)
                return WorldCommandReasons.NotReady;
            if (IsReturningHome(state)) return ReturningReason;
        }
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        return CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out _)
            ? null : SpeedRangeReason;
    }

    // ConfirmDestinationState::btnOkClick checks armor, cargo, then pilots.
    private string? ArmorReason(CampaignState.BaseState owner, CampaignState.CraftState craft) =>
        campaign.HasAllowedArmorsOnboard(owner, craft) ? null : WorldCommandReasons.ArmorForbidden;

    private string? ItemsReason(CampaignState.CraftState craft) =>
        CraftLogistics.TooManyItemsOnboard(craft.Logistics!, campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            campaign.Content.RuntimeRules) ? WorldCommandReasons.TooManyItems : null;

    private string? PilotsReason(CampaignState.BaseState owner, CampaignState.CraftState craft) =>
        campaign.HasRequiredPilots(owner, craft) ? null : WorldCommandReasons.NoPilots;

    /// <summary>Craft::getLowFuel or getMissionComplete: the craft is already heading home.</summary>
    private static bool IsReturningHome(CraftLogisticsState state) => state.LowFuel || state.MissionComplete;

    private const string ReturningReason = WorldCommandReasons.Returning;

    private CampaignCommandResult Patrol(PatrolCraft command)
    {
        var (owner, index) = FindCraft(command.BaseId, command.CraftTypeId, command.CraftId);
        var craft = owner.Crafts[index];
        if (craft.Logistics is not { Status: "STR_OUT" } state)
            return Blocked(WorldCommandReasons.NotAirborne);
        if (IsReturningHome(state)) return Blocked(ReturningReason);
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
                // GeoscapeCraftState::btnPatrolClick only touches auto-patrol when the rule allows it.
                IsAutoPatrolling = rule.AutoPatrol || state.IsAutoPatrolling,
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
        if (state is not { Status: "STR_OUT" }) return Blocked(WorldCommandReasons.NotAirborne);
        // GeoscapeCraftState hides its commands while the craft is already heading home.
        if (IsReturningHome(state)) return Blocked(ReturningReason);
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var speed))
            return Blocked(SpeedRangeReason);
        var destination = BaseReference(owner);
        // GeoscapeCraftState::btnBaseClick cancels auto-patrol only for crafts that can use it.
        owner.Crafts[index] = craft with
        {
            Logistics = SetDestination(state with { IsAutoPatrolling = state.IsAutoPatrolling && !rule.AutoPatrol },
                speed, destination),
        };
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
    private string? CraftMovementReason(CampaignTimeTrigger highest)
    {
        var bases = campaign.BaseStates;
        var possibleRelaunches = 0;
        _possibleCraftReturns.Clear();
        _arrivalBaseDiscovery.Clear();
        _arrivalBaseDiscoveryWork = 0;
        bool? terminalUfoArrival = null;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var owner = bases[baseIndex];
            for (var craftIndex = 0; craftIndex < owner.Crafts.Count; craftIndex++)
            {
                var craft = owner.Crafts[craftIndex];
                if (craft.Logistics is not { } state) continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                if (CraftConditionReason(state, rule) is { } reason) return reason;
                if (highest >= CampaignTimeTrigger.ThirtyMinutes && state.IsAutoPatrolling && rule.AutoPatrol &&
                    (state.Status == "STR_REFUELLING" || highest >= CampaignTimeTrigger.OneHour &&
                        (state.Status == "STR_REARMING" || state.Status == "STR_READY" && rule.RefuelItem is not null)))
                    possibleRelaunches++;
                if (state.Status == "STR_OUT" || highest >= CampaignTimeTrigger.ThirtyMinutes && state.IsAutoPatrolling && rule.AutoPatrol)
                    _possibleCraftReturns.Add((campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id));
                if (state.Status != "STR_OUT") continue;
                if (!owner.IsPlaced) return "An airborne craft belongs to a base that is not placed.";
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                        campaign.Content.RuntimeRules, out _))
                    return SpeedRangeReason;
                if (state.Fuel < 0 || state.Takeoff < 0)
                    return "Airborne craft fuel or takeoff state is out of range.";
                if (!new WorldPosition(state.Longitude, state.Latitude).IsNormalized)
                    return "Airborne craft coordinates are invalid.";
                if (CraftArrivalReason(owner, craft, state, highest, ref terminalUfoArrival) is { } arrivalReason) return arrivalReason;
            }
            if (highest < CampaignTimeTrigger.OneHour) continue;
            // Transfer::advance/checkup precedes refuelling and auto-patrol relaunch.
            // Check arriving craft before any of those handlers can mutate the campaign.
            foreach (var transfer in owner.Transfers)
            {
                if (transfer.Delivered || transfer.Hours > 1 || transfer.Craft?.Logistics is not { } state) continue;
                var rules = campaign.Content.RuntimeRules;
                var rule = rules.Crafts[rules.Crafts.GetRequired(transfer.Craft.RuleId)].Value;
                if (CraftConditionReason(state, rule) is { } reason) return reason;
                if (state.IsAutoPatrolling && rule.AutoPatrol) possibleRelaunches++;
            }
        }
        // Native stopHuntingXcomCraft inspects every UFO, including dormant
        // contacts and a malformed craft destination without hunting flags.
        foreach (var ufo in _ufos)
            if (ufo.Destination is { Kind: WorldTargetKind.Craft } target &&
                _possibleCraftReturns.Contains((target.TypeId, target.Id)) && ufo.OriginalWaypoint is not { IsNormalized: true })
                return HuntStateReason;
        // Reserve an upper bound: hourly servicing, arrivals and produced fuel can
        // enter refuelling before relaunch. ReuseItem also changes READY craft with
        // missing fuel. No forecast mutates stocks or consumes RNG.
        return CanAllocateWaypointIds(possibleRelaunches) ? null : WaypointIdReason;
    }

    private const string WaypointIdReason = WorldCommandReasons.WaypointIds;

    private bool CanAllocateWaypointIds(int count)
    {
        if (count == 0) return true;
        var first = campaign.PeekNextId(WorldTargetReference.WaypointType);
        var afterLast = (long)first + count;
        if (afterLast > int.MaxValue) return false;
        foreach (var waypoint in _waypoints)
            if (waypoint.Id >= first && waypoint.Id < afterLast) return false;
        return true;
    }

    private string? CraftConditionReason(CraftLogisticsState state, RuntimeCraftRule rule)
    {
        // GeoscapeState::time5Seconds removes a destroyed craft in any status, with
        // activity, crew and statistics consequences owned by the dogfight slice.
        // Retain the port's exception for incomplete rules with non-positive damageMax.
        var damageMaximum = CraftLogistics.EffectiveDamageMaximum(state, rule, campaign.Content.RuntimeRules);
        if (damageMaximum > 0 && state.Damage >= damageMaximum)
            return "Destroyed craft removal requires world simulation.";
        if (!CraftLogistics.TryEffectiveGeoscapeShields(rule, state.Weapons, campaign.Content.RuntimeRules,
                out _, out _))
            return "Craft geoscape shield stats exceed the supported range.";
        if (state.IsAutoPatrolling)
        {
            if (!new WorldPosition(state.AutoPatrolLongitude, state.AutoPatrolLatitude).IsNormalized)
                return "Craft auto-patrol coordinates are invalid.";
            if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out _))
                return SpeedRangeReason;
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

    private const string SpeedRangeReason = WorldCommandReasons.SpeedRange;

    private void MoveCrafts(CampaignState.TimeEffects effects)
    {
        // UFO movement runs at order 100; a terminal arrival returns from the reference
        // handler before craft movement at order 200. Cleanup follows at order 300.
        _returnedCraftTargets.Clear();
        if (_ufoArrivalEndedTick) return;
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var owner = bases[baseIndex];
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { } state) continue;
                WorldTargetReference? arrival = null;
                if (state.Status == "STR_OUT")
                {
                    state = RepairCraftPursuit(owner, craft, state, effects);
                    // Craft::think: takeoff, movement and the return-to-base checkup.
                    if (!_ufoIsAttackingThisTick)
                    {
                        var takingOff = state.Takeoff > 0;
                        if (takingOff)
                            state = state with { Takeoff = state.Takeoff - 1 };
                        if (state.Destination is { } destination && TryGetCraftDestinationPosition(destination, out var to))
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
                                    _returnedCraftTargets.Add((campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id));
                                }
                                else
                                {
                                    arrival = destination;
                                }
                            }
                        }
                    }
                }
                // The reference recharges grounded craft too, after think/checkup and before the
                // arrival handling. A terminal UFO arrival skips this whole pass; a hunter-killer
                // attack skips the shields and arrivals of the craft after it.
                if (!_ufoIsAttackingThisTick)
                {
                    var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                    if (!CraftLogistics.TryEffectiveGeoscapeShields(rule, state.Weapons, campaign.Content.RuntimeRules,
                            out var capacity, out var recharge))
                        throw new InvalidOperationException("Craft shield stats changed after world preflight.");
                    state = CraftServicing.RechargeGeoscapeShield(state, capacity, recharge, campaign.Random);
                }
                if (arrival is { } reached && !_ufoIsAttackingThisTick)
                {
                    if (reached.Kind == WorldTargetKind.Waypoint)
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
                                reached.Id));
                    }
                    else if (reached.Kind == WorldTargetKind.Ufo && TargetUfo(reached) is { Status: UfoStatus.Flying } flying)
                    {
                        if (!ReferenceEquals(state, craft.Logistics)) owner.Crafts[index] = craft with { Logistics = state };
                        ArriveAtFlyingUfo(owner, index, UfoIndex(flying.UniqueId));
                        continue;
                    }
                    else
                    {
                        state = ArriveAtWorldTarget(owner, craft, state, reached);
                    }
                }
                if (!ReferenceEquals(state, craft.Logistics)) owner.Crafts[index] = craft with { Logistics = state };
            }
        }
        StopHuntingReturnedCrafts();
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

    private void StopHuntingReturnedCrafts()
    {
        if (_returnedCraftTargets.Count == 0) return;
        // Native stops each returned craft's hunters immediately after think.
        // UFO positions/speeds are unchanged by the reset; later craft flight
        // reads those fields only. Batch before UFO cleanup to scan each UFO once.
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Destination is not { Kind: WorldTargetKind.Craft } target ||
                !_returnedCraftTargets.Contains((target.TypeId, target.Id))) continue;
            var restored = WorldHunting.Restore(ufo);
            var speed = WorldGeometry.RadianSpeed(restored.Speed);
            var vector = WorldGeometry.SpeedVector(restored.Position, restored.Destination!.Position, speed);
            _ufos[index] = restored with
            {
                SpeedRadian = speed,
                SpeedLongitude = vector.Longitude,
                SpeedLatitude = vector.Latitude,
            };
        }
    }

    private bool TryGetCraftDestinationPosition(WorldTargetReference destination, out WorldPosition position)
    {
        if (destination.Kind == WorldTargetKind.Base)
        {
            foreach (var owner in campaign.BaseStates)
                if (owner.Id == destination.Id)
                {
                    position = new(owner.Longitude, owner.Latitude);
                    return true;
                }
        }
        else if (destination.Kind == WorldTargetKind.Waypoint)
        {
            foreach (var waypoint in _waypoints)
                if (waypoint.Id == destination.Id)
                {
                    position = waypoint.Position;
                    return true;
                }
        }
        else if (destination.Kind == WorldTargetKind.Ufo && TargetUfo(destination) is { } ufo)
        {
            position = ufo.Position;
            return true;
        }
        else if (destination.Kind == WorldTargetKind.Craft && TargetCraft(destination)?.Logistics is { } craft)
        {
            position = new(craft.Longitude, craft.Latitude);
            return true;
        }
        else if (destination.Kind is WorldTargetKind.MissionSite or WorldTargetKind.AlienBase &&
            Resolve(destination) is { } site)
        {
            position = site.Position;
            return true;
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
                state = AdvanceCraftFuel(owner, craft, state, out var refund);
                if (refund != 0)
                    campaign.CraftFuelStock.Refund(owner.Id, campaign.Content.RuntimeRules.Crafts[craft.Rule].Value.RefuelItem!.Value, refund);
                if (!craft.Logistics.LowFuel && state.LowFuel && !state.IsAutoPatrolling)
                    effects.Notify(new CraftLowFuel(owner.Id,
                        campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id));
                if (!ReferenceEquals(state, craft.Logistics)) owner.Crafts[index] = craft with { Logistics = state };
                DiscoverAlienBasesFromCraft(craft.Rule, state);
            }
        }
    }

    // Pure transition shared by time10Minutes and arrival preflight. Fuel return precedes
    // time5Seconds even when its notification pauses subsequent ticks.
    private CraftLogisticsState AdvanceCraftFuel(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, IReadOnlyDictionary<(string TypeId, int Id), CraftLogisticsState>? projectedCrafts = null) =>
        AdvanceCraftFuel(owner, craft, state, out _, projectedCrafts);

    private CraftLogisticsState AdvanceCraftFuel(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CraftLogisticsState state, out int fuelItemChange,
        IReadOnlyDictionary<(string TypeId, int Id), CraftLogisticsState>? projectedCrafts = null)
    {
        fuelItemChange = 0;
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var speedMaximum))
            throw new InvalidOperationException("Craft speed changed after world preflight.");
        var escortSpeed = 0;
        if (state.Destination is { Kind: WorldTargetKind.Craft } escort)
        {
            var leader = projectedCrafts is null ? TargetCraft(escort)?.Logistics :
                projectedCrafts.GetValueOrDefault((escort.TypeId, escort.Id));
            if (leader is not null && WorldGeometry.Distance(new(state.Longitude, state.Latitude),
                    new(leader.Longitude, leader.Latitude)) < WorldGeometry.Nautical(campaign.Content.RuntimeRules.Campaign.EscortRange))
                escortSpeed = leader.Speed;
        }
        // Native free patrol returns before setFuel; a zero burn with a
        // destination still caps an overfilled tank and refunds item fuel.
        if (state.Destination is not null || !rule.PatrolWithoutFuel)
        {
            var consumption = WorldFlight.FuelConsumption(rule.RefuelItem is not null, speedMaximum, state.Speed, escortSpeed);
            var result = CraftLogistics.SetFuel(state, rule, campaign.Content.RuntimeRules, checked(state.Fuel - consumption));
            state = result.State;
            fuelItemChange = result.FuelItemChange;
        }
        // Craft::getFuelLimit divides by zero here, so its C++ result is undefined.
        // Keep the craft stationary and recallable instead of inventing a low-fuel return.
        if (speedMaximum > 0 && !state.LowFuel && state.Fuel <= WorldFlight.FuelLimit(
                rule.RefuelItem is not null, speedMaximum,
                WorldGeometry.Distance(new(state.Longitude, state.Latitude), new(owner.Longitude, owner.Latitude))))
            state = SetDestination(state with { LowFuel = true }, speedMaximum, BaseReference(owner));
        return state;
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
            if (!CanAllocateWaypointIds(1))
                throw new InvalidOperationException("Waypoint IDs changed after world preflight.");
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
        new([CampaignActionBlocked.From(WorldCommandReasons.For(reason))]);
}
