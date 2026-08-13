using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Rendering;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class SpriteFontFixtureTests
{
    [Fact]
    public void GlyphBoundsFallbackAndWhitespaceMetricsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "sprite-font.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
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

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    private static int[] ToArray(IndexedTextSize size) => [size.Width, size.Height];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
