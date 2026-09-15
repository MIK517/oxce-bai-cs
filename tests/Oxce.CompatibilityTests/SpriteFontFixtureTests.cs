using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Rendering;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class SpriteFontFixtureTests
{
    [Fact]
    public void GlyphBoundsFallbackAndWhitespaceMetricsMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("sprite-font");
        var pixels = Convert.FromHexString(
            File.ReadAllText(Path.GetFullPath(manifest.Inputs[0].Path, root)).Trim());
        var surface = new IndexedSurface(8, 4);
        pixels.CopyTo(surface.Pixels);
        var font = new IndexedSpriteFont(
            [new IndexedSpriteFontImage(surface, 4, 4, 1, "A?")],
            monospace: false);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            glyphs = new[] { font.GetGlyph(new Rune('A')), font.GetGlyph(new Rune('?')) }
                .Select(glyph => new { height = glyph.Height, width = glyph.Width, x = glyph.X, y = glyph.Y }),
            sizes = new
            {
                A = ToArray(font.GetCharacterSize(new Rune('A'))),
                nbsp = ToArray(font.GetCharacterSize(new Rune(0x00a0))),
                space = ToArray(font.GetCharacterSize(new Rune(' '))),
                tab = ToArray(font.GetCharacterSize(new Rune('\t'))),
                unknown = ToArray(font.GetCharacterSize(new Rune('Z'))),
            },
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static int[] ToArray(IndexedTextSize size) => [size.Width, size.Height];
}
