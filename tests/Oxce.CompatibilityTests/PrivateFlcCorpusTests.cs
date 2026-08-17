using Oxce.Core.Graphics;
using Oxce.Formats.Video;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateFlcCorpusTests
{
    [Fact]
    public void OwnedUfoTftdAndModVideosDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            Path.Combine(root, "data", "UFO", "UFOINTRO", "UFOINT.FLI"),
            Path.Combine(root, "data", "TFTD", "ANIMS", "GAMEOVER.VID"),
            Path.Combine(root, "data", "TFTD", "ANIMS", "LOGO.VID"),
            Path.Combine(root, "data", "TFTD", "ANIMS", "OUTRO.VID"),
            Path.Combine(root, "data", "TFTD", "ANIMS", "RISE.VID"),
            Path.Combine(root, "fixtures", "private", "mods", "rosigma", "Resources", "ANIMS", "lose.flc"),
        };
        Assert.SkipUnless(paths.All(File.Exists), "Owned UFO, TFTD, and Rosigma video assets are not available in this checkout.");

        foreach (var path in paths)
        {
            using var input = File.OpenRead(path);
            var sink = new CountingSink();
            var summary = FlcDecoder.Decode(input, sink);

            Assert.True(summary.DecodedFrames > 0, path);
            Assert.Equal(summary.DecodedFrames, sink.Frames);
            Assert.Equal(summary.AudioChunks, sink.AudioChunks);
            Assert.Equal(input.Length, summary.BytesRead);
        }
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

    private sealed class CountingSink : IFlcFrameSink
    {
        public int Frames { get; private set; }

        public int AudioChunks { get; private set; }

        public void OnFrame(FlcFrameInfo frame, ReadOnlySpan<byte> pixels, ReadOnlySpan<Rgba32> palette)
        {
            Assert.Equal(++Frames, frame.Index);
            Assert.False(pixels.IsEmpty);
            Assert.Equal(256, palette.Length);
        }

        public void OnAudio(FlcAudioInfo audio, ReadOnlySpan<byte> unsignedPcm)
        {
            Assert.Equal(++AudioChunks, audio.Index);
            Assert.True(audio.SampleRate > 0);
            Assert.False(unsignedPcm.IsEmpty);
        }
    }
}
