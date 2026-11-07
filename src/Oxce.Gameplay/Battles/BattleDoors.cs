using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>What a unit's try at a door did (the result of <c>TileEngine::unitOpensDoor</c>).</summary>
public enum BattleDoorResult
{
    /// <summary>-1: no door to open (or one the unit cannot open: a big unit at a swinging door, a unit standing in the doorway).</summary>
    None,

    /// <summary>0: a swinging door opened.</summary>
    Opened,

    /// <summary>1: a UFO door (and the UFO doors next to it) opened. The reference animates this over frames; the port opens it at once.</summary>
    UfoDoorOpened,

    /// <summary>4: the unit has not enough time units (or energy) for the door; nothing changed.</summary>
    NotEnoughTimeUnits,

    /// <summary>5: opening it would break the time units reserved for the unit's weapon or kneeling; the door is open but nothing was paid.</summary>
    ReservedTimeUnits,
}

/// <summary>What <see cref="BattleState.UnitOpensDoor"/> did.</summary>
internal readonly record struct BattleDoorAttempt(BattleDoorResult Result, int DoorsOpened, BattlePosition? Centre, int TimeUnitsSpent, bool Changed)
{
    /// <summary>Nothing happened.</summary>
    internal static readonly BattleDoorAttempt None = new(BattleDoorResult.None, 0, null, 0, false);
}

/// <summary>
/// Doors (Phase 7 branch 4 S6): <c>TileEngine::unitOpensDoor</c>, <c>Tile::openDoor</c>, <c>checkAdjacentDoors</c> and <c>closeUfoDoors</c>.
/// The reference opens a UFO door over animation frames and the walk waits for it; the port collapses the animation, so a UFO door is open
/// (frame 7) as soon as it starts to open and the reference's result 3 (still opening) never happens.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// <c>TileEngine::unitOpensDoor(unit, rClick, dir)</c> (<paramref name="direction"/> -1 is the unit's own): looks for the first door part
    /// in front of the unit it could open, from the tile above when it stands on stairs, opens it, pays and updates light and sight through the
    /// doorway. Like the reference, the door is opened before the reserved time units are checked, so a result of
    /// <see cref="BattleDoorResult.ReservedTimeUnits"/> leaves it open and unpaid. Returns a refusal, before any change, for the hostile
    /// side's reserve (branch 6). The AI's reactivation after the right click is branch 6's.
    /// </summary>
    internal CampaignReason? UnitOpensDoor(RuntimeRuleCatalog rules, BattleUnit unit, bool rightClick, int direction, out BattleDoorAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(unit);
        attempt = BattleDoorAttempt.None;
        var origin = unit.Position!.Value;
        var size = unit.ArmorSize;
        var stairs = Map.TerrainLevel(Map.TileIndex(origin)) < -12 ? 1 : 0;
        var facing = direction == -1 ? unit.Direction : direction;

        // The search: the first part, in the reference's order, that answers anything but -1. Nothing changes until one is found.
        for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
            {
                var unitTile = new BattlePosition(origin.X + x, origin.Y + y, origin.Z + stairs);
                if (Map.TileAt(unitTile.X, unitTile.Y, unitTile.Z) < 0) continue;
                foreach (var (dx, dy, part) in DoorChecks(facing, x, y, rightClick))
                {
                    var at = new BattlePosition(unitTile.X + dx, unitTile.Y + dy, unitTile.Z);
                    var tile = Map.TileAt(at.X, at.Y, at.Z);
                    if (tile < 0) continue;
                    var code = OpenDoor(rules, tile, part, unit, rightClick, apply: false);
                    if (code == DoorNone) continue;
                    // The hostile side's reserve is not ported: refuse before the door opens.
                    if (code is DoorOpened or DoorUfoOpening && Side == BattleFaction.Hostile && unit.Faction == BattleFaction.Hostile)
                        return HostileReserveUnsupportedReason;
                    attempt = FinishDoor(rules, unit, tile, at, part, code, rightClick);
                    return null;
                }
            }
        return null;
    }

    private const int DoorNone = -1;
    private const int DoorOpened = 0;
    private const int DoorUfoOpening = 1;
    private const int DoorNotEnough = 4;

    /// <summary>The rest of <c>unitOpensDoor</c> once a part answered: the door opens, the cost is worked out, reserved and paid.</summary>
    private BattleDoorAttempt FinishDoor(RuntimeRuleCatalog rules, BattleUnit unit, int tile, BattlePosition at, BattleTilePart part, int code, bool rightClick)
    {
        if (code == DoorNotEnough)
            return new(BattleDoorResult.NotEnoughTimeUnits, 0, null, 0, false);
        var movement = unit.MovementType;
        // Result 1 is worked out with the door closed: the reference reads the cost with the door at frame 1, which still costs.
        var cost = code == DoorUfoOpening ? Map.TimeUnitCost(tile, part, movement) : 0;
        var doorsOpened = 0;
        BattlePosition centre = at;
        OpenDoor(rules, tile, part, unit, rightClick, apply: true);
        if (code == DoorOpened)
        {
            doorsOpened++;
            if (rightClick)
            {
                // The reference prices the other wall of the tile (a door that has moved to another part leaves this one for its other half).
                var other = part == BattleTilePart.WestWall ? BattleTilePart.NorthWall : BattleTilePart.WestWall;
                cost = Map.TimeUnitCost(tile, other, movement);
            }
        }
        else
        {
            var (adjacent, adjacentCentre) = CheckAdjacentDoors(rules, at, part);
            doorsOpened += adjacent + 1;
            centre = adjacentCentre;
        }

        if (!CheckReservedTimeUnits(rules, unit, cost, 0, false).Allowed)
            return new(BattleDoorResult.ReservedTimeUnits, doorsOpened, centre, 0, true);
        if (!unit.SpendTimeUnits(cost))
            return new(BattleDoorResult.NotEnoughTimeUnits, doorsOpened, centre, 0, true);
        CalculateLighting(rules, BattleLightLayer.Fire, centre, doorsOpened, true);
        // Update the field of view through the doorway.
        Sight().CalculateFov(centre, doorsOpened, true, true);
        unit.UpdateEnemyKnowledge(Map.TileIndex(unit.Position!.Value), door: true);
        return new(code == DoorOpened ? BattleDoorResult.Opened : BattleDoorResult.UfoDoorOpened, doorsOpened, centre, cost, true);
    }

    /// <summary>
    /// <c>Tile::openDoor(part, unit, reserve, rClick, kneelReserved)</c>: -1 for no door, 0 for a swinging door, 1 for a closed UFO door, 4
    /// for not enough time units. With <paramref name="apply"/> the door is changed (a swinging door moves to its alternative object, a UFO
    /// door's frame is set); without, only the answer is worked out.
    /// </summary>
    private int OpenDoor(RuntimeRuleCatalog rules, int tile, BattleTilePart part, BattleUnit? unit, bool rightClick, bool apply)
    {
        if (Map.MapData(tile, part) is not { } data) return DoorNone;
        var cost = new BattleActionCost(unit);
        if (unit is not null)
        {
            var tuCost = BattleMap.MovementCost(data, unit.MovementType);
            cost = BattleActionCost.For(rules, (BattleActionType)TimeUnitsReserved, unit, MainHandWeapon(rules, unit, quickest: false));
            tuCost += KneelReserved && !unit.Kneeled && AllowsKneeling(rules, unit) ? BattleUnit.KneelDownCost : 0;
            cost.Time += tuCost;
            if (!rightClick) cost.Energy += tuCost / 2;
        }

        if (data.IsDoor)
        {
            // Double-wide units cannot open swinging doors.
            if (unit is { IsBigUnit: true }) return DoorNone;
            if (unit is not null && cost.Time != 0 && !cost.HaveTimeUnits()) return DoorNotEnough;
            // Another unit in the doorway keeps it shut (a big unit's other tiles do not count).
            if (Map.UnitAt(tile) is { } standing && UnitById(standing) is { } blocker && blocker != unit && blocker.Position != Map.PositionOf(tile))
                return DoorNone;
            // A door whose alternative lies past its data set's end has no object to turn into (the reference reads out of range; ADR 0040).
            var (_, objectSet) = Map.Part(tile, part);
            if ((uint)data.AlternateMcd >= (uint)Map.DataSets[objectSet].Objects.Count) return DoorNone;
            if (apply)
            {
                var (_, set) = Map.Part(tile, part);
                var alternative = data.AlternateMcd;
                var replacement = Map.DataSets[set].Objects[alternative];
                // The alternative goes to the part its own object type names, and then this part is emptied, as the reference's two calls do.
                Map.SetPart(tile, replacement.ObjectType, alternative, set);
                Map.SetPart(tile, part, -1, -1);
            }
            return DoorOpened;
        }
        if (data.IsUfoDoor && !Map.HasDoorFrame(tile, part))
        {
            if (unit is not null && cost.Time != 0 && !cost.HaveTimeUnits()) return DoorNotEnough;
            if (apply) Map.SetDoorFrame(tile, part, true);
            return DoorUfoOpening;
        }
        return DoorNone;
    }

    /// <summary>
    /// <c>TileEngine::checkAdjacentDoors</c>: opens the UFO doors of the same part along the wall either side of the one at
    /// <paramref name="position"/> (a run of UFO door parts; one already open does not stop the run). Returns how many it opened and the
    /// door's centre, the starting position moved by half the net offset.
    /// </summary>
    private (int Opened, BattlePosition Centre) CheckAdjacentDoors(RuntimeRuleCatalog rules, BattlePosition position, BattleTilePart part)
    {
        var west = part == BattleTilePart.WestWall;
        var opened = 0;
        var offset = 0;
        foreach (var step in new[] { 1, -1 })
            for (var i = step; ; i += step)
            {
                var tile = west ? Map.TileAt(position.X, position.Y + i, position.Z) : Map.TileAt(position.X + i, position.Y, position.Z);
                if (tile < 0 || Map.MapData(tile, part) is not { IsUfoDoor: true }) break;
                if (OpenDoor(rules, tile, part, null, false, apply: true) == DoorUfoOpening)
                {
                    opened++;
                    offset += step;
                }
            }
        offset /= 2; // the middle of the door
        return (opened, west ? position with { Y = position.Y + offset } : position with { X = position.X + offset });
    }

    /// <summary>
    /// <c>TileEngine::closeUfoDoors</c>: closes every open UFO door, except those a big unit stands in the doorway of (a big unit's tile whose
    /// open north wall has the unit's own tile north of it, or whose open west wall has it to the west, keeps that tile's doors). Returns the
    /// number of tiles' doors closed, as the reference does (a tile counts once whatever its doors). Branch 6's end of turn calls it, and
    /// recomputes the light and sight the closed doors change; the reference's visibility cache has no counterpart.
    /// </summary>
    internal int CloseUfoDoors()
    {
        var closed = 0;
        for (var tile = 0; tile < Map.TileCount; tile++)
        {
            if (Map.UnitAt(tile) is { } id && UnitById(id) is { IsBigUnit: true })
            {
                var position = Map.PositionOf(tile);
                var north = Map.TileAt(position.X, position.Y - 1, position.Z);
                var west = Map.TileAt(position.X - 1, position.Y, position.Z);
                if (Map.IsUfoDoorOpen(tile, BattleTilePart.NorthWall) && north >= 0 && Map.UnitAt(north) == id ||
                    Map.IsUfoDoorOpen(tile, BattleTilePart.WestWall) && west >= 0 && Map.UnitAt(west) == id)
                    continue;
            }
            // Tile::closeUfoDoor: the floor, west wall and north wall; only walls keep a frame here. A tile counts once.
            var any = false;
            foreach (var part in new[] { BattleTilePart.WestWall, BattleTilePart.NorthWall })
                if (Map.IsUfoDoorOpen(tile, part))
                {
                    Map.SetDoorFrame(tile, part, false);
                    any = true;
                }
            if (any) closed++;
        }
        return closed;
    }

    /// <summary>
    /// The tiles and wall parts <c>unitOpensDoor</c> checks, for the unit tile (x, y) of its footprint; the right click adds the tiles beside
    /// the diagonal directions.
    /// </summary>
    private static IEnumerable<(int X, int Y, BattleTilePart Part)> DoorChecks(int direction, int x, int y, bool rightClick)
    {
        const BattleTilePart west = BattleTilePart.WestWall;
        const BattleTilePart north = BattleTilePart.NorthWall;
        switch (direction)
        {
            case 0:
                yield return (0, 0, north);
                if (x != 0) yield return (0, -1, west);
                break;
            case 1:
                yield return (0, 0, north);
                yield return (1, -1, west);
                if (rightClick)
                {
                    yield return (1, 0, west);
                    yield return (1, 0, north);
                }
                break;
            case 2:
                yield return (1, 0, west);
                break;
            case 3:
                if (y == 0) yield return (1, 1, west);
                if (x == 0) yield return (1, 1, north);
                if (rightClick)
                {
                    yield return (1, 0, west);
                    yield return (0, 1, north);
                }
                break;
            case 4:
                yield return (0, 1, north);
                break;
            case 5:
                yield return (0, 0, west);
                yield return (-1, 1, north);
                if (rightClick)
                {
                    yield return (0, 1, west);
                    yield return (0, 1, north);
                }
                break;
            case 6:
                yield return (0, 0, west);
                if (y != 0) yield return (-1, 0, north);
                break;
            case 7:
                yield return (0, 0, west);
                yield return (0, 0, north);
                if (x != 0) yield return (-1, -1, west);
                if (y != 0) yield return (-1, -1, north);
                if (rightClick)
                {
                    yield return (0, -1, west);
                    yield return (-1, 0, north);
                }
                break;
        }
    }
}
