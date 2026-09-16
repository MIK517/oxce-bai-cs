using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Audio;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class GmCatFixtureTests
{
    [Fact]
    public void GmStreamConversionMatchesCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("gm-cat");
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var entry = Convert.FromHexString(File.ReadAllText(fixturePath).Trim());
        var midi = GmCatMusicCodec.DecodeEntry(entry);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            length = midi.Length,
            midi = Convert.ToHexString(midi),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }
}
