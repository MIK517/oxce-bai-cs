using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class IndexedGifFixtureTests
{
    [Fact]
    public void FirstFramePaletteIndexesAndTransparencyMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("indexed-gif");
        var fixture = Convert.FromHexString(
            File.ReadAllText(Path.GetFullPath(manifest.Inputs[0].Path, root)).Trim());
        var image = IndexedGifCodec.Decode(new BinaryDataReader(fixture));
        var paletteBytes = image.Palette
            .Take(2)
            .SelectMany(color => new[] { color.Red, color.Green, color.Blue, color.Alpha })
            .ToArray();
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            height = image.Height,
            palette = Convert.ToHexString(paletteBytes),
            pixels = Convert.ToHexString(image.Pixels.Span),
            transparentIndex = image.OriginalTransparentIndex,
            width = image.Width,
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }
}
