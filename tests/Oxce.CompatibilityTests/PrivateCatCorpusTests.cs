using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateCatCorpusTests
{
    [Fact]
    public void OwnedGameAndModCatFilesParseWithinBounds()
    {
        var privateMods = TestFixtures.RequirePrivateMods();
        var data = TestFixtures.RequireOwnedData();

        var paths = TestFixtures.CorpusFiles("*.CAT", data, privateMods);
        Assert.NotEmpty(paths);

        TestFixtures.ForEachCorpusFile(paths, path =>
        {
            var archive = CatArchive.Parse(BinaryDataReader.FromFile(path));
            var lastOffset = checked((int)new FileInfo(path).Length - 1);
            Assert.NotEmpty(archive.Entries);
            Assert.All(archive.Entries, entry => Assert.InRange(entry.Offset, 0, lastOffset));
        });
    }
}
