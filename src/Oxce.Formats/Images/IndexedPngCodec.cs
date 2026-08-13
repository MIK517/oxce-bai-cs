using System.Buffers.Binary;
using System.IO.Compression;
using Oxce.Core.Graphics;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class IndexedPngCodec
{
    public const int DefaultMaximumPixels = 16 * 1024 * 1024;
    private const uint Ihdr = 0x49484452;
    private const uint Plte = 0x504c5445;
    private const uint Idat = 0x49444154;
    private const uint Trns = 0x74524e53;
    private const uint Iend = 0x49454e44;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static ReadOnlySpan<int> PassStartX => [0, 4, 0, 2, 0, 1, 0];
    private static ReadOnlySpan<int> PassStartY => [0, 0, 4, 0, 2, 0, 1];
    private static ReadOnlySpan<int> PassStepX => [8, 8, 4, 4, 2, 2, 1];
    private static ReadOnlySpan<int> PassStepY => [8, 8, 8, 4, 4, 2, 2];

    public static IndexedImageData Decode(
        BinaryDataReader input,
        int maxPixels = DefaultMaximumPixels)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixels);
        var encoded = input.ReadMemory(input.Remaining);
        var data = encoded.Span;
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Input does not have a valid PNG signature.");
        }

        var position = Signature.Length;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var interlace = 0;
        byte[]? paletteBytes = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        var sawHeader = false;
        var sawEnd = false;
        while (position < data.Length)
        {
            if (data.Length - position < 12)
            {
                throw new InvalidDataException("PNG contains a truncated chunk header.");
            }

            var lengthWord = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            if (lengthWord > int.MaxValue)
            {
                throw new InvalidDataException("PNG chunk length exceeds supported bounds.");
            }

            var length = (int)lengthWord;
            var chunkEnd = checked(position + 12 + length);
            if (chunkEnd > data.Length)
            {
                throw new InvalidDataException("PNG contains a truncated chunk.");
            }

            var type = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            var chunkData = data.Slice(position + 8, length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 8 + length)..]);
            if (ComputeCrc(data.Slice(position + 4, length + 4)) != expectedCrc)
            {
                throw new InvalidDataException("PNG chunk CRC does not match its contents.");
            }

            switch (type)
            {
                case Ihdr:
                    if (sawHeader || length != 13)
                    {
                        throw new InvalidDataException("PNG must contain one 13-byte IHDR chunk.");
                    }

                    width = ReadDimension(chunkData, "width");
                    height = ReadDimension(chunkData[4..], "height");
                    bitDepth = chunkData[8];
                    if (chunkData[9] != 3 || (bitDepth is not 1 and not 2 and not 4 and not 8))
                    {
                        throw new InvalidDataException("Only indexed PNG images with 1, 2, 4, or 8 bits per pixel are supported.");
                    }

                    if (chunkData[10] != 0 || chunkData[11] != 0 || chunkData[12] > 1)
                    {
                        throw new InvalidDataException("PNG uses an unsupported compression, filter, or interlace method.");
                    }

                    interlace = chunkData[12];
                    var pixelCount = checked(width * height);
                    if (pixelCount > maxPixels)
                    {
                        throw new InvalidDataException(
                            $"PNG declares {pixelCount} pixels, exceeding the {maxPixels}-pixel limit.");
                    }

                    sawHeader = true;
                    break;
                case Plte:
                    EnsureAfterHeader(sawHeader, "PLTE");
                    if (length == 0 || length % 3 != 0 || length > 256 * 3)
                    {
                        throw new InvalidDataException("PNG palette must contain between 1 and 256 RGB entries.");
                    }

                    paletteBytes = chunkData.ToArray();
                    break;
                case Trns:
                    EnsureAfterHeader(sawHeader, "tRNS");
                    if (length > 256)
                    {
                        throw new InvalidDataException("PNG palette transparency contains more than 256 entries.");
                    }

                    transparency = chunkData.ToArray();
                    break;
                case Idat:
                    EnsureAfterHeader(sawHeader, "IDAT");
                    compressed.Write(chunkData);
                    break;
                case Iend:
                    if (length != 0)
                    {
                        throw new InvalidDataException("PNG IEND chunk must be empty.");
                    }

                    sawEnd = true;
                    position = chunkEnd;
                    break;
            }

            position = chunkEnd;
            if (sawEnd)
            {
                break;
            }
        }

        if (!sawHeader || !sawEnd || position != data.Length || paletteBytes is null || compressed.Length == 0)
        {
            throw new InvalidDataException("PNG is missing required IHDR, PLTE, IDAT, or IEND data.");
        }

        var paletteCount = paletteBytes.Length / 3;
        if (paletteCount > 1 << bitDepth || (transparency?.Length ?? 0) > paletteCount)
        {
            throw new InvalidDataException("PNG palette is inconsistent with its bit depth or transparency table.");
        }

        var inflatedLength = GetInflatedLength(width, height, bitDepth, interlace);
        var inflated = new byte[inflatedLength];
        compressed.Position = 0;
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            inflater.ReadExactly(inflated);
            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("PNG decompressed data exceeds its declared dimensions.");
            }
        }

        var pixels = DecodePixels(inflated, width, height, bitDepth, interlace, paletteCount);
        var palette = DecodePalette(paletteBytes, transparency);
        var transparentIndex = NormalizeTransparentIndex(pixels, palette);
        return new IndexedImageData(width, height, pixels, palette, transparentIndex);
    }

    private static byte[] DecodePixels(
        byte[] inflated,
        int width,
        int height,
        int bitDepth,
        int interlace,
        int paletteCount)
    {
        var pixels = new byte[checked(width * height)];
        var position = 0;
        var passCount = interlace == 0 ? 1 : 7;
        for (var pass = 0; pass < passCount; pass++)
        {
            var startX = interlace == 0 ? 0 : PassStartX[pass];
            var startY = interlace == 0 ? 0 : PassStartY[pass];
            var stepX = interlace == 0 ? 1 : PassStepX[pass];
            var stepY = interlace == 0 ? 1 : PassStepY[pass];
            var passWidth = GetPassSize(width, startX, stepX);
            var passHeight = GetPassSize(height, startY, stepY);
            if (passWidth == 0 || passHeight == 0)
            {
                continue;
            }

            var rowBytes = checked((passWidth * bitDepth + 7) / 8);
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            for (var passY = 0; passY < passHeight; passY++)
            {
                var filter = inflated[position++];
                inflated.AsSpan(position, rowBytes).CopyTo(current);
                position += rowBytes;
                Unfilter(current, previous, filter);
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var paletteIndex = ReadPackedSample(current, passX, bitDepth);
                    if (paletteIndex >= paletteCount)
                    {
                        throw new InvalidDataException("PNG pixel references a palette entry that does not exist.");
                    }

                    var x = startX + (passX * stepX);
                    var y = startY + (passY * stepY);
                    pixels[checked((y * width) + x)] = paletteIndex;
                }

                (previous, current) = (current, previous);
            }
        }

        return pixels;
    }

    private static void Unfilter(Span<byte> row, ReadOnlySpan<byte> previous, byte filter)
    {
        for (var index = 0; index < row.Length; index++)
        {
            var left = index == 0 ? 0 : row[index - 1];
            var above = previous[index];
            var upperLeft = index == 0 ? 0 : previous[index - 1];
            row[index] = filter switch
            {
                0 => row[index],
                1 => unchecked((byte)(row[index] + left)),
                2 => unchecked((byte)(row[index] + above)),
                3 => unchecked((byte)(row[index] + ((left + above) / 2))),
                4 => unchecked((byte)(row[index] + Paeth(left, above, upperLeft))),
                _ => throw new InvalidDataException($"PNG uses unknown scanline filter {filter}."),
            };
        }
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static byte ReadPackedSample(ReadOnlySpan<byte> row, int x, int bitDepth)
    {
        var bit = checked(x * bitDepth);
        var shift = 8 - bitDepth - (bit & 7);
        return (byte)((row[bit >> 3] >> shift) & ((1 << bitDepth) - 1));
    }

    private static Rgba32[] DecodePalette(byte[] bytes, byte[]? transparency)
    {
        var palette = new Rgba32[bytes.Length / 3];
        for (var index = 0; index < palette.Length; index++)
        {
            palette[index] = new Rgba32(
                bytes[index * 3],
                bytes[(index * 3) + 1],
                bytes[(index * 3) + 2],
                transparency is not null && index < transparency.Length ? transparency[index] : byte.MaxValue);
        }

        return palette;
    }

    private static int NormalizeTransparentIndex(byte[] pixels, Rgba32[] palette)
    {
        var transparent = Array.FindIndex(palette, color => color.Alpha == 0);
        if (transparent <= 0)
        {
            return transparent < 0 ? 0 : transparent;
        }

        for (var index = 0; index < pixels.Length; index++)
        {
            if (pixels[index] == transparent)
            {
                pixels[index] = 0;
            }
        }

        return transparent;
    }

    private static int GetInflatedLength(int width, int height, int bitDepth, int interlace)
    {
        var total = 0;
        var passCount = interlace == 0 ? 1 : 7;
        for (var pass = 0; pass < passCount; pass++)
        {
            var passWidth = interlace == 0 ? width : GetPassSize(width, PassStartX[pass], PassStepX[pass]);
            var passHeight = interlace == 0 ? height : GetPassSize(height, PassStartY[pass], PassStepY[pass]);
            if (passWidth != 0 && passHeight != 0)
            {
                total = checked(total + (passHeight * (1 + ((passWidth * bitDepth + 7) / 8))));
            }
        }

        return total;
    }

    private static int GetPassSize(int size, int start, int step) =>
        size <= start ? 0 : ((size - start - 1) / step) + 1;

    private static int ReadDimension(ReadOnlySpan<byte> data, string name)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (value == 0 || value > int.MaxValue)
        {
            throw new InvalidDataException($"PNG {name} is outside supported bounds.");
        }

        return (int)value;
    }

    private static void EnsureAfterHeader(bool sawHeader, string chunk)
    {
        if (!sawHeader)
        {
            throw new InvalidDataException($"PNG {chunk} chunk appears before IHDR.");
        }
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
        }

        return ~crc;
    }
}
