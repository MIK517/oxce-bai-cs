using Oxce.Gameplay.Battles.Terrain;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The wall checks of <c>Pathfinding::isBlockedDirection</c> and <c>isBlocked</c> for a unit moving normally (no missile):
/// west and north walls, big walls in the object part and the neighbouring tiles' facing big walls, and impassable wall
/// parts. The floor part (units in the way, falling) belongs to pathfinding (Phase 7 branch 4); no direction here asks
/// for it. <c>strictBlockedChecking</c> is a user option the port does not have yet; it is off, its default.
/// </summary>
internal static class BattlePathing
{
    /// <summary>Pathfinding::INVALID_MOVE_COST.</summary>
    public const int InvalidMoveCost = 255;

    // Pathfinding's big wall types.
    private const int BigWallNesw = 2;
    private const int BigWallNwse = 3;
    private const int BigWallWest = 4;
    private const int BigWallNorth = 5;
    private const int BigWallEast = 6;
    private const int BigWallSouth = 7;
    private const int BigWallEastAndSouth = 8;
    private const int BigWallWestAndNorth = 9;

    private enum Part { WestWall, NorthWall, BigWall }

    /// <summary>Whether something blocks a move from the tile in a direction (0 north, clockwise to 7 north-west).</summary>
    public static bool IsBlockedDirection(BattleMap map, int movementType, BattlePosition start, int direction)
    {
        var north = start with { Y = start.Y - 1 };
        var east = start with { X = start.X + 1 };
        var south = start with { Y = start.Y + 1 };
        var west = start with { X = start.X - 1 };
        bool Blocked(BattlePosition position, Part part, int exclusion = 0) => IsBlocked(map, movementType, position, part, exclusion);
        return direction switch
        {
            0 => Blocked(start, Part.NorthWall),
            1 => Blocked(start, Part.NorthWall) || Blocked(north with { X = start.X + 1 }, Part.WestWall) || Blocked(east, Part.WestWall) ||
                Blocked(east, Part.NorthWall) || Blocked(east, Part.BigWall, BigWallNesw) || Blocked(north, Part.BigWall, BigWallNesw),
            2 => Blocked(east, Part.WestWall),
            3 => Blocked(east, Part.WestWall) || Blocked(south, Part.NorthWall) || Blocked(south with { X = start.X + 1 }, Part.NorthWall) ||
                Blocked(south with { X = start.X + 1 }, Part.WestWall) || Blocked(east, Part.BigWall, BigWallNwse) ||
                Blocked(south, Part.BigWall, BigWallNwse),
            4 => Blocked(south, Part.NorthWall),
            5 => Blocked(start, Part.WestWall) || Blocked(south, Part.WestWall) || Blocked(south, Part.NorthWall) ||
                Blocked(south, Part.BigWall, BigWallNesw) || Blocked(west, Part.BigWall, BigWallNesw) ||
                Blocked(south with { X = start.X - 1 }, Part.NorthWall),
            6 => Blocked(start, Part.WestWall),
            7 => Blocked(start, Part.WestWall) || Blocked(start, Part.NorthWall) || Blocked(west, Part.NorthWall) ||
                Blocked(north, Part.WestWall) || Blocked(north, Part.BigWall, BigWallNwse) || Blocked(west, Part.BigWall, BigWallNwse),
            _ => false,
        };
    }

    /// <summary>Pathfinding::isBlocked for a wall or big wall part; outside the map is blocked.</summary>
    private static bool IsBlocked(BattleMap map, int movementType, BattlePosition position, Part part, int bigWallExclusion)
    {
        if (!map.Contains(position)) return true;
        var tile = map.TileIndex(position);
        var bigWall = map.MapData(tile, BattleTilePart.ObjectPart)?.BigWall ?? 0;
        if (part == Part.BigWall) return bigWall != 0 && bigWall <= BigWallNwse && bigWall != bigWallExclusion;
        var (wallPart, own, neighbourOffset, facing) = part == Part.WestWall
            ? (BattleTilePart.WestWall, BigWallWest, position with { X = position.X - 1 }, BigWallEast)
            : (BattleTilePart.NorthWall, BigWallNorth, position with { Y = position.Y - 1 }, BigWallSouth);
        if (bigWall == own || bigWall == BigWallWestAndNorth) return true;
        if (!map.Contains(neighbourOffset)) return true;
        var neighbour = map.MapData(map.TileIndex(neighbourOffset), BattleTilePart.ObjectPart)?.BigWall ?? 0;
        if (neighbour == facing || neighbour == BigWallEastAndSouth) return true;
        return map.TimeUnitCost(tile, wallPart, movementType) == InvalidMoveCost;
    }
}
