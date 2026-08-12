using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Rendering;

public sealed class IndexedFrameConverterTests
{
    [Fact]
    public void ConvertsPaletteIndicesToRgbaBytes()
    {
        var colors = new Rgba32[IndexedPalette.ColorCount];
        colors[1] = new Rgba32(10, 20, 30, 40);
        colors[2] = new Rgba32(50, 60, 70, 80);
        var palette = new IndexedPalette(colors);
        var surface = new IndexedSurface(2, 1);
        surface.Pixels[0] = 1;
        surface.Pixels[1] = 2;
        var rgba = new byte[8];

        IndexedFrameConverter.ConvertToRgba32(surface, palette, rgba);

        Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 }, rgba);
    }

    [Fact]
    public void PaletteRequiresAll256EntriesAndConverterRequiresExactBuffer()
    {
        Assert.Throws<ArgumentException>(() => new IndexedPalette(new Rgba32[255]));
        var surface = new IndexedSurface(1, 1);
        var palette = IndexedPalette.CreateGrayscale();
        Assert.Throws<ArgumentException>(() => IndexedFrameConverter.ConvertToRgba32(surface, palette, new byte[3]));
    }
}
