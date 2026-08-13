using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class IndexedPngCodecTests
{
    [Fact]
    public void DecodePreservesPaletteIndexesAndNormalizesTransparentIndex()
    {
        var image = IndexedPngCodec.Decode(Reader(FixtureHex));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 0, 1, 0, 3, 0, 1 }, image.Pixels.ToArray());
        Assert.Equal(4, image.Palette.Count);
        Assert.Equal(10, image.Palette[0].Red);
        Assert.Equal(0, image.Palette[2].Alpha);
    }

    [Fact]
    public void DecodeRejectsCrcMismatchAndPixelLimit()
    {
        var corrupt = Convert.FromHexString(FixtureHex);
        corrupt[20] ^= 1;

        Assert.Throws<InvalidDataException>(() => IndexedPngCodec.Decode(new BinaryDataReader(corrupt)));
        Assert.Throws<InvalidDataException>(() => IndexedPngCodec.Decode(Reader(FixtureHex), maxPixels: 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("89504E470D0A1A0A")]
    [InlineData("89504E470D0A1A0A00000000")]
    public void DecodeRejectsIncompletePng(string hex)
    {
        Assert.Throws<InvalidDataException>(() => IndexedPngCodec.Decode(Reader(hex)));
    }

    [Fact]
    public void GeneralDecoderUsesFileSignature()
    {
        var image = IndexedImageCodec.Decode(Reader(FixtureHex));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    private const string FixtureHex =
        "89504E470D0A1A0A0000000D4948445200000003000000020803000000AAAA9628" +
        "0000000C504C54450A141E28323C46505A646E78C64877DF0000000474524E53" +
        "FFFF00FFFE0CBB0B00000013494441547801010800F7FF00000102000302010026" +
        "000A8D712C090000000049454E44AE426082";

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
