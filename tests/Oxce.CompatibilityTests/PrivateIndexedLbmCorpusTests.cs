using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedLbmCorpusTests
{
    [Fact]
    public void OwnedOriginalLbmFilesDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
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
