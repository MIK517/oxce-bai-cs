using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedGifCorpusTests
{
    [Fact]
    public void OwnedModGifFilesDecodeWithinBounds()
    {
        var privateMods = TestFixtures.RequirePrivateMods();

        var paths = TestFixtures.CorpusFiles("*.gif", privateMods);
        var header = new byte[6];
        var invalidSignatures = new List<string>();
        var decodedCount = 0;
        TestFixtures.ForEachCorpusFile(paths, path =>
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                {
                    invalidSignatures.Add(RelativePath(privateMods, path));
                    return;
                }
                if (!header.SequenceEqual("GIF87a"u8) && !header.SequenceEqual("GIF89a"u8))
                {
                    invalidSignatures.Add(RelativePath(privateMods, path));
                    return;
                }
            }

            var image = IndexedGifCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.InRange(image.Palette.Count, 1, 256);
            decodedCount++;
        });

        Assert.Equal(["40k/Resources/CODEX/map.gif"], invalidSignatures);
        Assert.True(decodedCount > 1_500, $"Expected the supplied decodable GIF corpus; decoded {decodedCount} files.");
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
