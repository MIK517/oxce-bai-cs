using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateTerrainMapRouteCorpusTests
{
    [Fact]
    public void OwnedGameAndModMapsAndRoutesParseWithinBounds()
    {
        var root = FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        var data = Path.Combine(root, "data");
        Assert.SkipUnless(
            Directory.Exists(privateMods) && Directory.Exists(data),
            "Owned game and private mod assets are not available in this checkout.");

        var assetRoots = Directory.EnumerateDirectories(data)
            .Concat(Directory.EnumerateDirectories(privateMods))
            .ToArray();
        var mapCount = 0;
        var routeCount = 0;
        foreach (var assetRoot in assetRoots)
        {
            var mapPaths = Directory.EnumerateFiles(assetRoot, "*.MAP", SearchOption.AllDirectories).ToArray();
            mapCount += mapPaths.Length;
            var mapsByName = mapPaths
                .GroupBy(GetBaseName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var mapPath in mapPaths)
            {
                var map = XcomMapCodec.Decode(BinaryDataReader.FromFile(mapPath));
                Assert.Equal(checked(map.Width * map.Length * map.Levels), map.Tiles.Count);
                Assert.InRange(map.TrailingData.Length, 0, XcomMapTileRecord.Size - 1);
            }

            foreach (var routePath in Directory.EnumerateFiles(assetRoot, "*.RMP", SearchOption.AllDirectories))
            {
                if (!mapsByName.TryGetValue(GetBaseName(routePath), out var mapPath))
                {
                    continue;
                }

                routeCount++;
                var map = XcomMapCodec.Decode(BinaryDataReader.FromFile(mapPath));
                var route = RmpRouteCodec.Decode(
                    BinaryDataReader.FromFile(routePath),
                    map.Width,
                    map.Length,
                    map.Levels);
                Assert.InRange(route.TrailingData.Length, 0, RmpRouteCodec.RecordSize - 1);
            }
        }

        Assert.True(mapCount > 4_000, $"Expected the supplied MAP corpus; found {mapCount} files.");
        Assert.True(routeCount > 4_000, $"Expected the supplied paired RMP corpus; found {routeCount} files.");
    }

    private static string GetBaseName(string path) =>
        Path.GetFileNameWithoutExtension(path)
        ?? throw new InvalidDataException($"Asset path '{path}' has no file name.");

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
