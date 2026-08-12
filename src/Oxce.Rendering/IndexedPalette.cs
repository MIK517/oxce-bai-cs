namespace Oxce.Rendering;

public sealed class IndexedPalette
{
    public const int ColorCount = 256;

    private readonly Rgba32[] _colors;

    public IndexedPalette(ReadOnlySpan<Rgba32> colors)
    {
        if (colors.Length != ColorCount)
        {
            throw new ArgumentException($"An indexed palette must contain exactly {ColorCount} colors.", nameof(colors));
        }

        _colors = colors.ToArray();
    }

    public Rgba32 this[byte index] => _colors[index];

    public static IndexedPalette CreateGrayscale()
    {
        var colors = new Rgba32[ColorCount];
        for (var index = 0; index < colors.Length; ++index)
        {
            var value = (byte)index;
            colors[index] = new Rgba32(value, value, value);
        }

        return new IndexedPalette(colors);
    }
}
