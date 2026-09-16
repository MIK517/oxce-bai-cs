using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class WaveMicrosoftAdpcmFixtureTests
{
    [Fact]
    public void MicrosoftAdpcmNormalizationMatchesCapturedSdlReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("wave-ms-adpcm");
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            mono = Decode(Path.GetFullPath(manifest.Inputs[0].Path, root)),
            stereo = Decode(Path.GetFullPath(manifest.Inputs[1].Path, root)),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static object Decode(string path)
    {
        var fixture = Convert.FromHexString(File.ReadAllText(path).Trim());
        var audio = WavePcmCodec.Decode(new BinaryDataReader(fixture));
        return new
        {
            channels = audio.Channels,
            frames = audio.FrameCount,
            sampleRate = audio.SampleRate,
            samples = audio.Samples.ToArray(),
        };
    }
}
