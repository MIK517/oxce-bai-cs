using System.Text;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Rendering;

public sealed class IndexedSpriteFontTests
{
    [Fact]
    public void VariableWidthGlyphsTrimTransparentColumnsAndFallbackToQuestionMark()
    {
        var surface = new IndexedSurface(8, 4);
        surface.FillRectangle(1, 0, 2, 4, 1);
        surface.FillRectangle(5, 0, 1, 4, 1);
        var font = new IndexedSpriteFont(
            [new IndexedSpriteFontImage(surface, 4, 4, 1, "A?")],
            monospace: false);

        Assert.Equal(new IndexedGlyph(0, 1, 0, 2, 4), font.GetGlyph(new Rune('A')));
        Assert.Equal(font.GetGlyph(new Rune('?')), font.GetGlyph(new Rune('Z')));
        Assert.Equal(new IndexedTextSize(5, 5), font.Measure("AZ"));
    }

    [Fact]
    public void DrawTextUsesShadeGroupAndWhitespaceAdvance()
    {
        var surface = new IndexedSurface(2, 2);
        surface.FillRectangle(0, 0, 1, 2, 1);
        var font = new IndexedSpriteFont(
            [new IndexedSpriteFontImage(surface, 2, 2, 0, "A")],
            monospace: true);
        var destination = new IndexedSurface(6, 2);

        font.DrawText(destination, "A A", 0, 0, replacementColorGroup: 3);

        Assert.Equal(0x31, destination.GetPixel(0, 0));
        Assert.Equal(0, destination.GetPixel(2, 0));
        Assert.Equal(0x31, destination.GetPixel(4, 0));
    }
}
