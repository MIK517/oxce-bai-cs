using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class IndexedBmpCodecTests
{
    [Fact]
    public void DecodeReadsBottomUpRowsAndBgrPalette()
    {
        var image = IndexedBmpCodec.Decode(Reader(
            "424D46000000000000003E0000002800000002000000020000000100080000000000" +
            "08000000000000000000000002000000000000001E140A003C3228000100000000010000"));

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 0, 1, 1, 0 }, image.Pixels.ToArray());
        Assert.Equal(10, image.Palette[0].Red);
        Assert.Equal(40, image.Palette[1].Red);
        Assert.Equal(0, image.Palette[0].Alpha);
    }

    [Fact]
    public void DecodeRejectsTruncationAndPixelLimit()
    {
        const string bmp =
            "424D46000000000000003E0000002800000002000000020000000100080000000000" +
            "08000000000000000000000002000000000000001E140A003C3228000100000000010000";

        Assert.Throws<InvalidDataException>(() => IndexedBmpCodec.Decode(Reader(bmp[..^4])));
        Assert.Throws<InvalidDataException>(() => IndexedBmpCodec.Decode(Reader(bmp), maxPixels: 3));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
