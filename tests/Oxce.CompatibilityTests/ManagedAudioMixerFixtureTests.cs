using System.Buffers.Binary;
using System.Text.Json;
using Oxce.Engine.Audio;
using Oxce.FixtureSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ManagedAudioMixerFixtureTests
{
    [Fact]
    public void LoopVolumeAndPanningMatchCapturedSdlMixerReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "managed-mixer.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var encoded = Convert.FromHexString(
            File.ReadAllText(Path.GetFullPath(manifest.Inputs[0].Path, root)).Trim());
        var samples = new short[encoded.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(index * sizeof(short)));
        }

        using var mixer = new ManagedAudioMixer(8_000);
        var clip = new PcmAudioClip(samples, 8_000, 2);
        using var playback = mixer.Play(
            clip,
            new AudioPlaybackOptions(
                AudioBus.Effects,
                LoopCount: 1,
                Gain: 0.5f,
                Pan: -127f / 255f));
        var mixed = new short[24];
        mixer.Mix(mixed);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            channels = mixer.Channels,
            frames = mixed.Length / mixer.Channels,
            sampleRate = mixer.SampleRate,
            samples = mixed,
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
        Assert.False(playback.IsPlaying);
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
