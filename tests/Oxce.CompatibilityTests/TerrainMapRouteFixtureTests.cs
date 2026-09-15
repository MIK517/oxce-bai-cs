using System.Text.Json;
using Oxce.Core.Geometry;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class TerrainMapRouteFixtureTests
{
    [Fact]
    public void MapAndRouteSemanticsMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("terrain-map-route");
        var map = XcomMapCodec.Decode(ReadHex(root, manifest.Inputs[0].Path));
        var route = RmpRouteCodec.Decode(
            ReadHex(root, manifest.Inputs[1].Path),
            map.Width,
            map.Length,
            map.Levels,
            nodeOffset: 10,
            positionOffset: new Position3(20, 30, 40),
            segment: 6);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            map = new
            {
                length = map.Length,
                levels = map.Levels,
                tiles = map.Tiles.Select(tile => new[]
                {
                    (int)tile.Floor,
                    tile.WestWall,
                    tile.NorthWall,
                    tile.ObjectPart,
                }),
                trailing = Convert.ToHexString(map.TrailingData.Span),
                width = map.Width,
            },
            route = new
            {
                nodes = route.Nodes.Select(node => new
                {
                    dummy = node.IsDummy,
                    flags = node.Flags,
                    id = node.Id,
                    index = node.Index,
                    links = node.Links,
                    position = new[] { node.Position.X, node.Position.Y, node.Position.Z },
                    priority = node.Priority,
                    rank = node.Rank,
                    reserved = node.Reserved,
                    segment = node.Segment,
                    type = node.Type,
                }),
                trailing = Convert.ToHexString(route.TrailingData.Span),
            },
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static BinaryDataReader ReadHex(string root, string relativePath) =>
        new(Convert.FromHexString(File.ReadAllText(Path.GetFullPath(relativePath, root)).Trim()));
}
