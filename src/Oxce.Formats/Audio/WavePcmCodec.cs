using System.Buffers.Binary;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Audio;

public static class WavePcmCodec
{
    public const int DefaultMaximumSampleFrames = 16 * 1024 * 1024;

    public static PcmAudioData Decode(
        BinaryDataReader input,
        int maxSampleFrames = DefaultMaximumSampleFrames)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSampleFrames);
        var data = input.ReadMemory(input.Remaining).Span;
        if (data.Length < 12
            || !data[..4].SequenceEqual("RIFF"u8)
            || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Input does not have a RIFF/WAVE header.");
        }

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (riffSize < 4 || riffSize > int.MaxValue || 8L + riffSize > data.Length)
        {
            throw new InvalidDataException("RIFF/WAVE data is truncated or outside supported bounds.");
        }

        // Several supported mod WAV files under-report the RIFF size while their chunk
        // boundaries remain valid. SDL accepts those files, so physical input bounds
        // are authoritative after rejecting declarations that extend past the input.
        var riffEnd = data.Length;
        WaveFormat? format = null;
        ReadOnlySpan<byte> sampleBytes = default;
        var position = 12;
        while (position < riffEnd)
        {
            if (riffEnd - position < 8)
            {
                throw new InvalidDataException("RIFF/WAVE chunk header is truncated.");
            }

            var chunkId = data.Slice(position, 4);
            var chunkSizeValue = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            if (chunkSizeValue > int.MaxValue)
            {
                throw new InvalidDataException("RIFF/WAVE chunk size is outside supported bounds.");
            }

            var chunkSize = (int)chunkSizeValue;
            var chunkStart = checked(position + 8);
            var chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > riffEnd)
            {
                throw new InvalidDataException("RIFF/WAVE chunk data is truncated.");
            }

            var chunk = data.Slice(chunkStart, chunkSize);
            if (chunkId.SequenceEqual("fmt "u8) && format is null)
            {
                format = ReadFormat(chunk);
            }
            else if (chunkId.SequenceEqual("data"u8) && sampleBytes.IsEmpty)
            {
                sampleBytes = chunk;
            }

            if (format is not null && !sampleBytes.IsEmpty)
            {
                break;
            }

            position = checked(chunkEnd + (chunkSize & 1));
            if (position > riffEnd)
            {
                throw new InvalidDataException("RIFF/WAVE chunk padding is truncated.");
            }
        }

        if (format is null || sampleBytes.IsEmpty)
        {
            throw new InvalidDataException("RIFF/WAVE input requires fmt and non-empty data chunks.");
        }

        return DecodeSamples(format.Value, sampleBytes, maxSampleFrames);
    }

    private static WaveFormat ReadFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
        {
            throw new InvalidDataException("RIFF/WAVE fmt chunk is truncated.");
        }

        var encoding = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (encoding != 1)
        {
            throw new InvalidDataException(
                $"RIFF/WAVE encoding {encoding} is unsupported; only integer PCM is currently supported.");
        }

        var channels = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var sampleRateValue = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var blockAlignment = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (channels is < 1 or > 8 || sampleRateValue is < 1 or > 384_000)
        {
            throw new InvalidDataException("RIFF/WAVE channel count or sample rate is outside supported bounds.");
        }

        if (bitsPerSample is not 8 and not 16)
        {
            throw new InvalidDataException("Only 8-bit and 16-bit integer PCM WAV samples are supported.");
        }

        var expectedAlignment = checked(channels * (bitsPerSample / 8));
        if (blockAlignment != expectedAlignment)
        {
            throw new InvalidDataException("RIFF/WAVE block alignment does not match its PCM format.");
        }

        return new WaveFormat(channels, checked((int)sampleRateValue), bitsPerSample, blockAlignment);
    }

    private static PcmAudioData DecodeSamples(
        WaveFormat format,
        ReadOnlySpan<byte> data,
        int maxSampleFrames)
    {
        if (data.Length % format.BlockAlignment != 0)
        {
            throw new InvalidDataException("RIFF/WAVE data does not contain a whole number of sample frames.");
        }

        var frameCount = data.Length / format.BlockAlignment;
        if (frameCount > maxSampleFrames)
        {
            throw new InvalidDataException(
                $"RIFF/WAVE data contains {frameCount} sample frames, exceeding the {maxSampleFrames}-frame limit.");
        }

        var sampleCount = checked(frameCount * format.Channels);
        var samples = new short[sampleCount];
        if (format.BitsPerSample == 8)
        {
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = unchecked((short)((data[index] - 128) << 8));
            }
        }
        else
        {
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = BinaryPrimitives.ReadInt16LittleEndian(data[(index * 2)..]);
            }
        }

        return new PcmAudioData(samples, format.SampleRate, format.Channels);
    }

    private readonly record struct WaveFormat(
        int Channels,
        int SampleRate,
        int BitsPerSample,
        int BlockAlignment);
}
