using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedGifCorpusTests
{
    [Fact]
    public void OwnedModGifFilesDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            Directory.Exists(privateMods),
            "Private mod assets are not available in this checkout.");

        var paths = Directory.EnumerateFiles(privateMods, "*.gif", SearchOption.AllDirectories).ToArray();
        Assert.True(paths.Length > 1_500, $"Expected the supplied GIF corpus; found {paths.Length} files.");
        var header = new byte[6];
        foreach (var path in paths)
        {
            using (var stream = File.OpenRead(path))
            {
                stream.ReadExactly(header);
                if (!header.SequenceEqual("GIF87a"u8) && !header.SequenceEqual("GIF89a"u8))
                {
                    continue;
                }
            }

            var image = IndexedGifCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.InRange(image.Palette.Count, 1, 256);
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
