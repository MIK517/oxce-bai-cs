using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateIndexedPngCorpusTests
{
    private const byte IndexedColorType = 3;

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void OwnedModIndexedPngFilesDecodeWithinBounds()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            Directory.Exists(privateMods),
            "Private mod assets are not available in this checkout.");

        var decodedCount = 0;
        var header = new byte[26];
        foreach (var path in Directory.EnumerateFiles(privateMods, "*.png", SearchOption.AllDirectories))
        {
            using (var stream = File.OpenRead(path))
            {
                // Signature, IHDR length/type, width, height and bit depth precede the color type at offset 25.
                var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
                if (read < header.Length || !header.AsSpan(0, 8).SequenceEqual(PngSignature) ||
                    !header.AsSpan(12, 4).SequenceEqual("IHDR"u8) || header[25] != IndexedColorType)
                {
                    continue;
                }
            }

            var image = IndexedPngCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.Equal(checked(image.Width * image.Height), image.Pixels.Length);
            Assert.InRange(image.Palette.Count, 1, 256);
            decodedCount++;
        }

        Assert.True(decodedCount > 14_000, $"Expected the supplied indexed PNG corpus; decoded {decodedCount} files.");
    }
}
