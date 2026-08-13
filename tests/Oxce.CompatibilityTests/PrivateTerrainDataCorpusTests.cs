using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateTerrainDataCorpusTests
{
    [Fact]
    public void OwnedGameAndModMcdAndLoftempsFilesParseWithinBounds()
    {
        var root = FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        var data = Path.Combine(root, "data");
        Assert.SkipUnless(
            Directory.Exists(privateMods) && Directory.Exists(data),
            "Owned game and private mod assets are not available in this checkout.");

        var assetRoots = new[] { data, privateMods };
        var mcdPaths = assetRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*.MCD", SearchOption.AllDirectories))
            .ToArray();
        var loftempsPaths = assetRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "LOFTEMPS.DAT", SearchOption.AllDirectories))
            .ToArray();
        Assert.True(mcdPaths.Length > 1_000, $"Expected the supplied MCD corpus; found {mcdPaths.Length} files.");
        Assert.True(loftempsPaths.Length >= 5, $"Expected the supplied LOFTEMPS corpus; found {loftempsPaths.Length} files.");

        foreach (var path in mcdPaths)
        {
            var dataSet = McdTerrainCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.NotEmpty(dataSet.Records);
            Assert.InRange(dataSet.TrailingData.Length, 0, McdTerrainCodec.RecordSize - 1);
        }

        foreach (var path in loftempsPaths)
        {
            var voxelData = LoftempsCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.NotEmpty(voxelData.Values);
            Assert.InRange(voxelData.TrailingData.Length, 0, sizeof(ushort) - 1);
        }
    }

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
