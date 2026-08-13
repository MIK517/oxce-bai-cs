using Oxce.Core.Graphics;
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

    [Fact]
    public void PaletteColorBlockReplacementIsBoundedAndDoesNotMutateSource()
    {
        var palette = IndexedPalette.CreateGrayscale();
        Rgba32[] replacement = [new(1, 2, 3), new(4, 5, 6)];

        var updated = palette.WithColors(224, replacement);

        Assert.Equal(new Rgba32(224, 224, 224), palette[224]);
        Assert.Equal(replacement[0], updated[224]);
        Assert.Equal(replacement[1], updated[225]);
        Assert.Throws<ArgumentException>(() => palette.WithColors(255, replacement));
    }
}
