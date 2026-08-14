using System.Buffers.Binary;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Audio;

public static class WavePcmCodec
{
    private const ushort PcmEncoding = 1;
    private const ushort MicrosoftAdpcmEncoding = 2;

    private static readonly int[] AdpcmAdaptationTable =
    [
        230, 230, 230, 230, 307, 409, 512, 614,
        768, 614, 512, 409, 307, 230, 230, 230,
    ];

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

        return format.Encoding switch
        {
            PcmEncoding => DecodePcm(format, sampleBytes, maxSampleFrames),
            MicrosoftAdpcmEncoding => DecodeMicrosoftAdpcm(format, sampleBytes, maxSampleFrames),
            _ => throw new InvalidDataException($"RIFF/WAVE encoding {format.Encoding} is unsupported."),
        };
    }

    private static WaveFormat ReadFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
        {
            throw new InvalidDataException("RIFF/WAVE fmt chunk is truncated.");
        }

        var encoding = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var sampleRateValue = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var blockAlignment = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (channels is < 1 or > 8 || sampleRateValue is < 1 or > 384_000)
        {
            throw new InvalidDataException("RIFF/WAVE channel count or sample rate is outside supported bounds.");
        }

        return encoding switch
        {
            PcmEncoding => ReadPcmFormat(channels, sampleRateValue, blockAlignment, bitsPerSample),
            MicrosoftAdpcmEncoding => ReadMicrosoftAdpcmFormat(
                data,
                channels,
                sampleRateValue,
                blockAlignment,
                bitsPerSample),
            _ => throw new InvalidDataException($"RIFF/WAVE encoding {encoding} is unsupported."),
        };
    }

    private static WaveFormat ReadPcmFormat(
        ushort channels,
        uint sampleRateValue,
        ushort blockAlignment,
        ushort bitsPerSample)
    {
        if (bitsPerSample is not 8 and not 16)
        {
            throw new InvalidDataException("Only 8-bit and 16-bit integer PCM WAV samples are supported.");
        }

        var expectedAlignment = checked(channels * (bitsPerSample / 8));
        if (blockAlignment != expectedAlignment)
        {
            throw new InvalidDataException("RIFF/WAVE block alignment does not match its PCM format.");
        }

        return new WaveFormat(
            PcmEncoding,
            channels,
            checked((int)sampleRateValue),
            bitsPerSample,
            blockAlignment,
            0,
            []);
    }

    private static WaveFormat ReadMicrosoftAdpcmFormat(
        ReadOnlySpan<byte> data,
        ushort channels,
        uint sampleRateValue,
        ushort blockAlignment,
        ushort bitsPerSample)
    {
        if (channels is not 1 and not 2 || bitsPerSample != 4)
        {
            throw new InvalidDataException("Microsoft ADPCM WAV data requires one or two channels and 4-bit samples.");
        }

        if (data.Length < 22)
        {
            throw new InvalidDataException("Microsoft ADPCM WAV format extension is truncated.");
        }

        var extraSize = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]);
        var samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
        var coefficientCount = BinaryPrimitives.ReadUInt16LittleEndian(data[20..]);
        var requiredExtraSize = checked(4 + coefficientCount * 4);
        if (coefficientCount is < 1 or > 256
            || extraSize < requiredExtraSize
            || data.Length < 18 + extraSize)
        {
            throw new InvalidDataException("Microsoft ADPCM WAV coefficient table is malformed or truncated.");
        }

        var headerSize = checked(7 * channels);
        if (blockAlignment < headerSize)
        {
            throw new InvalidDataException("Microsoft ADPCM WAV block alignment is smaller than its block header.");
        }

        var calculatedSamplesPerBlock = checked(2 + ((blockAlignment - headerSize) * 2 / channels));
        if (samplesPerBlock != calculatedSamplesPerBlock)
        {
            throw new InvalidDataException("Microsoft ADPCM WAV samples-per-block does not match its block alignment.");
        }

        var coefficients = new AdpcmCoefficient[coefficientCount];
        var coefficientData = data[22..];
        for (var index = 0; index < coefficients.Length; index++)
        {
            coefficients[index] = new AdpcmCoefficient(
                BinaryPrimitives.ReadInt16LittleEndian(coefficientData[(index * 4)..]),
                BinaryPrimitives.ReadInt16LittleEndian(coefficientData[(index * 4 + 2)..]));
        }

        return new WaveFormat(
            MicrosoftAdpcmEncoding,
            channels,
            checked((int)sampleRateValue),
            bitsPerSample,
            blockAlignment,
            samplesPerBlock,
            coefficients);
    }

    private static PcmAudioData DecodePcm(
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

    private static PcmAudioData DecodeMicrosoftAdpcm(
        WaveFormat format,
        ReadOnlySpan<byte> data,
        int maxSampleFrames)
    {
        if (data.Length % format.BlockAlignment != 0)
        {
            throw new InvalidDataException("Microsoft ADPCM WAV data ends with a partial block.");
        }

        var blockCount = data.Length / format.BlockAlignment;
        var frameCount = checked(blockCount * format.SamplesPerBlock);
        if (frameCount > maxSampleFrames)
        {
            throw new InvalidDataException(
                $"RIFF/WAVE data contains {frameCount} sample frames, exceeding the {maxSampleFrames}-frame limit.");
        }

        var samples = new short[checked(frameCount * format.Channels)];
        var outputOffset = 0;
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var block = data.Slice(blockIndex * format.BlockAlignment, format.BlockAlignment);
            outputOffset += DecodeMicrosoftAdpcmBlock(format, block, samples.AsSpan(outputOffset));
        }

        return new PcmAudioData(samples, format.SampleRate, format.Channels);
    }

    private static int DecodeMicrosoftAdpcmBlock(
        WaveFormat format,
        ReadOnlySpan<byte> block,
        Span<short> output)
    {
        Span<AdpcmState> states = stackalloc AdpcmState[format.Channels];
        var position = 0;
        for (var channel = 0; channel < states.Length; channel++)
        {
            var predictor = block[position++];
            if (predictor >= format.Coefficients.Length)
            {
                throw new InvalidDataException("Microsoft ADPCM WAV block uses an undefined predictor.");
            }

            states[channel].Coefficient = format.Coefficients[predictor];
        }

        for (var channel = 0; channel < states.Length; channel++)
        {
            states[channel].Delta = Math.Max(
                16,
                (int)BinaryPrimitives.ReadUInt16LittleEndian(block[position..]));
            position += 2;
        }

        for (var channel = 0; channel < states.Length; channel++)
        {
            states[channel].Sample1 = BinaryPrimitives.ReadInt16LittleEndian(block[position..]);
            position += 2;
        }

        for (var channel = 0; channel < states.Length; channel++)
        {
            states[channel].Sample2 = BinaryPrimitives.ReadInt16LittleEndian(block[position..]);
            position += 2;
        }

        var outputPosition = 0;
        for (var channel = 0; channel < states.Length; channel++)
        {
            output[outputPosition++] = states[channel].Sample2;
        }

        for (var channel = 0; channel < states.Length; channel++)
        {
            output[outputPosition++] = states[channel].Sample1;
        }

        if (format.Channels == 1)
        {
            var framesRemaining = format.SamplesPerBlock - 2;
            while (framesRemaining > 0)
            {
                var encoded = block[position++];
                output[outputPosition++] = DecodeNibble(ref states[0], encoded >> 4);
                framesRemaining--;
                if (framesRemaining > 0)
                {
                    output[outputPosition++] = DecodeNibble(ref states[0], encoded & 0x0F);
                    framesRemaining--;
                }
            }
        }
        else
        {
            for (var frame = 2; frame < format.SamplesPerBlock; frame++)
            {
                var encoded = block[position++];
                output[outputPosition++] = DecodeNibble(ref states[0], encoded >> 4);
                output[outputPosition++] = DecodeNibble(ref states[1], encoded & 0x0F);
            }
        }

        return outputPosition;
    }

    private static short DecodeNibble(ref AdpcmState state, int nibble)
    {
        var signedNibble = nibble < 8 ? nibble : nibble - 16;
        var prediction = ((long)state.Sample1 * state.Coefficient.First
            + (long)state.Sample2 * state.Coefficient.Second) / 256;
        var decoded = (int)Math.Clamp(prediction + (long)signedNibble * state.Delta, short.MinValue, short.MaxValue);
        state.Sample2 = state.Sample1;
        state.Sample1 = (short)decoded;
        state.Delta = Math.Max(16, AdpcmAdaptationTable[nibble] * state.Delta / 256);
        return (short)decoded;
    }

    private sealed record WaveFormat(
        ushort Encoding,
        int Channels,
        int SampleRate,
        int BitsPerSample,
        int BlockAlignment,
        int SamplesPerBlock,
        AdpcmCoefficient[] Coefficients);

    private readonly record struct AdpcmCoefficient(short First, short Second);

    private struct AdpcmState
    {
        internal AdpcmCoefficient Coefficient;
        internal int Delta;
        internal short Sample1;
        internal short Sample2;
    }
}
