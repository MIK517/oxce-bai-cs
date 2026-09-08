using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateCatCorpusTests
{
    [Fact]
    public void OwnedGameAndModCatFilesParseWithinBounds()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        var data = Path.Combine(root, "data");
        Assert.SkipUnless(
            Directory.Exists(privateMods) && Directory.Exists(data),
            "Owned game and private mod assets are not available in this checkout.");

        var paths = Directory.EnumerateFiles(data, "*.CAT", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(privateMods, "*.CAT", SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(paths);

        foreach (var path in paths)
        {
            var archive = CatArchive.Parse(BinaryDataReader.FromFile(path));
            Assert.NotEmpty(archive.Entries);
            Assert.All(archive.Entries, entry => Assert.InRange(entry.Offset, 0, checked((int)new FileInfo(path).Length - 1)));
        }
    }

}
