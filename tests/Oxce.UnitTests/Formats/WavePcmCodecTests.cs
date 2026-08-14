using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class WavePcmCodecTests
{
    private const string Unsigned8Bit =
        "524946463600000057415645666D74201000000001000100401F0000401F0000010008006461746105000000004080C0FF004C4953540400000074657374";

    private const string Signed16Bit =
        "524946463200000057415645666D7420120000000100020044AC000010B10200040010000000646174610C0000000080FF7FFFFF00003412CCED";

    [Fact]
    public void DecodeConvertsUnsigned8BitMonoToSigned16Bit()
    {
        var audio = WavePcmCodec.Decode(Reader(Unsigned8Bit));

        Assert.Equal(8_000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
        Assert.Equal(5, audio.FrameCount);
        Assert.Equal(new short[] { -32768, -16384, 0, 16384, 32512 }, audio.Samples.ToArray());
    }

    [Fact]
    public void DecodePreservesSigned16BitStereoSamples()
    {
        var audio = WavePcmCodec.Decode(Reader(Signed16Bit));

        Assert.Equal(44_100, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(3, audio.FrameCount);
        Assert.Equal(new short[] { -32768, 32767, -1, 0, 4660, -4660 }, audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeAcceptsUnderReportedRiffSizeWithValidPhysicalChunks()
    {
        var encoded = Convert.FromHexString(Signed16Bit);
        encoded[4] -= 4;

        var audio = WavePcmCodec.Decode(new BinaryDataReader(encoded));

        Assert.Equal(3, audio.FrameCount);
    }

    [Fact]
    public void DecodeRejectsTruncationUnsupportedEncodingAndFrameLimit()
    {
        var adpcm = Convert.FromHexString(Unsigned8Bit);
        adpcm[20] = 2;

        Assert.Throws<InvalidDataException>(() => WavePcmCodec.Decode(Reader(Unsigned8Bit[..^4])));
        Assert.Throws<InvalidDataException>(() => WavePcmCodec.Decode(new BinaryDataReader(adpcm)));
        Assert.Throws<InvalidDataException>(() => WavePcmCodec.Decode(Reader(Unsigned8Bit), maxSampleFrames: 4));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
