using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class WaveMicrosoftAdpcmCodecTests
{
    private const string Mono =
        "524946466600000057415645666D74203200000002000100401F0000001900001000040020001400070000010000000200FF00000000C0004000F0000000CC0130FF880118FF6461746120000000001000E8038403108F27D3456AB011FF01200018FC7CFCF18072D3C4A5B6E7F0";

    private const string Stereo =
        "524946465800000057415645666D74203200000002000200112B0000338100001200040020000600070000010000000200FF00000000C0004000F0000000CC0130FF880118FF646174611200000000011000200064009CFF5000B0FF1F807E42";

    [Fact]
    public void DecodeMatchesMicrosoftAdpcmMonoPredictionAndAdaptation()
    {
        var audio = WavePcmCodec.Decode(Reader(Mono));

        Assert.Equal(8_000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
        Assert.Equal(40, audio.FrameCount);
        Assert.Equal(
            new short[]
            {
                900, 1000, 1016, 1016, 888, 840, 926, 1192, 919, 1162,
                1450, 1880, 2702, 1058, -1682, -1682, -896, -190, -824, -1393,
                -900, -1000, -1132, -1236, -1540, -1844, -1679, -1194, -1138, -698,
                -718, -190, -646, 538, -898, 2688, 2926, 13685, 20840, 27995,
            },
            audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeMatchesMicrosoftAdpcmStereoNibbleInterleaving()
    {
        var audio = WavePcmCodec.Decode(Reader(Stereo));

        Assert.Equal(11_025, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(6, audio.FrameCount);
        Assert.Equal(
            new short[] { 80, -80, 100, -100, 116, -152, -12, -204, 324, -306, 784, -364 },
            audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeRejectsMalformedMicrosoftAdpcmAndFrameLimit()
    {
        var invalidPredictor = Convert.FromHexString(Mono);
        invalidPredictor[78] = 7;
        var invalidCoefficients = Convert.FromHexString(Mono);
        invalidCoefficients[40] = 0;
        invalidCoefficients[41] = 0;
        var partialBlock = Convert.FromHexString(Mono)[..^1];
        partialBlock[4] = 101;
        partialBlock[74] = 31;

        Assert.Throws<InvalidDataException>(
            () => WavePcmCodec.Decode(new BinaryDataReader(invalidPredictor)));
        Assert.Throws<InvalidDataException>(
            () => WavePcmCodec.Decode(new BinaryDataReader(invalidCoefficients)));
        Assert.Throws<InvalidDataException>(
            () => WavePcmCodec.Decode(new BinaryDataReader(partialBlock)));
        Assert.Throws<InvalidDataException>(() => WavePcmCodec.Decode(Reader(Mono), maxSampleFrames: 39));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
