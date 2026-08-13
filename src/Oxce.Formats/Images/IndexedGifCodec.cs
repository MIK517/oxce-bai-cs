using System.Buffers.Binary;
using Oxce.Core.Graphics;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class IndexedGifCodec
{
    public const int DefaultMaximumPixels = 16 * 1024 * 1024;

    public static IndexedImageData Decode(
        BinaryDataReader input,
        int maxPixels = DefaultMaximumPixels)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixels);
        var data = input.ReadMemory(input.Remaining).Span;
        if (data.Length < 13 || (!data[..6].SequenceEqual("GIF87a"u8) && !data[..6].SequenceEqual("GIF89a"u8)))
        {
            throw new InvalidDataException("Input does not have a valid GIF header.");
        }

        var width = ReadPositiveWord(data[6..], "width");
        var height = ReadPositiveWord(data[8..], "height");
        var pixelCount = checked(width * height);
        if (pixelCount > maxPixels)
        {
            throw new InvalidDataException(
                $"GIF declares {pixelCount} pixels, exceeding the {maxPixels}-pixel limit.");
        }

        var packed = data[10];
        var backgroundIndex = data[11];
        var position = 13;
        var globalPalette = (packed & 0x80) == 0
            ? null
            : ReadPalette(data, ref position, 1 << ((packed & 7) + 1));
        var transparentIndex = 0;
        while (position < data.Length)
        {
            var marker = data[position++];
            if (marker == 0x3b)
            {
                break;
            }

            if (marker == 0x21)
            {
                ReadExtension(data, ref position, ref transparentIndex);
                continue;
            }

            if (marker != 0x2c)
            {
                throw new InvalidDataException($"GIF contains unknown block marker 0x{marker:X2}.");
            }

            if (data.Length - position < 9)
            {
                throw new InvalidDataException("GIF contains a truncated image descriptor.");
            }

            var left = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            var top = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 2)..]);
            var imageWidth = ReadPositiveWord(data[(position + 4)..], "image width");
            var imageHeight = ReadPositiveWord(data[(position + 6)..], "image height");
            var imagePacked = data[position + 8];
            position += 9;
            if ((long)left + imageWidth > width || (long)top + imageHeight > height)
            {
                throw new InvalidDataException("GIF image descriptor extends beyond the logical screen.");
            }

            var palette = (imagePacked & 0x80) == 0
                ? globalPalette
                : ReadPalette(data, ref position, 1 << ((imagePacked & 7) + 1));
            if (palette is null)
            {
                throw new InvalidDataException("GIF image has neither a global nor local color table.");
            }

            if (position >= data.Length)
            {
                throw new InvalidDataException("GIF is missing its LZW code size.");
            }

            var minimumCodeSize = data[position++];
            if (minimumCodeSize is < 2 or > 8)
            {
                throw new InvalidDataException("GIF LZW minimum code size must be between 2 and 8.");
            }

            var compressed = ReadSubBlocks(data, ref position);
            var imagePixels = DecodeLzw(compressed, minimumCodeSize, checked(imageWidth * imageHeight));
            var canvas = new byte[pixelCount];
            canvas.AsSpan().Fill(backgroundIndex < palette.Length ? backgroundIndex : (byte)0);
            CopyImage(
                imagePixels,
                canvas,
                width,
                left,
                top,
                imageWidth,
                imageHeight,
                (imagePacked & 0x40) != 0,
                palette.Length);
            NormalizeTransparentIndex(canvas, transparentIndex);
            var outputPalette = new Rgba32[256];
            Array.Fill(outputPalette, new Rgba32(0, 0, 0));
            palette.CopyTo(outputPalette, 0);
            if (transparentIndex < outputPalette.Length)
            {
                outputPalette[transparentIndex] = outputPalette[transparentIndex] with { Alpha = 0 };
            }

            return new IndexedImageData(width, height, canvas, outputPalette, transparentIndex);
        }

        throw new InvalidDataException("GIF does not contain an image frame.");
    }

    private static void ReadExtension(ReadOnlySpan<byte> data, ref int position, ref int transparentIndex)
    {
        if (position >= data.Length)
        {
            throw new InvalidDataException("GIF contains a truncated extension.");
        }

        var label = data[position++];
        if (label == 0xf9)
        {
            if (data.Length - position < 6 || data[position] != 4 || data[position + 5] != 0)
            {
                throw new InvalidDataException("GIF contains a malformed graphic-control extension.");
            }

            if ((data[position + 1] & 1) != 0)
            {
                transparentIndex = data[position + 4];
            }

            position += 6;
            return;
        }

        _ = ReadSubBlocks(data, ref position);
    }

    private static byte[] ReadSubBlocks(ReadOnlySpan<byte> data, ref int position)
    {
        using var output = new MemoryStream();
        while (true)
        {
            if (position >= data.Length)
            {
                throw new InvalidDataException("GIF contains unterminated data sub-blocks.");
            }

            var length = data[position++];
            if (length == 0)
            {
                return output.ToArray();
            }

            if (length > data.Length - position)
            {
                throw new InvalidDataException("GIF contains a truncated data sub-block.");
            }

            output.Write(data.Slice(position, length));
            position += length;
        }
    }

    private static byte[] DecodeLzw(ReadOnlySpan<byte> compressed, int minimumCodeSize, int outputLength)
    {
        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var stack = new byte[4097];
        for (var index = 0; index < clearCode; index++)
        {
            suffix[index] = (byte)index;
        }

        var output = new byte[outputLength];
        var outputPosition = 0;
        var bitPosition = 0;
        var codeSize = minimumCodeSize + 1;
        var nextCode = endCode + 1;
        var oldCode = -1;
        byte first = 0;
        while (true)
        {
            var code = ReadCode(compressed, ref bitPosition, codeSize);
            if (code == clearCode)
            {
                codeSize = minimumCodeSize + 1;
                nextCode = endCode + 1;
                oldCode = -1;
                continue;
            }

            if (code == endCode)
            {
                break;
            }

            if (code < 0 || code >= 4096 || (code >= nextCode && oldCode < 0))
            {
                throw new InvalidDataException("GIF contains an invalid LZW code.");
            }

            var currentCode = code;
            var stackPosition = 0;
            if (code == nextCode)
            {
                if (oldCode < 0)
                {
                    throw new InvalidDataException("GIF LZW stream references an unavailable code.");
                }

                stack[stackPosition++] = first;
                code = oldCode;
            }
            else if (code > nextCode)
            {
                throw new InvalidDataException("GIF LZW stream skips a dictionary code.");
            }

            while (code >= clearCode)
            {
                if (code >= nextCode || stackPosition >= stack.Length - 1)
                {
                    throw new InvalidDataException("GIF LZW dictionary chain is invalid.");
                }

                stack[stackPosition++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            stack[stackPosition++] = first;
            while (stackPosition != 0)
            {
                if (outputPosition >= output.Length)
                {
                    throw new InvalidDataException("GIF LZW output exceeds the image dimensions.");
                }

                output[outputPosition++] = stack[--stackPosition];
            }

            if (oldCode >= 0 && nextCode < 4096)
            {
                prefix[nextCode] = (short)oldCode;
                suffix[nextCode] = first;
                nextCode++;
                if (nextCode == 1 << codeSize && codeSize < 12)
                {
                    codeSize++;
                }
            }

            oldCode = currentCode;
        }

        if (outputPosition != output.Length)
        {
            throw new InvalidDataException("GIF LZW output is shorter than the image dimensions.");
        }

        return output;
    }

    private static int ReadCode(ReadOnlySpan<byte> data, ref int bitPosition, int codeSize)
    {
        if (bitPosition + codeSize > data.Length * 8)
        {
            throw new InvalidDataException("GIF LZW stream ends before its end code.");
        }

        var result = 0;
        for (var bit = 0; bit < codeSize; bit++)
        {
            result |= ((data[bitPosition >> 3] >> (bitPosition & 7)) & 1) << bit;
            bitPosition++;
        }

        return result;
    }

    private static void CopyImage(
        byte[] source,
        byte[] canvas,
        int canvasWidth,
        int left,
        int top,
        int width,
        int height,
        bool interlaced,
        int paletteCount)
    {
        var sourceRow = 0;
        ReadOnlySpan<int> starts = interlaced ? [0, 4, 2, 1] : [0];
        ReadOnlySpan<int> steps = interlaced ? [8, 8, 4, 2] : [1];
        for (var pass = 0; pass < starts.Length; pass++)
        {
            for (var y = starts[pass]; y < height; y += steps[pass])
            {
                var sourceOffset = checked(sourceRow++ * width);
                var targetOffset = checked(((top + y) * canvasWidth) + left);
                for (var x = 0; x < width; x++)
                {
                    var value = source[sourceOffset + x];
                    if (value >= paletteCount)
                    {
                        throw new InvalidDataException("GIF pixel references a palette entry that does not exist.");
                    }

                    canvas[targetOffset + x] = value;
                }
            }
        }
    }

    private static Rgba32[] ReadPalette(ReadOnlySpan<byte> data, ref int position, int colorCount)
    {
        var byteCount = checked(colorCount * 3);
        if (byteCount > data.Length - position)
        {
            throw new InvalidDataException("GIF contains a truncated color table.");
        }

        var palette = new Rgba32[colorCount];
        for (var index = 0; index < palette.Length; index++)
        {
            palette[index] = new Rgba32(
                data[position++],
                data[position++],
                data[position++]);
        }

        return palette;
    }

    private static int ReadPositiveWord(ReadOnlySpan<byte> data, string name)
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (value == 0)
        {
            throw new InvalidDataException($"GIF {name} must be positive.");
        }

        return value;
    }

    private static void NormalizeTransparentIndex(byte[] pixels, int transparentIndex)
    {
        if (transparentIndex == 0)
        {
            return;
        }

        for (var index = 0; index < pixels.Length; index++)
        {
            if (pixels[index] == transparentIndex)
            {
                pixels[index] = 0;
            }
        }
    }
}
