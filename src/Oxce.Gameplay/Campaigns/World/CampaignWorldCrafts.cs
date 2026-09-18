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

public sealed record CraftReturnedToBase(int BaseId, string CraftTypeId, int CraftId) : ICampaignEvent;

public sealed record CraftLowFuel(int BaseId, string CraftTypeId, int CraftId) : ICampaignEvent;

/// <summary>
/// Craft-only world operations. Reference: Craft::setDestination, think, returnToBase,
/// consumeFuel and GeoscapeState::time5Seconds/time10Minutes at 4df3a5e.
/// </summary>
internal sealed partial class CampaignWorld
{
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
                campaign.Content.RuntimeRules, out var speed) || speed <= 0 ||
            state.Fuel <= 0)
            return Blocked("The craft needs positive speed and fuel to depart.");
        var id = campaign.NextId(WorldTargetReference.WaypointType);
        var waypoint = new WaypointSnapshot(id, position.Longitude, position.Latitude);
        _waypoints.Add(waypoint);
        var destination = WorldTargetReference.ForWaypoint(id, position);
        owner.Crafts[index] = craft with
        { Logistics = SetDestination(state with { IsAutoPatrolling = false }, rule, destination) };
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
                campaign.Content.RuntimeRules, out var speedMaximum) || speedMaximum <= 0)
            return Blocked("The craft cannot patrol without a positive speed.");
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
                AutoPatrolLongitude = state.Longitude,
                AutoPatrolLatitude = state.Latitude,
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
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var speed) || speed <= 0)
            return Blocked("The craft cannot return without a positive speed.");
        var destination = BaseReference(owner);
        owner.Crafts[index] = craft with
        { Logistics = SetDestination(state with { IsAutoPatrolling = false }, rule, destination) };
        return new CampaignCommandResult([new CraftDestinationChanged(owner.Id, command.CraftTypeId,
            craft.Id, destination)]);
    }

    private (CampaignState.BaseState Owner, int Index) FindCraft(int baseId, string craftTypeId, int craftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(craftTypeId);
        var owner = campaign.BaseStates.FirstOrDefault(baseState => baseState.Id == baseId)
            ?? throw new ArgumentOutOfRangeException(nameof(baseId));
        var index = owner.Crafts.FindIndex(craft => craft.Id == craftId &&
            campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule) == craftTypeId);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(craftId));
        return (owner, index);
    }

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
                if (state.IsAutoPatrolling && !new WorldPosition(state.AutoPatrolLongitude,
                        state.AutoPatrolLatitude).IsNormalized)
                    return "Craft auto-patrol coordinates are invalid.";
                if (state.IsAutoPatrolling && !CraftLogistics.TryEffectiveSpeedMaximum(
                        campaign.Content.RuntimeRules.Crafts[craft.Rule].Value, state.Weapons,
                        campaign.Content.RuntimeRules, out _))
                    return "Craft auto-patrol speed exceeds the supported range.";
                if (state.Status != "STR_OUT") continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                if (!owner.IsPlaced ||
                    !CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                        campaign.Content.RuntimeRules, out var speed) || speed <= 0 ||
                    state.Speed <= 0 || state.Fuel < 0 ||
                    state.Takeoff < 0 || !new WorldPosition(state.Longitude, state.Latitude).IsNormalized ||
                    state.Destination is { } destination && Resolve(destination) is null)
                    return "Craft movement requires world simulation.";
            }
        }
        return null;
    }

    private CraftLogisticsState SetDestination(CraftLogisticsState state, RuntimeCraftRule rule,
        WorldTargetReference destination)
    {
        var speed = CraftLogistics.EffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules);
        return state with
        {
            Status = "STR_OUT",
            Destination = destination,
            Takeoff = state.Status == "STR_OUT" ? state.Takeoff : 60,
            Speed = speed,
            SpeedRadian = WorldGeometry.RadianSpeed(speed),
            SpeedLongitude = 0,
            SpeedLatitude = 0,
        };
    }

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
                    var resolved = Resolve(destination);
                    if (resolved is not null)
                    {
                        var from = new WorldPosition(state.Longitude, state.Latitude);
                        var to = resolved.Position;
                        if (!takingOff)
                        {
                            var speedRadian = WorldGeometry.RadianSpeed(state.Speed);
                            var vector = WorldGeometry.SpeedVector(from, to, speedRadian);
                            var moved = WorldGeometry.Move(from, to, speedRadian);
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
                                effects.Notify(new CraftReturnedToBase(owner.Id,
                                    campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id));
                            }
                            else if (destination.Kind == WorldTargetKind.Waypoint)
                            {
                                state = state with
                                {
                                    Destination = null,
                                    Speed = CraftLogistics.EffectiveSpeedMaximum(
                                        campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                                        state.Weapons, campaign.Content.RuntimeRules) / 2,
                                    SpeedRadian = WorldGeometry.RadianSpeed(
                                        CraftLogistics.EffectiveSpeedMaximum(
                                            campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                                            state.Weapons, campaign.Content.RuntimeRules) / 2),
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
                owner.Crafts[index] = craft with { Logistics = state };
            }
        }
        // GeoscapeState::time5Seconds deletes waypoints without followers.
        for (var index = _waypoints.Count - 1; index >= 0; index--)
            if (!HasWaypointFollower(_waypoints[index].Id)) _waypoints.RemoveAt(index);
    }

    private bool HasWaypointFollower(int waypointId)
    {
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var crafts = bases[baseIndex].Crafts;
            for (var craftIndex = 0; craftIndex < crafts.Count; craftIndex++)
                if (crafts[craftIndex].Logistics?.Destination is { Kind: WorldTargetKind.Waypoint } destination &&
                    destination.Id == waypointId) return true;
        }
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
                var speedMaximum = CraftLogistics.EffectiveSpeedMaximum(rule, state.Weapons,
                    campaign.Content.RuntimeRules);
                var consumption = state.Destination is null && rule.PatrolWithoutFuel ? 0 :
                    WorldFlight.FuelConsumption(rule.RefuelItem is not null, speedMaximum, state.Speed, 0);
                state = state with { Fuel = Math.Max(0, state.Fuel - consumption) };
                var distance = WorldGeometry.Distance(new WorldPosition(state.Longitude, state.Latitude),
                    new WorldPosition(owner.Longitude, owner.Latitude));
                if (!state.LowFuel && state.Fuel <= WorldFlight.FuelLimit(
                    rule.RefuelItem is not null, speedMaximum, distance))
                {
                    state = SetDestination(state with { LowFuel = true }, rule, BaseReference(owner));
                    if (!state.IsAutoPatrolling)
                        effects.Notify(new CraftLowFuel(owner.Id,
                            campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id));
                }
                owner.Crafts[index] = craft with { Logistics = state };
            }
        }
    }

    private static WorldTargetReference BaseReference(CampaignState.BaseState owner) =>
        new(WorldTargetKind.Base, WorldTargetReference.BaseType, owner.Id, owner.Longitude, owner.Latitude);

    private void RelaunchAutoPatrol(CampaignState.TimeEffects _)
    {
        var bases = campaign.BaseStates;
        for (var baseIndex = 0; baseIndex < bases.Count; baseIndex++)
        {
            var owner = bases[baseIndex];
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { Status: "STR_READY", IsAutoPatrolling: true } state) continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                if (!rule.AutoPatrol) continue;
                var waypointId = campaign.NextId(WorldTargetReference.WaypointType);
                var position = new WorldPosition(state.AutoPatrolLongitude, state.AutoPatrolLatitude);
                _waypoints.Add(new WaypointSnapshot(waypointId, position.Longitude, position.Latitude));
                owner.Crafts[index] = craft with
                {
                    Logistics = SetDestination(state, rule, WorldTargetReference.ForWaypoint(waypointId, position)),
                };
            }
        }
    }

    private static CampaignCommandResult Blocked(string reason) =>
        new([new CampaignActionBlocked(reason)]);
}
