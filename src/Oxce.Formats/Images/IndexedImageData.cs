using Oxce.Core.Graphics;

namespace Oxce.Formats.Images;

public sealed class IndexedImageData
{
    internal IndexedImageData(
        int width,
        int height,
        byte[] pixels,
        Rgba32[] palette,
        int originalTransparentIndex)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Palette = palette;
        OriginalTransparentIndex = originalTransparentIndex;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Pixels { get; }

    public IReadOnlyList<Rgba32> Palette { get; }

    public int OriginalTransparentIndex { get; }
}
