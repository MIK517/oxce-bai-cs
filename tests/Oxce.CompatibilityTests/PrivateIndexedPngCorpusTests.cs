using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedPngCorpusTests
{
    [Fact]
    public void OwnedModIndexedPngFilesDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            Directory.Exists(privateMods),
            "Private mod assets are not available in this checkout.");

        var decodedCount = 0;
        var header = new byte[26];
        foreach (var path in Directory.EnumerateFiles(privateMods, "*.png", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(path);
            stream.ReadExactly(header);
            if (header[25] != 3)
            {
                continue;
            }

            var image = IndexedPngCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.InRange(image.Palette.Count, 1, 256);
            decodedCount++;
        }

        Assert.True(decodedCount > 14_000, $"Expected the supplied indexed PNG corpus; decoded {decodedCount} files.");
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
