using System.Text.Json;
using Oxce.Core.Geometry;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class TerrainMapRouteFixtureTests
{
    [Fact]
    public void MapAndRouteSemanticsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "terrain-map-route.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
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

        Assert.True(
            CanonicalJson.SemanticallyEquals(expected, actual),
            $"Expected: {System.Text.Encoding.UTF8.GetString(expected)}{Environment.NewLine}Actual: {System.Text.Encoding.UTF8.GetString(actual)}");
    }

    private static BinaryDataReader ReadHex(string root, string relativePath) =>
        new(Convert.FromHexString(File.ReadAllText(Path.GetFullPath(relativePath, root)).Trim()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
