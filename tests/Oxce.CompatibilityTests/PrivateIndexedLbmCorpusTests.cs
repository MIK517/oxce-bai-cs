using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedLbmCorpusTests
{
    [Fact]
    public void OwnedOriginalLbmFilesDecodeWithinBounds()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var data = Path.Combine(root, "data");
        Assert.SkipUnless(
            Directory.Exists(data),
            "Private original-game assets are not available in this checkout.");

        var paths = Directory.EnumerateFiles(data, "*.LBM", SearchOption.AllDirectories).ToArray();
        Assert.True(paths.Length >= 61, $"Expected the supplied TFTD LBM corpus; found {paths.Length} files.");
        foreach (var path in paths)
        {
            var image = IndexedLbmCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.Equal(256, image.Palette.Count);
        }
    }

}
