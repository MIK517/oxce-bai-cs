using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedBmpCorpusTests
{
    [Fact]
    public void OwnedModIndexedBmpFilesDecodeWithinBounds()
    {
        var privateMods = TestFixtures.RequirePrivateMods();

        var paths = TestFixtures.CorpusFiles("*.bmp", privateMods);
        Assert.NotEmpty(paths);
        TestFixtures.ForEachCorpusFile(paths, path =>
        {
            var image = IndexedBmpCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.Equal(256, image.Palette.Count);
        });
    }
}
