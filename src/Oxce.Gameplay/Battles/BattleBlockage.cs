using Oxce.Gameplay.Battles.Terrain;
using static Oxce.Gameplay.Battles.BattleBigWalls;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The kind of power <c>TileEngine::blockage</c> measures: <c>DT_NONE</c> (light and sight, the map data's vision block), <c>DT_SMOKE</c>
/// and any other damage type (<c>DT_IN</c> and the rest, the high explosive block at the default <c>battleTerrainSquishyness</c> of 1).
/// </summary>
internal enum BattleBlockType
{
    None,
    Damage,
    Smoke,
}

/// <summary>
/// <c>TileEngine::blockage</c>, <c>horizontalBlockage</c> and <c>verticalBlockage</c> (Phase 7 branch 4 S3): how much of a power a tile
/// part, a step to a neighbour or a step between levels stops. Tiles are indexes into the map, -1 for the reference's null tile (outside
/// the map). Sight (S4) and the fire and smoke routines (S9) call these too.
/// </summary>
internal static class BattleBlockage
{
    private const int DirectionUp = BattleUnit.DirectionUp;
    private const int DirectionDown = BattleUnit.DirectionDown;

    private const BattleTilePart West = BattleTilePart.WestWall;
    private const BattleTilePart North = BattleTilePart.NorthWall;
    private const BattleTilePart Obj = BattleTilePart.ObjectPart;

    /// <summary>MapData::getBlock(type) at the default terrain squishyness.</summary>
    private static int BlockOf(BattleMapData data, BattleBlockType type) => type switch
    {
        BattleBlockType.None => data.Block(1),
        BattleBlockType.Smoke => data.Block(3),
        _ => data.Block(2),
    };

    /// <summary>
    /// TileEngine::blockage: the amount one part of a tile blocks. A direction other than -1 (0-7, <see cref="BattleUnit.DirectionUp"/>,
    /// <see cref="BattleUnit.DirectionDown"/>) asks about the object part's big wall in that direction; 256 and more is a solid stop.
    /// </summary>
    public static int Blockage(BattleMap map, int tile, BattleTilePart part, BattleBlockType type, int direction = -1, bool checkingFromOrigin = false)
    {
        if (tile < 0) return 255;
        var blockage = 0;
        if (map.MapData(tile, part) is { } data)
        {
            var check = true;
            var wall = -1;
            if (direction != -1)
            {
                wall = map.MapData(tile, Obj)?.BigWall ?? 0;
                if (type != BattleBlockType.Smoke && checkingFromOrigin && wall is BigWallNesw or BigWallNwse) check = false;
                switch (direction)
                {
                    case 0:
                        if (wall is BigWallWest or BigWallEast or BigWallSouth or BigWallEastAndSouth) check = false;
                        break;
                    case 1:
                        if (wall is BigWallWest or BigWallSouth) check = false;
                        break;
                    case 2:
                        if (wall is BigWallNorth or BigWallSouth or BigWallWest or BigWallWestAndNorth) check = false;
                        break;
                    case 3:
                        if (wall is BigWallNorth or BigWallWest or BigWallWestAndNorth) check = false;
                        break;
                    case 4:
                        if (wall is BigWallWest or BigWallEast or BigWallNorth or BigWallWestAndNorth) check = false;
                        break;
                    case 5:
                        if (wall is BigWallNorth or BigWallEast) check = false;
                        break;
                    case 6:
                        if (wall is BigWallNorth or BigWallSouth or BigWallEast or BigWallEastAndSouth) check = false;
                        break;
                    case 7:
                        if (wall is BigWallSouth or BigWallEast or BigWallEastAndSouth) check = false;
                        break;
                    case DirectionUp:
                    case DirectionDown:
                        if (wall != 0 && wall != BigWallBlock) check = false;
                        break;
                }
            }
            else if (part == BattleTilePart.Floor && BlockOf(data, type) == 0)
            {
                if (type != BattleBlockType.None) blockage += data.Armor;
                else if (!data.HasNoFloor) return 256;
            }
            if (check)
            {
                // -1 is a regular wall, anything over 0 a big wall.
                if (type == BattleBlockType.Smoke && wall != 0 && !map.IsUfoDoorOpen(tile, part)) return 256;
                blockage += BlockOf(data, type);
            }
        }
        // An open UFO door is still closed behind the scenes, so it is told apart here.
        if (map.IsUfoDoorOpen(tile, part)) blockage = 0;
        return blockage;
    }

    /// <summary>TileEngine::verticalBlockage: the blockage of a step to the tile above or below (and beside it).</summary>
    public static int VerticalBlockage(BattleMap map, int start, int end, BattleBlockType type, bool skipObject = false)
    {
        if (start < 0 || end < 0) return 255;
        var startPosition = map.PositionOf(start);
        var endPosition = map.PositionOf(end);
        var direction = endPosition.Z - startPosition.Z;
        if (direction == 0) return 0;
        var block = 0;
        var beside = startPosition.X != endPosition.X || startPosition.Y != endPosition.Y;
        if (direction < 0)
        {
            block += Blockage(map, start, BattleTilePart.Floor, type);
            if (!skipObject) block += Blockage(map, start, Obj, type, DirectionDown);
            if (beside)
            {
                var other = map.TileAt(endPosition.X, endPosition.Y, startPosition.Z);
                block += HorizontalBlockage(map, start, other, type, skipObject);
                block += Blockage(map, other, BattleTilePart.Floor, type);
                if (!skipObject) block += Blockage(map, other, Obj, type, DirectionDown);
            }
        }
        else
        {
            var above = map.TileAt(startPosition.X, startPosition.Y, startPosition.Z + 1);
            block += Blockage(map, above, BattleTilePart.Floor, type);
            if (!skipObject) block += Blockage(map, above, Obj, type, DirectionUp);
            if (beside)
            {
                var other = map.TileAt(endPosition.X, endPosition.Y, startPosition.Z + 1);
                block += HorizontalBlockage(map, start, other, type, skipObject);
                block += Blockage(map, other, BattleTilePart.Floor, type);
                if (!skipObject) block += Blockage(map, other, Obj, type, DirectionUp);
            }
        }
        return block;
    }

    /// <summary>
    /// TileEngine::horizontalBlockage: the blockage of a step to a neighbour on the same level; -1 for light and sight (<see cref="BattleBlockType.None"/>)
    /// means the step ends in a big wall, which is revealed.
    /// </summary>
    public static int HorizontalBlockage(BattleMap map, int start, int end, BattleBlockType type, bool skipObject = false)
    {
        if (start < 0 || end < 0) return 255;
        var startPosition = map.PositionOf(start);
        var endPosition = map.PositionOf(end);
        if (startPosition.Z != endPosition.Z) return 0;
        var direction = BattleGeometry.VectorToDirection(endPosition.X - startPosition.X, endPosition.Y - startPosition.Y);
        if (direction == -1) return 0;
        int sx = startPosition.X, sy = startPosition.Y, sz = startPosition.Z;
        int North1() => map.TileAt(sx, sy - 1, sz);
        int East1() => map.TileAt(sx + 1, sy, sz);
        int South1() => map.TileAt(sx, sy + 1, sz);
        int West1() => map.TileAt(sx - 1, sy, sz);
        int B(int tile, BattleTilePart part, int blockDirection = -1) => Blockage(map, tile, part, type, blockDirection);
        bool ObjectIsNot(int tile, int bigWall) => tile >= 0 && map.MapData(tile, Obj) is { } data && data.BigWall != bigWall;
        var none = type == BattleBlockType.None;
        var block = 0;
        int tmp;
        switch (direction)
        {
            case 0:
                block = B(start, North);
                break;
            case 1:
                if (none)
                {
                    // Two-way diagonal check of the original game: up and right, else right and up.
                    block = B(start, North) + B(end, West);
                    tmp = North1();
                    if (ObjectIsNot(tmp, BigWallNesw)) block += B(tmp, Obj, 3);
                    if (block == 0) break;
                    tmp = East1();
                    block = B(tmp, North) + B(tmp, West);
                    if (ObjectIsNot(tmp, BigWallNesw)) block += B(tmp, Obj, 7);
                }
                else
                {
                    tmp = East1();
                    block = (B(start, North) + B(end, West)) / 2 + (B(tmp, West) + B(tmp, North)) / 2;
                    tmp = North1();
                    block += (B(tmp, Obj, 4) + B(tmp, Obj, 6)) / 2;
                }
                break;
            case 2:
                block = B(end, West);
                break;
            case 3:
                if (none)
                {
                    tmp = South1();
                    block = B(tmp, North) + B(end, West);
                    if (ObjectIsNot(tmp, BigWallNwse)) block += B(tmp, Obj, 1);
                    if (block == 0) break;
                    tmp = East1();
                    block = B(tmp, West) + B(end, North);
                    if (ObjectIsNot(tmp, BigWallNwse)) block += B(tmp, Obj, 5);
                }
                else
                {
                    block = (B(end, West) + B(end, North)) / 2 + (B(East1(), West) + B(South1(), North)) / 2;
                    block += (B(South1(), Obj, 0) + B(East1(), Obj, 6)) / 2;
                }
                break;
            case 4:
                block = B(end, North);
                break;
            case 5:
                if (none)
                {
                    tmp = South1();
                    block = B(tmp, North) + B(tmp, West);
                    if (ObjectIsNot(tmp, BigWallNesw)) block += B(tmp, Obj, 7);
                    if (block == 0) break;
                    block = B(start, West) + B(end, North);
                    tmp = West1();
                    if (ObjectIsNot(tmp, BigWallNesw)) block += B(tmp, Obj, 3);
                }
                else
                {
                    block = (B(end, North) + B(start, West)) / 2 + (B(South1(), West) + B(South1(), North)) / 2;
                    block += (B(South1(), Obj, 0) + B(West1(), Obj, 2)) / 2;
                }
                break;
            case 6:
                block = B(start, West);
                break;
            case 7:
                if (none)
                {
                    tmp = North1();
                    block = B(start, North) + B(tmp, West);
                    if (ObjectIsNot(tmp, BigWallNwse)) block += B(tmp, Obj, 5);
                    if (block == 0) break;
                    tmp = West1();
                    block = B(start, West) + B(tmp, North);
                    if (ObjectIsNot(tmp, BigWallNwse)) block += B(tmp, Obj, 1);
                }
                else
                {
                    block = (B(start, West) + B(start, North)) / 2 + (B(North1(), West) + B(West1(), North)) / 2;
                    block += (B(North1(), Obj, 4) + B(West1(), Obj, 2)) / 2;
                }
                break;
        }
        if (!skipObject || none && IsBigWall(map, start)) block += B(start, Obj, direction);
        if (type != BattleBlockType.None)
        {
            direction = (direction + 4) % 8;
            if (IsBigWall(map, end)) block += Blockage(map, end, Obj, type, direction, true);
        }
        else if (block <= 127)
        {
            direction = (direction + 4) % 8;
            if (Blockage(map, end, Obj, type, direction, true) > 127) return -1; // hit a big wall: reveal its tile
        }
        return block;
    }

    /// <summary>Tile::isBigWall: the object part is a big wall of any kind.</summary>
    public static bool IsBigWall(BattleMap map, int tile) => map.MapData(tile, Obj) is { BigWall: not 0 };
}
