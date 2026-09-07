using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateTerrainMapRouteCorpusTests
{
    private static readonly string[] ExpectedOrphanRoutes =
    [
        "data/TFTD/ROUTES/A_BASE18.RMP",
        "data/TFTD/ROUTES/A_BASE20.RMP",
        "fixtures/private/mods/40k/ROUTES/FOREST10.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR00.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR01.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR02.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR03.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR04.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR05.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR06.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR07.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR08.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR09.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR10.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR11.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR12.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR13.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR14.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR15.RMP",
        "fixtures/private/mods/40k/ROUTES/FORESTPOLAR16.RMP",
        "fixtures/private/mods/40k/ROUTES/RHINOCHAOS_UFO_spawnlevel0.RMP",
        "fixtures/private/mods/40k/ROUTES/THUNDERHAWKGK.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/JUNGLE09.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/MARS08.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/MARS09.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/MOUNT05.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/POLAR08.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/UFO1A.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_00.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_02.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_03.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_04.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_05.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_06.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_07.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_09.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_10.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_11.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_12.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_13.RMP",
        "fixtures/private/mods/Final Mod Pack/ROUTES/XBASE_15.RMP",
        "fixtures/private/mods/rosigma/ROUTES/CH02HammerheadTopFloor.RMP",
        "fixtures/private/mods/rosigma/ROUTES/GORGONTOPCOVER.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN00.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN01.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN02.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN03.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN04.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN05.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN06.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN07.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN08.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN09.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN10.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN11.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN12.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN13.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN14.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN15.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN16.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN17.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN18.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN19.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN20.RMP",
        "fixtures/private/mods/rosigma/ROUTES/INDUSTRIALURBAN21.RMP",
        "fixtures/private/mods/rosigma/ROUTES/MARS08.RMP",
        "fixtures/private/mods/rosigma/ROUTES/SWARMYUFO.RMP",
        "fixtures/private/mods/rosigma/ROUTES/UBASE_TZ07C.RMP",
        "fixtures/private/mods/rosigma/ROUTES/URBAN00.RMP",
        "fixtures/private/mods/rosigma/ROUTES/URBAN01.RMP",
        "fixtures/private/mods/rosigma/ROUTES/URBAN02.RMP",
        "fixtures/private/mods/rosigma/ROUTES/URBAN03.RMP",
        "fixtures/private/mods/rosigma/ROUTES/URBAN05.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/DAWN14.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/DAWN15.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/DAWN16.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/DAWN17.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/DAWN18.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/FORESTMOUNT27.RMP",
        "fixtures/private/mods/XCOM-Terrain-Pack/ROUTES/MADURBAN75.RMP",
    ];

    [Fact]
    public void OwnedGameAndModMapsAndRoutesParseWithinBounds()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
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
        var orphanRoutes = new List<string>();
        foreach (var assetRoot in assetRoots)
        {
            var mapPaths = Directory.EnumerateFiles(assetRoot, "*.MAP", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();
            mapCount += mapPaths.Length;
            var mapsByName = mapPaths
                .GroupBy(GetBaseName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var mapPath in mapPaths)
            {
                var map = XcomMapCodec.Decode(BinaryDataReader.FromFile(mapPath));
                Assert.Equal(checked(map.Width * map.Length * map.Levels), map.Tiles.Count);
                Assert.InRange(map.TrailingData.Length, 0, XcomMapTileRecord.Size - 1);
            }

            foreach (var routePath in Directory.EnumerateFiles(assetRoot, "*.RMP", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                if (!mapsByName.TryGetValue(GetBaseName(routePath), out var candidates))
                {
                    orphanRoutes.Add(RelativePath(root, routePath));
                    continue;
                }

                routeCount++;
                var mapPath = SelectCanonicalMap(assetRoot, candidates);
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
        Assert.Equal(ExpectedOrphanRoutes.Order(StringComparer.Ordinal), orphanRoutes.Order(StringComparer.Ordinal));
    }

    private static string GetBaseName(string path) =>
        Path.GetFileNameWithoutExtension(path)
        ?? throw new InvalidDataException($"Asset path '{path}' has no file name.");

    private static string SelectCanonicalMap(string assetRoot, IReadOnlyList<string> candidates)
    {
        var ranked = candidates
            .Select(path => new
            {
                Path = path,
                Relative = RelativePath(assetRoot, path),
            })
            .Select(candidate => new
            {
                candidate.Path,
                CanonicalRank = candidate.Relative.StartsWith("MAPS/", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                Depth = candidate.Relative.Count(character => character == '/'),
            })
            .OrderBy(candidate => candidate.CanonicalRank)
            .ThenBy(candidate => candidate.Depth)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        var selected = ranked[0];
        Assert.Single(ranked, candidate =>
            candidate.CanonicalRank == selected.CanonicalRank && candidate.Depth == selected.Depth);
        return selected.Path;
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

}
