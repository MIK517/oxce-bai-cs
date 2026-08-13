using System.Text;

namespace Oxce.Rendering;

public sealed class IndexedSpriteFont
{
    private readonly IndexedSpriteFontImage[] _images;
    private readonly Dictionary<int, IndexedGlyph> _glyphs;

    public IndexedSpriteFont(IEnumerable<IndexedSpriteFontImage> images, bool monospace)
    {
        ArgumentNullException.ThrowIfNull(images);
        _images = images.ToArray();
        if (_images.Length == 0)
        {
            throw new ArgumentException("A sprite font requires at least one image.", nameof(images));
        }

        Monospace = monospace;
        _glyphs = new Dictionary<int, IndexedGlyph>();
        for (var imageIndex = 0; imageIndex < _images.Length; imageIndex++)
        {
            AddImageGlyphs(imageIndex, _images[imageIndex]);
        }
    }

    public bool Monospace { get; }

    public int Width => _images[0].CellWidth;

    public int Height => _images[0].CellHeight;

    public int Spacing => _images[0].Spacing;

    public IndexedGlyph GetGlyph(Rune rune)
    {
        if (_glyphs.TryGetValue(rune.Value, out var glyph))
        {
            return glyph;
        }

        return _glyphs.TryGetValue('?', out glyph)
            ? glyph
            : throw new KeyNotFoundException($"Font has no glyph for U+{rune.Value:X4} and no '?' fallback.");
    }

    public IndexedTextSize GetCharacterSize(Rune rune)
    {
        if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
        {
            var width = Monospace
                ? Width + Spacing
                : rune.Value == 0x00a0
                    ? Width / 4
                    : rune.Value == '\t'
                        ? Width * 3 / 4
                        : Width / 2;
            return new IndexedTextSize(width, Height + Spacing);
        }

        var glyph = GetGlyph(rune);
        var image = _images[glyph.ImageIndex];
        return new IndexedTextSize(glyph.Width + image.Spacing, glyph.Height + image.Spacing);
    }

    public IndexedTextSize Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var width = 0;
        var height = Height + Spacing;
        foreach (var rune in text.EnumerateRunes())
        {
            width = checked(width + GetCharacterSize(rune).Width);
        }

        return new IndexedTextSize(width, height);
    }

    public void DrawText(
        IndexedSurface destination,
        string text,
        int x,
        int y,
        int replacementColorGroup,
        int shade = 0)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(text);
        var cursor = x;
        foreach (var rune in text.EnumerateRunes())
        {
            var size = GetCharacterSize(rune);
            if (!Rune.IsControl(rune) && !Rune.IsWhiteSpace(rune))
            {
                var glyph = GetGlyph(rune);
                if (glyph.X >= 0)
                {
                    var source = _images[glyph.ImageIndex].Surface;
                    destination.BlitShaded(
                        source,
                        glyph.X,
                        glyph.Y,
                        glyph.Width,
                        glyph.Height,
                        cursor,
                        y,
                        shade,
                        replacementColorGroup: replacementColorGroup);
                }
            }

            cursor = checked(cursor + size.Width);
        }
    }

    private void AddImageGlyphs(int imageIndex, IndexedSpriteFontImage image)
    {
        var cellsPerRow = image.Surface.Width / image.CellWidth;
        if (cellsPerRow == 0)
        {
            throw new ArgumentException("Font image is narrower than its glyph cell width.", nameof(image));
        }

        var characterIndex = 0;
        foreach (var rune in image.Characters.EnumerateRunes())
        {
            var startX = characterIndex % cellsPerRow * image.CellWidth;
            var startY = characterIndex / cellsPerRow * image.CellHeight;
            if (startY + image.CellHeight > image.Surface.Height)
            {
                throw new ArgumentException("Font image does not contain enough cells for its character map.", nameof(image));
            }

            var left = startX;
            var width = image.CellWidth;
            if (!Monospace)
            {
                var first = -1;
                var last = -1;
                for (var x = startX; x < startX + image.CellWidth; x++)
                {
                    for (var y = startY; y < startY + image.CellHeight; y++)
                    {
                        if (image.Surface.GetPixel(x, y) != 0)
                        {
                            first = first < 0 ? x : first;
                            last = x;
                            break;
                        }
                    }
                }

                left = first;
                width = last - first + 1;
            }

            _glyphs[rune.Value] = new IndexedGlyph(
                imageIndex,
                left,
                startY,
                width,
                image.CellHeight);
            characterIndex++;
        }
    }
}

public sealed record IndexedSpriteFontImage
{
    public IndexedSpriteFontImage(
        IndexedSurface surface,
        int cellWidth,
        int cellHeight,
        int spacing,
        string characters)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);
        ArgumentNullException.ThrowIfNull(characters);
        Surface = surface;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Spacing = spacing;
        Characters = characters;
    }

    public IndexedSurface Surface { get; }

    public int CellWidth { get; }

    public int CellHeight { get; }

    public int Spacing { get; }

    public string Characters { get; }
}

public readonly record struct IndexedGlyph(int ImageIndex, int X, int Y, int Width, int Height);

public readonly record struct IndexedTextSize(int Width, int Height);
