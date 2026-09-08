using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Audio;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class GmCatFixtureTests
{
    [Fact]
    public void GmStreamConversionMatchesCapturedCppReference()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "gm-cat.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var entry = Convert.FromHexString(File.ReadAllText(fixturePath).Trim());
        var midi = GmCatMusicCodec.DecodeEntry(entry);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            length = midi.Length,
            midi = Convert.ToHexString(midi),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

}
