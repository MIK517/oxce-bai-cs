using Oxce.Core.Geometry;
using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class TerrainMapCodecTests
{
    [Fact]
    public void MapDecodeUsesYxzHeaderAndTopDownLayerOrder()
    {
        var map = XcomMapCodec.Decode(Reader(
            "020202" +
            "01020304" + "05060708" + "090A0B0C" + "0D0E0F10" +
            "11121314" + "15161718" + "191A1B1C" + "1D1E1F20" +
            "00"));

        Assert.Equal(2, map.Width);
        Assert.Equal(2, map.Length);
        Assert.Equal(2, map.Levels);
        Assert.Equal(new XcomMapTileRecord(1, 2, 3, 4), map.GetTile(0, 0, 1));
        Assert.Equal(new XcomMapTileRecord(29, 30, 31, 32), map.GetTile(1, 1, 0));
        Assert.Equal(new byte[] { 0 }, map.TrailingData.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0102")]
    [InlineData("000101")]
    [InlineData("010101010203")]
    [InlineData("0101010102030405060708")]
    public void MapDecodeRejectsInvalidDimensionsAndRecordCounts(string hex)
    {
        Assert.Throws<InvalidDataException>(() => XcomMapCodec.Decode(Reader(hex)));
    }

    [Fact]
    public void MapDecodeEnforcesTileLimit()
    {
        Assert.Throws<InvalidDataException>(() => XcomMapCodec.Decode(Reader("020202"), maxTiles: 7));
    }

    [Fact]
    public void RouteDecodeTransformsCoordinatesLinksAndCullsBadNodes()
    {
        var valid = RouteRecord(y: 2, x: 1, z: 0, links: [1, 255, 254, 253, 252], metadata: [7, 8, 9, 5, 11]);
        var invalid = RouteRecord(y: 1, x: 9, z: 0, links: [0, 0, 0, 0, 0], metadata: [1, 2, 3, 4, 5]);
        var data = valid.Concat(invalid).Concat(new byte[] { 0, 0 }).ToArray();

        var route = RmpRouteCodec.Decode(
            new BinaryDataReader(data),
            mapWidth: 4,
            mapLength: 4,
            mapLevels: 2,
            nodeOffset: 10,
            positionOffset: new Position3(20, 30, 40),
            segment: 6);

        Assert.Equal(2, route.Nodes.Count);
        var node = route.Nodes[0];
        Assert.Equal(10, node.Id);
        Assert.Equal(new Position3(21, 32, 41), node.Position);
        Assert.Equal(6, node.Segment);
        Assert.Equal(7, node.Type);
        Assert.Equal(8, node.Rank);
        Assert.Equal(9, node.Flags);
        Assert.Equal(5, node.Reserved);
        Assert.Equal(11, node.Priority);
        Assert.Collection(
            node.Links,
            link => Assert.Equal(-1, link),
            link => Assert.Equal(-1, link),
            link => Assert.Equal(-2, link),
            link => Assert.Equal(-3, link),
            link => Assert.Equal(-4, link));
        Assert.True(route.Nodes[1].IsDummy);
        Assert.Equal(new byte[] { 0, 0 }, route.TrailingData.ToArray());
    }

    [Fact]
    public void RouteDecodeEnforcesNodeLimit()
    {
        Assert.Throws<InvalidDataException>(
            () => RmpRouteCodec.Decode(Reader(new string('0', 96)), 1, 1, 1, maxNodes: 1));
    }

    private static byte[] RouteRecord(byte y, byte x, byte z, byte[] links, byte[] metadata)
    {
        var result = new byte[RmpRouteCodec.RecordSize];
        result[0] = y;
        result[1] = x;
        result[2] = z;
        for (var index = 0; index < links.Length; index++)
        {
            result[4 + (index * 3)] = links[index];
        }

        metadata.CopyTo(result, 19);
        return result;
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
