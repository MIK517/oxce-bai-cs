using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedLbmCorpusTests
{
    [Fact]
    public void OwnedOriginalLbmFilesDecodeWithinBounds()
    {
        var data = TestFixtures.RequireOwnedData();

        var paths = TestFixtures.CorpusFiles("*.LBM", data);
        Assert.True(paths.Length >= 61, $"Expected the supplied TFTD LBM corpus; found {paths.Length} files.");
        TestFixtures.ForEachCorpusFile(paths, path =>
        {
            var image = IndexedLbmCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.Equal(256, image.Palette.Count);
        });
    }
}
