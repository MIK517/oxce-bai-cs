using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Audio;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CatSoundFixtureTests
{
    [Fact]
    public void OriginalCatSoundConversionMatchesCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "cat-sound.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tftd = Decode(Path.GetFullPath(manifest.Inputs[1].Path, root), XcomSoundVariant.Tftd),
            ufo = Decode(Path.GetFullPath(manifest.Inputs[0].Path, root), XcomSoundVariant.Ufo),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
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
