using System.Buffers.Binary;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Audio;

public enum XcomSoundVariant
{
    Ufo,
    Tftd,
}

public static class CatSoundCodec
{
    private const int OutputSampleRate = 11_025;
    private const int UfoInputSampleRate = 8_000;
    private const int FixedWaveHeaderSize = 44;

    public static PcmAudioData DecodeEntry(
        ReadOnlySpan<byte> entryData,
        XcomSoundVariant variant,
        int maxSampleFrames = WavePcmCodec.DefaultMaximumSampleFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSampleFrames);
        if (!Enum.IsDefined(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        if (entryData.IsEmpty)
        {
            throw new InvalidDataException("CAT sound entry is empty.");
        }

        var soundOffset = checked(1 + entryData[0]);
        if (soundOffset > entryData.Length)
        {
            throw new InvalidDataException("CAT sound entry name is truncated.");
        }

        var sound = entryData[soundOffset..];
        if (sound.Length < 12)
        {
            throw new InvalidDataException("CAT sound entry is too short to contain supported sound data.");
        }

        return sound[..4].SequenceEqual("RIFF"u8)
            && sound.Slice(8, 4).SequenceEqual("WAVE"u8)
            ? DecodeEmbeddedWave(sound, variant, maxSampleFrames)
            : DecodeDosSound(sound, variant, maxSampleFrames);
    }

    private static PcmAudioData DecodeEmbeddedWave(
        ReadOnlySpan<byte> sound,
        XcomSoundVariant variant,
        int maxSampleFrames)
    {
        if (sound.Length < FixedWaveHeaderSize)
        {
            throw new InvalidDataException("Embedded CAT WAV data is shorter than its fixed header.");
        }

        byte[]? corrected = null;
        var declaredRiffSize = BinaryPrimitives.ReadUInt32LittleEndian(sound[4..]);
        var declaredLength = 8L + declaredRiffSize;
        if (declaredLength > sound.Length)
        {
            var missingBytes = declaredLength - sound.Length;
            var declaredDataSize = BinaryPrimitives.ReadUInt32LittleEndian(sound[40..]);
            if (missingBytes > declaredDataSize)
            {
                throw new InvalidDataException("Embedded CAT WAV size declarations cannot be repaired safely.");
            }

            corrected = sound.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(corrected.AsSpan(4), checked((uint)(sound.Length - 8)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                corrected.AsSpan(40),
                checked((uint)(declaredDataSize - missingBytes)));
            sound = corrected;
        }

        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(sound[24..]);
        if (sampleRate < OutputSampleRate)
        {
            return DecodeMonoBytes(
                sound[FixedWaveHeaderSize..],
                SampleEncoding.Unsigned8,
                resample: variant == XcomSoundVariant.Ufo,
                maxSampleFrames);
        }

        return WavePcmCodec.Decode(new BinaryDataReader(sound.ToArray()), maxSampleFrames);
    }

    private static PcmAudioData DecodeDosSound(
        ReadOnlySpan<byte> sound,
        XcomSoundVariant variant,
        int maxSampleFrames)
    {
        // The reference skips five DOS header bytes and intentionally excludes
        // the entry's final byte from the submitted sample range.
        var samples = sound.Slice(5, sound.Length - 6);
        return variant == XcomSoundVariant.Tftd
            ? DecodeMonoBytes(samples, SampleEncoding.Signed8, resample: false, maxSampleFrames)
            : DecodeMonoBytes(samples, SampleEncoding.UfoSixBit, resample: true, maxSampleFrames);
    }

    private static PcmAudioData DecodeMonoBytes(
        ReadOnlySpan<byte> samples,
        SampleEncoding encoding,
        bool resample,
        int maxSampleFrames)
    {
        const int fixedPointUnit = 1 << 16;
        var step = (UfoInputSampleRate * fixedPointUnit) / OutputSampleRate;
        var frameCount = resample
            ? checked((int)(((long)samples.Length * fixedPointUnit + step - 1) / step))
            : samples.Length;
        if (frameCount > maxSampleFrames)
        {
            throw new InvalidDataException(
                $"CAT sound contains {frameCount} sample frames, exceeding the {maxSampleFrames}-frame limit.");
        }

        var decoded = new short[frameCount];
        for (var frame = 0; frame < decoded.Length; frame++)
        {
            var sourceIndex = resample ? checked((int)((long)frame * step >> 16)) : frame;
            var source = samples[sourceIndex];
            decoded[frame] = encoding switch
            {
                SampleEncoding.Unsigned8 => unchecked((short)((source - 128) << 8)),
                SampleEncoding.Signed8 => unchecked((short)(unchecked((sbyte)source) << 8)),
                SampleEncoding.UfoSixBit => unchecked((short)((unchecked((byte)(source * 4)) - 128) << 8)),
                _ => throw new InvalidOperationException("Unknown CAT sound sample encoding."),
            };
        }

        return new PcmAudioData(decoded, OutputSampleRate, channels: 1);
    }

    private enum SampleEncoding
    {
        Unsigned8,
        Signed8,
        UfoSixBit,
    }
}
