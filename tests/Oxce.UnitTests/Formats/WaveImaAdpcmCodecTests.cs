using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class WaveImaAdpcmCodecTests
{
    private const string Mono =
        "524946463800000057415645666D74201400000011000100401F0000C71B000008000400020009006461746110000000E8030A00108F27D318FC1E00F18072D3";

    private const string Stereo =
        "524946463800000057415645666D74201400000011000200112B0000904C000010000400020009006461746110000000640005009CFF1400108F27D3F18072D3";

    [Fact]
    public void DecodeMatchesImaAdpcmMonoStepsAndLowNibbleOrdering()
    {
        var audio = WavePcmCodec.Decode(Reader(Mono));

        Assert.Equal(8_000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
        Assert.Equal(18, audio.FrameCount);
        Assert.Equal(
            new short[]
            {
                1000, 1002, 1008, 978, 974, 1030, 1071, 1123, 1049,
                -1000, -952, -1172, -1141, -1169, -1039, -684, -327, -836,
            },
            audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeMatchesImaAdpcmStereoChannelGroups()
    {
        var audio = WavePcmCodec.Decode(Reader(Stereo));

        Assert.Equal(11_025, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(9, audio.FrameCount);
        Assert.Equal(
            new short[]
            {
                100, -100, 101, -82, 104, -165, 86, -153, 84,
                -164, 118, -114, 143, 22, 174, 158, 128, -37,
            },
            audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeRejectsMalformedImaAdpcmAndFrameLimit()
    {
        var invalidStepIndex = Convert.FromHexString(Mono);
        invalidStepIndex[50] = 89;
        var invalidSamplesPerBlock = Convert.FromHexString(Mono);
        invalidSamplesPerBlock[38] = 8;
        var partialBlock = Convert.FromHexString(Mono)[..^1];
        partialBlock[4] = 55;
        partialBlock[44] = 15;

        Assert.Throws<InvalidDataException>(
            () => WavePcmCodec.Decode(new BinaryDataReader(invalidStepIndex)));
        Assert.Throws<InvalidDataException>(
            () => WavePcmCodec.Decode(new BinaryDataReader(invalidSamplesPerBlock)));
        Assert.Throws<InvalidDataException>(
            () => WavePcmCodec.Decode(new BinaryDataReader(partialBlock)));
        Assert.Throws<InvalidDataException>(() => WavePcmCodec.Decode(Reader(Mono), maxSampleFrames: 17));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
