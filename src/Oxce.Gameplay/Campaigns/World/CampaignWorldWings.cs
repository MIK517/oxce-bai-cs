using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>A craft by owner base, rule type and identity.</summary>
public sealed record CraftKey(int BaseId, string CraftTypeId, int CraftId);

/// <summary>
/// Launch a wing of one to four craft at a target or a new waypoint, as InterceptState and
/// ConfirmDestinationState::btnOkClick do. The first craft leads; the others follow the leader
/// or fly to the same destination. Every craft and the destination are validated before any
/// craft changes.
/// </summary>
public sealed record DispatchCraftWing(IReadOnlyList<CraftKey> Crafts, WorldTargetReference? Target,
    WorldPosition? Waypoint, bool FollowLeader) : ICampaignCommand;

internal sealed partial class CampaignWorld
{
    private const int MaximumWing = 4;

    private CampaignCommandResult DispatchWing(DispatchCraftWing command)
    {
        ArgumentNullException.ThrowIfNull(command.Crafts);
        if ((command.Target is null) == (command.Waypoint is null))
            throw new ArgumentException("A wing needs either a target or a waypoint.", nameof(command));
        if (command.Waypoint is { IsNormalized: false }) throw new ArgumentOutOfRangeException(nameof(command));
        if (command.Crafts.Count is 0 or > MaximumWing || command.Crafts.Distinct().Count() != command.Crafts.Count)
            return Blocked(WorldCommandReasons.WingSize);
        var members = command.Crafts.Select(key =>
        {
            ArgumentNullException.ThrowIfNull(key);
            var (owner, index) = FindCraft(key.BaseId, key.CraftTypeId, key.CraftId);
            return (Key: key, Owner: owner, Index: index);
        }).ToArray();

        // InterceptState only lets a launchable craft join; ConfirmDestinationState then checks the
        // armor of every craft, the destination, cargo of every craft and pilots of every craft.
        foreach (var member in members)
            if (LaunchReason(member.Owner, member.Owner.Crafts[member.Index]) is { } reason) return Blocked(reason);
        foreach (var member in members)
            if (ArmorReason(member.Owner, member.Owner.Crafts[member.Index]) is { } reason) return Blocked(reason);
        var leader = members[0];
        WorldTargetReference? destination = null;
        var leaderIsTarget = false;
        if (command.Target is { } target)
        {
            if (DestinationReason(target, out var resolved) is { } reason) return Blocked(reason);
            leaderIsTarget = IsCraft(target, leader.Key);
            for (var slot = 1; slot < members.Length; slot++)
                // Port restriction: the reference would let such a craft target itself.
                if (!command.FollowLeader && IsCraft(target, members[slot].Key))
                    return Blocked(WorldCommandReasons.TargetSelf);
            // Only craft that fly to the base itself must belong to it; followers fly to the leader.
            if (target.Kind == WorldTargetKind.Base && members.Where((_, slot) => slot == 0 || !command.FollowLeader)
                    .Any(member => member.Owner.Id != target.Id))
                return Blocked(WorldCommandReasons.ForeignBase);
            if (TargetDeployment(resolved) is { StartingCondition.Length: > 0 })
                return Blocked(WorldCommandReasons.StartingConditionBoundary);
            destination = resolved;
        }
        foreach (var member in members)
            if (ItemsReason(member.Owner.Crafts[member.Index]) is { } reason) return Blocked(reason);
        foreach (var member in members)
            if (PilotsReason(member.Owner, member.Owner.Crafts[member.Index]) is { } reason) return Blocked(reason);
        if (command.Waypoint is not null && !CanAllocateWaypointIds(1)) return Blocked(WaypointIdReason);

        if (command.Waypoint is { } point)
        {
            var id = campaign.NextId(WorldTargetReference.WaypointType);
            _waypoints.Add(new WaypointSnapshot(id, point.Longitude, point.Latitude));
            destination = WorldTargetReference.ForWaypoint(id, point);
        }
        var events = new List<ICampaignEvent>(members.Length);
        var leaderReference = new WorldTargetReference(WorldTargetKind.Craft, leader.Key.CraftTypeId,
            leader.Key.CraftId, 0, 0);
        for (var position = 0; position < members.Length; position++)
        {
            var member = members[position];
            var craft = member.Owner.Crafts[member.Index];
            var state = craft.Logistics!;
            var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
            CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out var speed);
            // ConfirmDestinationState cancels auto-patrol for craft that can use it.
            state = state with { IsAutoPatrolling = state.IsAutoPatrolling && !rule.AutoPatrol };
            WorldTargetReference? memberDestination = position == 0
                ? leaderIsTarget ? null : destination
                : command.FollowLeader ? Resolve(leaderReference) : destination;
            state = memberDestination is null ? Patrolling(state, speed) : SetDestination(state, speed, memberDestination);
            member.Owner.Crafts[member.Index] = craft with { Logistics = state };
            events.Add(new CraftDestinationChanged(member.Owner.Id, member.Key.CraftTypeId, member.Key.CraftId,
                memberDestination));
        }
        return new(events);

        static bool IsCraft(WorldTargetReference target, CraftKey key) =>
            target.Kind == WorldTargetKind.Craft && target.Id == key.CraftId &&
            string.Equals(target.TypeId, key.CraftTypeId, StringComparison.Ordinal);
    }

    /// <summary>Craft::setDestination(0): out on patrol at half the maximum speed.</summary>
    private static CraftLogisticsState Patrolling(CraftLogisticsState state, int speedMaximum)
    {
        var speed = speedMaximum / 2;
        return state with
        {
            Status = "STR_OUT",
            Destination = null,
            Takeoff = state.Status == "STR_OUT" ? state.Takeoff : 60,
            Speed = speed,
            SpeedRadian = WorldGeometry.RadianSpeed(speed),
            SpeedLongitude = 0,
            SpeedLatitude = 0,
        };
    }
}
