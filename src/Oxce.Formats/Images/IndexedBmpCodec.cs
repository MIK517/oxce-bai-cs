using System.Buffers.Binary;
using Oxce.Core.Graphics;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class IndexedBmpCodec
{
    public const int DefaultMaximumPixels = 16 * 1024 * 1024;

    public static IndexedImageData Decode(
        BinaryDataReader input,
        int maxPixels = DefaultMaximumPixels)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixels);
        var data = input.ReadMemory(input.Remaining).Span;
        if (data.Length < 54 || data[0] != 'B' || data[1] != 'M')
        {
            throw new InvalidDataException("Input does not have a supported BMP header.");
        }

        var pixelOffset = ReadNonNegativeInt32(data[10..], "pixel offset");
        var dibSize = ReadNonNegativeInt32(data[14..], "DIB header size");
        if (dibSize < 40 || dibSize > data.Length - 14)
        {
            throw new InvalidDataException("BMP DIB header is truncated or unsupported.");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        if (width <= 0 || storedHeight == 0 || storedHeight == int.MinValue)
        {
            throw new InvalidDataException("BMP dimensions are outside supported bounds.");
        }

        var height = Math.Abs(storedHeight);
        var pixelCount = checked(width * height);
        if (pixelCount > maxPixels)
        {
            throw new InvalidDataException(
                $"BMP declares {pixelCount} pixels, exceeding the {maxPixels}-pixel limit.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) != 8
            || BinaryPrimitives.ReadUInt32LittleEndian(data[30..]) != 0)
        {
            throw new InvalidDataException("Only uncompressed 8-bit indexed BMP images are supported.");
        }

        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(data[46..]);
        var paletteCount = colorsUsed == 0 ? 256 : checked((int)colorsUsed);
        if (paletteCount is < 1 or > 256)
        {
            throw new InvalidDataException("BMP palette must contain between 1 and 256 entries.");
        }

        var paletteOffset = checked(14 + dibSize);
        var paletteEnd = checked(paletteOffset + (paletteCount * 4));
        if (paletteEnd > data.Length || pixelOffset < paletteEnd)
        {
            throw new InvalidDataException("BMP palette is truncated or overlaps pixel data.");
        }

        var rowStride = checked((width + 3) & ~3);
        if ((long)pixelOffset + ((long)rowStride * height) > data.Length)
        {
            throw new InvalidDataException("BMP pixel data is truncated.");
        }

        var palette = new Rgba32[256];
        Array.Fill(palette, new Rgba32(0, 0, 0));
        for (var index = 0; index < paletteCount; index++)
        {
            var offset = paletteOffset + (index * 4);
            palette[index] = new Rgba32(data[offset + 2], data[offset + 1], data[offset]);
        }

        palette[0] = palette[0] with { Alpha = 0 };
        var pixels = new byte[pixelCount];
        for (var y = 0; y < height; y++)
        {
            var sourceY = storedHeight < 0 ? y : height - 1 - y;
            var source = data.Slice(pixelOffset + (sourceY * rowStride), width);
            if (paletteCount < 256 && source.IndexOfAnyInRange((byte)paletteCount, byte.MaxValue) >= 0)
            {
                throw new InvalidDataException("BMP pixel references a palette entry that does not exist.");
            }

            source.CopyTo(pixels.AsSpan(y * width, width));
        }

        return new IndexedImageData(width, height, pixels, palette, originalTransparentIndex: 0);
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> data, string name)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (value < 0)
        {
            throw new InvalidDataException($"BMP {name} is outside supported bounds.");
        }

        return value;
    }
}
