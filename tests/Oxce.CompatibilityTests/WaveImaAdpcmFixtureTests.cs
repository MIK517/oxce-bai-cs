using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class WaveImaAdpcmFixtureTests
{
    [Fact]
    public void ImaAdpcmNormalizationMatchesCapturedSdlReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "wave-ima-adpcm.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            mono = Decode(Path.GetFullPath(manifest.Inputs[0].Path, root)),
            stereo = Decode(Path.GetFullPath(manifest.Inputs[1].Path, root)),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
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
