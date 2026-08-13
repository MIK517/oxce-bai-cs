using Oxce.Core.Graphics;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class XcomPaletteCodec
{
    public const int ColorsPerPalette = 256;
    public const int BytesPerColor = 3;
    public const int PaletteDataBytes = ColorsPerPalette * BytesPerColor;
    public const int PaletteSeparatorBytes = 6;
    public const int BackgroundColorStart = 224;
    public const int ColorsPerBlock = 16;

    public static Rgba32[] Decode(BinaryDataReader input, int colorCount, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(colorCount);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        input.Seek(Math.Min(offset, input.Length));
        var colors = new Rgba32[colorCount];
        for (var index = 0; index < colors.Length && input.Remaining >= BytesPerColor; index++)
        {
            colors[index] = new Rgba32(
                ScaleRgb6(input.ReadByte()),
                ScaleRgb6(input.ReadByte()),
                ScaleRgb6(input.ReadByte()));
        }

        colors[0] = colors[0] with { Alpha = 0 };
        return colors;
    }

    public static int GetPaletteOffset(int paletteIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(paletteIndex);
        return checked(paletteIndex * (PaletteDataBytes + PaletteSeparatorBytes));
    }

    public static int GetColorBlockOffset(int blockIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        return checked(blockIndex * ColorsPerBlock);
    }

    private static byte ScaleRgb6(byte value) => unchecked((byte)(value * 4));
}
