using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class IndexedGifFixtureTests
{
    [Fact]
    public void FirstFramePaletteIndexesAndTransparencyMatchCapturedCppReference()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "indexed-gif.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
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

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

}
