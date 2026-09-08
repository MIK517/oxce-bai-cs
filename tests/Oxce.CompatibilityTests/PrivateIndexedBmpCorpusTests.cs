using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedBmpCorpusTests
{
    [Fact]
    public void OwnedModIndexedBmpFilesDecodeWithinBounds()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            Directory.Exists(privateMods),
            "Private mod assets are not available in this checkout.");

        var paths = Directory.EnumerateFiles(privateMods, "*.bmp", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var image = IndexedBmpCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.Equal(256, image.Palette.Count);
        }
    }

}
