using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The movement queries a battlescape client asks (Phase 7 branch 4 S10): the previewed path with its time unit and energy markers
/// (<c>Pathfinding::calculate</c> and <c>Pathfinding::refreshPath</c>) and the reachable tiles (<c>Pathfinding::findReachable</c>). Both read the
/// battle and change nothing, and both answer from what the viewer knows: only for a unit of the viewer's side on turn, only towards a tile the
/// viewer has discovered, and only over discovered tiles.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// Whether the viewer's faction has discovered the tile's floor (or explored it, for the sides whose discovery the port keeps as
    /// exploration): what <see cref="LevelView"/> shows of a tile's floor.
    /// </summary>
    private bool ViewerKnowsTile(BattleViewer viewer, int tile) =>
        viewer.Faction != BattleFaction.Player && Map.LastExplored(tile, viewer.Faction) > 0 || Map.IsDiscovered(tile, BattleTilePart.Floor, viewer.Faction);

    /// <summary>The viewer's own live unit on the map, on its side's turn, or null.</summary>
    private BattleUnit? ViewersUnitOnTurn(BattleViewer viewer, int unitId)
    {
        if (!Enum.IsDefined(viewer.Faction)) throw new ArgumentOutOfRangeException(nameof(viewer));
        if (viewer.Faction != Side || SelectionUnit(unitId, out var found) is not null) return null;
        return found!.Position is null ? null : found;
    }

    /// <summary>
    /// The path <see cref="Walk"/> would calculate to <paramref name="target"/>, marked as <c>refreshPath</c> does: the time units and energy the
    /// unit would have left after each step (the kneel's standing up and, for an armor that turns before its first step, that turn paid first), and
    /// whether the step stays clear of the reserve (checked with an auto shot reserve when none is set, as the reference does for the preview).
    /// </summary>
    internal BattlePathView? PathView(RuntimeRuleCatalog rules, BattleViewer viewer, int unitId, BattlePosition target, BattleMoveType move)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (ViewersUnitOnTurn(viewer, unitId) is not { } unit || !Contains(target) || !ViewerKnowsTile(viewer, Map.TileIndex(target))) return null;
        BattlePathView None(string code) => new(unit.Id, target, move, [], 0, 0, code);
        var armor = ArmorOf(rules, unit);
        if (MoveRefusal(armor, unit, move) is { } notAllowed) return None(notAllowed.Code);

        // refreshPath prices the steps on the pathfinder that calculated them: a one-step strafe (StrafeMove) costs one more.
        var pathfinder = new BattlePathfinder(this, rules);
        var path = pathfinder.Calculate(unit, target, (BattleActionMove)move);
        if (path.Count == 0) return None(BattleMovementReasons.WalkNoPath.Code);

        var timeUnits = unit.TimeUnits;
        var energy = unit.Energy;
        var total = 0;
        if (unit.Kneeled)
        {
            timeUnits -= BattleUnit.KneelUpCost;
            total = BattleUnit.KneelUpCost;
        }
        if (armor.TurnBeforeFirstStep && path[0] != unit.Direction)
        {
            var turn = Math.Abs(unit.Direction - path[0]);
            if (turn > 4) turn = 8 - turn;
            turn *= unit.TurnCost(rules);
            timeUnits -= turn;
            total += turn;
        }
        // The preview checks the reserve as if an auto shot were reserved when nothing is.
        int? reserved = TimeUnitsReserved == (int)BattleActionType.None ? (int)BattleActionType.AutoShot : null;
        var steps = new List<BattlePathStepView>(path.Count);
        var position = unit.RequiredPosition;
        foreach (var direction in path)
        {
            var step = pathfinder.GetTuCost(position, direction, unit, (BattleActionMove)move);
            position = step.Position;
            energy -= step.Cost.Energy;
            timeUnits -= step.Cost.Time;
            total += step.Cost.Time;
            var check = CheckReservedTimeUnits(rules, unit, total, unit.Energy - energy, true, reserved);
            var reserveKept = check.Allowed || check.Unsupported is not null;
            var marker = timeUnits >= 0 && energy >= 0 ? reserveKept ? BattlePathMarker.Affordable : BattlePathMarker.ReservedTimeUnits : BattlePathMarker.Unaffordable;
            steps.Add(new BattlePathStepView(position, direction, Math.Max(0, timeUnits), Math.Max(0, energy), marker));
        }
        // A route through tiles the viewer has not discovered would show how passable they are: no preview then.
        if (steps.Any(step => !Contains(step.Position) || !ViewerKnowsTile(viewer, Map.TileIndex(step.Position)))) return null;
        return new BattlePathView(unit.Id, target, move, steps, total, unit.Energy - energy, "");
    }

    /// <summary>The tiles the unit can reach (<see cref="BattlePathfinder.FindReachable"/>, no action's cost kept back) that the viewer has discovered.</summary>
    internal BattleReachableView? ReachableView(RuntimeRuleCatalog rules, BattleViewer viewer, int unitId)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (ViewersUnitOnTurn(viewer, unitId) is not { } unit || !ArmorOf(rules, unit).AllowsMoving) return null;
        var reachable = new BattlePathfinder(this, rules).FindReachableNodes(unit, new BattleActionCost(unit));
        return new BattleReachableView(unit.Id, [.. reachable.Nodes.Where(node => ViewerKnowsTile(viewer, Map.TileIndex(node.Position))).Select(static node => node.Position)]);
    }

    /// <summary>Whether the unit up (or down) button is legal: the armor allows moving, a level lies there and <c>validateUpDown</c> accepts.</summary>
    private bool UpDownLegal(RuntimeRuleCatalog rules, BattleUnit unit, bool up)
    {
        if (unit.Position is not { } position || !ArmorOf(rules, unit).AllowsMoving) return false;
        if (!Contains(position with { Z = position.Z + (up ? 1 : -1) })) return false;
        return new BattlePathfinder(this, rules).ValidateUpDown(unit, position, up ? BattleUnit.DirectionUp : BattleUnit.DirectionDown);
    }
}
