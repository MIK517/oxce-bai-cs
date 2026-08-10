namespace Oxce.Rendering;

/// <summary>
/// Managed representation of the engine's canonical 8-bit indexed surface.
/// Platform backends convert these indices and a 256-color palette for display.
/// </summary>
public sealed class IndexedSurface
{
    private readonly byte[] _pixels;

    public IndexedSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = GC.AllocateUninitializedArray<byte>(checked(width * height));
    }

    public int Width { get; }

    public int Height { get; }

    public Span<byte> Pixels => _pixels;

    public Span<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return _pixels.AsSpan(checked(y * Width), Width);
    }
}
