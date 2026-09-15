using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class IndexedLbmFixtureTests
{
    [Fact]
    public void ChunkyAndPlanarImagesMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("indexed-lbm");
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ilbm = Decode(Path.GetFullPath(manifest.Inputs[1].Path, root)),
            pbm = Decode(Path.GetFullPath(manifest.Inputs[0].Path, root)),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static object Decode(string path)
    {
        var fixture = Convert.FromHexString(File.ReadAllText(path).Trim());
        var image = IndexedLbmCodec.Decode(new BinaryDataReader(fixture));
        var paletteBytes = image.Palette
            .Take(4)
            .SelectMany(color => new[] { color.Red, color.Green, color.Blue, color.Alpha })
            .ToArray();
        return new
        {
            height = image.Height,
            palette = Convert.ToHexString(paletteBytes),
            pixels = Convert.ToHexString(image.Pixels.Span),
            transparentIndex = image.OriginalTransparentIndex,
            width = image.Width,
        };
    }
}
