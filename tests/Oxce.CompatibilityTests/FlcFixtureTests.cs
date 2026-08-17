using System.Text.Json;
using Oxce.Core.Graphics;
using Oxce.FixtureSupport;
using Oxce.Formats.Video;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class FlcFixtureTests
{
    [Fact]
    public void IndexedFramesMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(Path.Combine(root, "fixtures", "manifests", "flc-indexed.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var encoded = string.Concat(File.ReadAllText(Path.GetFullPath(manifest.Inputs[0].Path, root))
            .Where(value => !char.IsWhiteSpace(value)));
        using var input = new MemoryStream(Convert.FromHexString(encoded));
        var sink = new CaptureSink();

        var summary = FlcDecoder.Decode(input, sink);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            frames = sink.Frames,
            height = summary.Header.Height,
            width = summary.Header.Width,
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
        Assert.Equal(5, summary.DecodedFrames);
        Assert.Equal(377, summary.BytesRead);
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

    private sealed class CaptureSink : IFlcFrameSink
    {
        public List<object> Frames { get; } = [];

        public void OnFrame(FlcFrameInfo frame, ReadOnlySpan<byte> pixels, ReadOnlySpan<Rgba32> palette)
        {
            Span<byte> selectedPalette = stackalloc byte[12];
            for (var index = 0; index < 4; index++)
            {
                selectedPalette[index * 3] = palette[index].Red;
                selectedPalette[index * 3 + 1] = palette[index].Green;
                selectedPalette[index * 3 + 2] = palette[index].Blue;
            }

            Frames.Add(new
            {
                index = frame.Index,
                palette0to3 = Convert.ToHexString(selectedPalette),
                pixels = Convert.ToHexString(pixels),
            });
        }

        public void OnAudio(FlcAudioInfo audio, ReadOnlySpan<byte> unsignedPcm)
        {
            throw new InvalidOperationException("The public FLI fixture does not contain audio.");
        }
    }
}
