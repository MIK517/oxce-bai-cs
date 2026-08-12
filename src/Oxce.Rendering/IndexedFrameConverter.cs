namespace Oxce.Rendering;

public static class IndexedFrameConverter
{
    public const int RgbaBytesPerPixel = 4;

    public static void ConvertToRgba32(
        IndexedSurface surface,
        IndexedPalette palette,
        Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(palette);
        var requiredLength = checked(surface.Pixels.Length * RgbaBytesPerPixel);
        if (destination.Length != requiredLength)
        {
            throw new ArgumentException(
                $"RGBA destination must contain exactly {requiredLength} bytes.",
                nameof(destination));
        }

        var pixels = surface.Pixels;
        for (var index = 0; index < pixels.Length; ++index)
        {
            var color = palette[pixels[index]];
            var offset = index * RgbaBytesPerPixel;
            destination[offset] = color.Red;
            destination[offset + 1] = color.Green;
            destination[offset + 2] = color.Blue;
            destination[offset + 3] = color.Alpha;
        }
    }
}
