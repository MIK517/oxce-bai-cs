using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Audio;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CatSoundFixtureTests
{
    [Fact]
    public void OriginalCatSoundConversionMatchesCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("cat-sound");
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tftd = Decode(Path.GetFullPath(manifest.Inputs[1].Path, root), XcomSoundVariant.Tftd),
            ufo = Decode(Path.GetFullPath(manifest.Inputs[0].Path, root), XcomSoundVariant.Ufo),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static object Decode(string path, XcomSoundVariant variant)
    {
        var fixture = Convert.FromHexString(File.ReadAllText(path).Trim());
        var audio = CatSoundCodec.DecodeEntry(fixture, variant);
        return new
        {
            channels = audio.Channels,
            frames = audio.FrameCount,
            sampleRate = audio.SampleRate,
            samples = audio.Samples.ToArray(),
        };
    }
}
