using System.Buffers.Binary;
using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateWavePcmCorpusTests
{
    [Fact]
    public void SuppliedModPcmWaveFilesDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            Directory.Exists(privateMods),
            "Private mod assets are not available in this checkout.");

        var decodedCount = 0;
        foreach (var path in Directory.EnumerateFiles(privateMods, "*.wav", SearchOption.AllDirectories))
        {
            var input = BinaryDataReader.FromFile(path);
            if (ReadEncoding(input) != 1)
            {
                continue;
            }

            input.Seek(0);
            var audio = WavePcmCodec.Decode(input);
            Assert.Equal(checked(audio.FrameCount * audio.Channels), audio.Samples.Length);
            decodedCount++;
        }

        Assert.True(
            decodedCount >= 2_097,
            $"Expected the supplied PCM WAV corpus; decoded {decodedCount} files.");
    }

    [Fact]
    public void SuppliedModMicrosoftAdpcmWaveFilesDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            Directory.Exists(privateMods),
            "Private mod assets are not available in this checkout.");

        var decodedCount = 0;
        foreach (var path in Directory.EnumerateFiles(privateMods, "*.wav", SearchOption.AllDirectories))
        {
            var input = BinaryDataReader.FromFile(path);
            if (ReadEncoding(input) != 2)
            {
                continue;
            }

            input.Seek(0);
            var audio = WavePcmCodec.Decode(input);
            Assert.Equal(checked(audio.FrameCount * audio.Channels), audio.Samples.Length);
            decodedCount++;
        }

        Assert.True(
            decodedCount >= 203,
            $"Expected the supplied Microsoft ADPCM WAV corpus; decoded {decodedCount} files.");
    }

    private static ushort ReadEncoding(BinaryDataReader input)
    {
        var data = input.ReadMemory(input.Remaining).Span;
        var position = 12;
        while (position <= data.Length - 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            if (data.Slice(position, 4).SequenceEqual("fmt "u8))
            {
                if (size < 2 || size > int.MaxValue || position + 8L + size > data.Length)
                {
                    throw new InvalidDataException($"WAV fmt chunk is malformed at offset {position}.");
                }

                return BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 8)..]);
            }

            position = checked(position + 8 + (int)size + ((int)size & 1));
        }

        throw new InvalidDataException("WAV input does not contain a fmt chunk.");
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
