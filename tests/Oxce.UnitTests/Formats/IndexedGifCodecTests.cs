using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class IndexedGifCodecTests
{
    [Fact]
    public void DecodeReadsFirstIndexedFrameAndTransparency()
    {
        var image = IndexedGifCodec.Decode(Reader(
            "47494638396101000100800000000000FFFFFF" +
            "21F90401000000002C00000000010001000002024401003B"));

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 0 }, image.Pixels.ToArray());
        Assert.Equal(256, image.Palette.Count);
        Assert.Equal(0, image.Palette[0].Alpha);
        Assert.Equal(0, image.OriginalTransparentIndex);
    }

    [Fact]
    public void DecodeEnforcesPixelLimitAndRejectsTruncation()
    {
        const string gif =
            "47494638396101000100800000000000FFFFFF" +
            "21F90401000000002C00000000010001000002024401003B";

        Assert.Throws<InvalidDataException>(() => IndexedGifCodec.Decode(Reader(gif), maxPixels: 0));
        Assert.Throws<InvalidDataException>(() => IndexedGifCodec.Decode(Reader(gif[..^4])));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
