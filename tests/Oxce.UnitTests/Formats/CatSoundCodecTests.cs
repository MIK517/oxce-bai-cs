using Oxce.Formats.Audio;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class CatSoundCodecTests
{
    private const string UfoRaw = "03524157010203040500011F203F407FFFEE";
    private const string TftdRaw = "025446010203040580C0FF0001407FEE";
    private const string EmbeddedWave =
        "0158524946462700000057415645666D74201000000001000100112B0000112B00000100080064617461030000000080FF";

    [Fact]
    public void DecodeEntryMatchesUfoScalingAndFixedPointResampling()
    {
        var audio = Decode(UfoRaw, XcomSoundVariant.Ufo);

        Assert.Equal(11_025, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
        Assert.Equal(
            new short[]
            {
                -32768, -32768, -31744, -1024, -1024, 0,
                31744, -32768, -32768, 31744, 31744, 31744,
            },
            audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeEntryMatchesTftdSignedSamplesWithoutResampling()
    {
        var audio = Decode(TftdRaw, XcomSoundVariant.Tftd);

        Assert.Equal(11_025, audio.SampleRate);
        Assert.Equal(new short[] { -32768, -16384, -256, 0, 256, 16384, 32512 }, audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeEntryStripsNameAndLoadsEmbeddedWave()
    {
        var audio = Decode(EmbeddedWave, XcomSoundVariant.Ufo);

        Assert.Equal(11_025, audio.SampleRate);
        Assert.Equal(new short[] { -32768, 0, 32512 }, audio.Samples.ToArray());
    }

    [Fact]
    public void DecodeEntryRepairsTruncatedEmbeddedWaveDeclarations()
    {
        var entry = Convert.FromHexString(EmbeddedWave);
        entry[6] = 40;
        entry[42] = 4;

        var audio = CatSoundCodec.DecodeEntry(entry, XcomSoundVariant.Ufo);

        Assert.Equal(3, audio.FrameCount);
    }

    [Fact]
    public void DecodeEntryRejectsMalformedBoundsAndFrameLimit()
    {
        Assert.Throws<InvalidDataException>(
            () => CatSoundCodec.DecodeEntry(Convert.FromHexString("0341"), XcomSoundVariant.Ufo));
        Assert.Throws<InvalidDataException>(
            () => CatSoundCodec.DecodeEntry(Convert.FromHexString("00010203"), XcomSoundVariant.Ufo));
        Assert.Throws<InvalidDataException>(
            () => CatSoundCodec.DecodeEntry(Convert.FromHexString(UfoRaw), XcomSoundVariant.Ufo, maxSampleFrames: 11));
    }

    private static PcmAudioData Decode(string hex, XcomSoundVariant variant) =>
        CatSoundCodec.DecodeEntry(Convert.FromHexString(hex), variant);
}
