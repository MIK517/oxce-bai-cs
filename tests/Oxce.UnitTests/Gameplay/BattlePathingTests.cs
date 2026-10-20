using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

/// <summary>Pathfinding::isBlockedDirection's wall checks, used to place large units (BattlescapeGenerator, S4b).</summary>
public sealed class BattlePathingTests
{
    // Objects: 0 floor, 1 impassable wall (TU walk 255), 2 passable wall, 3 NESW big wall, 4 east big wall.
    private static BattleMap Map()
    {
        static byte[] Record(params (int Offset, byte Value)[] values)
        {
            var record = new byte[McdTerrainCodec.RecordSize];
            foreach (var (offset, value) in values) record[offset] = value;
            return record;
        }
        var mcd = McdTerrainCodec.Decode(new BinaryDataReader(new[]
        {
            Record((39, 4)), Record((39, 255), (53, 1)), Record((39, 4), (53, 1)), Record((33, 2), (53, 3)), Record((33, 6), (53, 3)),
        }.SelectMany(static record => record).ToArray()));
        var set = new BattleMapDataSet("TEST", [.. mcd.Records.Select(static record => new BattleMapData(record))]);
        return new BattleMap(3, 3, 1, [set]);
    }

    private static int Tile(BattleMap map, int x, int y) => map.TileIndex(new BattlePosition(x, y, 0));

    [Fact]
    public void AnImpassableWestWallBlocksTheMoveEastIntoItsTile()
    {
        var map = Map();
        map.SetPart(Tile(map, 1, 0), BattleTilePart.WestWall, 1, 0);
        Assert.True(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 0, 0), 2));
        Assert.True(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(1, 0, 0), 6));
        Assert.False(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 1, 0), 2));
        map.SetPart(Tile(map, 1, 0), BattleTilePart.WestWall, 2, 0);
        Assert.False(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 0, 0), 2));
    }

    [Fact]
    public void BigWallsBlockByTypeAndTheMapEdgeBlocks()
    {
        var map = Map();
        // A NESW big wall east of the start blocks the south-east move but not the north-east one, which excludes it.
        map.SetPart(Tile(map, 1, 1), BattleTilePart.ObjectPart, 3, 0);
        Assert.True(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 1, 0), 3));
        Assert.False(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 2, 0), 1));
        // An east big wall blocks the west wall of the next tile.
        map.SetPart(Tile(map, 0, 2), BattleTilePart.ObjectPart, 4, 0);
        Assert.True(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 2, 0), 2));
        // Leaving the map is blocked.
        Assert.True(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(2, 0, 0), 2));
        Assert.True(BattlePathing.IsBlockedDirection(map, 0, new BattlePosition(0, 0, 0), 0));
    }
}
