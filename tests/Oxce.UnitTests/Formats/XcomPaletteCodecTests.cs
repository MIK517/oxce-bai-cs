using Oxce.Core.Graphics;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class XcomPaletteCodecTests
{
    [Fact]
    public void DecodeScalesRgb6AndMakesOnlyFirstDecodedColorTransparent()
    {
        var reader = Reader("0000000102033F201040FF7F");

        var colors = XcomPaletteCodec.Decode(reader, colorCount: 5);

        Assert.Equal(new Rgba32(0, 0, 0, 0), colors[0]);
        Assert.Equal(new Rgba32(4, 8, 12), colors[1]);
        Assert.Equal(new Rgba32(252, 128, 64), colors[2]);
        Assert.Equal(new Rgba32(0, 252, 252), colors[3]);
        Assert.Equal(default, colors[4]);
    }

    [Fact]
    public void DecodeUsesPaletteOffsetsAndLeavesMissingBlocksZeroInitialized()
    {
        var data = new byte[XcomPaletteCodec.GetPaletteOffset(1) + 3];
        data[XcomPaletteCodec.GetPaletteOffset(1)] = 2;
        data[XcomPaletteCodec.GetPaletteOffset(1) + 1] = 3;
        data[XcomPaletteCodec.GetPaletteOffset(1) + 2] = 4;

        var present = XcomPaletteCodec.Decode(
            new BinaryDataReader(data),
            colorCount: 2,
            XcomPaletteCodec.GetPaletteOffset(1));
        var missing = XcomPaletteCodec.Decode(
            new BinaryDataReader(data),
            colorCount: 2,
            XcomPaletteCodec.GetPaletteOffset(2));

        Assert.Equal(new Rgba32(8, 12, 16, 0), present[0]);
        Assert.Equal(default, present[1]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), missing[0]);
        Assert.Equal(default, missing[1]);
    }

    [Fact]
    public void PaletteAndColorBlockOffsetsMatchReferenceLayout()
    {
        Assert.Equal(0, XcomPaletteCodec.GetPaletteOffset(0));
        Assert.Equal(774, XcomPaletteCodec.GetPaletteOffset(1));
        Assert.Equal(3096, XcomPaletteCodec.GetPaletteOffset(4));
        Assert.Equal(224, XcomPaletteCodec.GetColorBlockOffset(14));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void DecodeRejectsInvalidRanges(int colorCount, int offset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => XcomPaletteCodec.Decode(Reader(string.Empty), colorCount, offset));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
