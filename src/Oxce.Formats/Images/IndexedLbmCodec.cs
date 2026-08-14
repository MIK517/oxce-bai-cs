using System.Buffers.Binary;
using Oxce.Core.Graphics;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class IndexedLbmCodec
{
    public const int DefaultMaximumPixels = 16 * 1024 * 1024;

    private const uint HamFlag = 0x0800;
    private const uint ExtraHalfBrightFlag = 0x0080;

    public static IndexedImageData Decode(
        BinaryDataReader input,
        int maxPixels = DefaultMaximumPixels)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixels);
        var data = input.ReadMemory(input.Remaining).Span;
        if (data.Length < 12 || !data[..4].SequenceEqual("FORM"u8))
        {
            throw new InvalidDataException("Input does not have an IFF FORM header.");
        }

        var isPbm = data.Slice(8, 4).SequenceEqual("PBM "u8);
        var isIlbm = data.Slice(8, 4).SequenceEqual("ILBM"u8);
        if (!isPbm && !isIlbm)
        {
            throw new InvalidDataException("IFF input is not a supported PBM or ILBM image.");
        }

        var formSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (formSize < 4 || formSize > int.MaxValue || 8L + formSize > data.Length)
        {
            throw new InvalidDataException("IFF FORM data is truncated or outside supported bounds.");
        }

        var formEnd = checked(8 + (int)formSize);
        BitmapHeader? header = null;
        ReadOnlySpan<byte> colorMap = default;
        ReadOnlySpan<byte> body = default;
        uint viewMode = 0;
        var position = 12;
        while (position < formEnd)
        {
            if (formEnd - position < 8)
            {
                throw new InvalidDataException("IFF chunk header is truncated.");
            }

            var chunkId = data.Slice(position, 4);
            var chunkSizeValue = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            if (chunkSizeValue > int.MaxValue)
            {
                throw new InvalidDataException("IFF chunk size is outside supported bounds.");
            }

            var chunkSize = (int)chunkSizeValue;
            var chunkStart = checked(position + 8);
            var chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > formEnd)
            {
                throw new InvalidDataException("IFF chunk data is truncated.");
            }

            var chunk = data.Slice(chunkStart, chunkSize);
            if (chunkId.SequenceEqual("BMHD"u8))
            {
                header = ReadHeader(chunk);
            }
            else if (chunkId.SequenceEqual("CMAP"u8))
            {
                colorMap = chunk;
            }
            else if (chunkId.SequenceEqual("CAMG"u8))
            {
                if (chunk.Length < sizeof(uint))
                {
                    throw new InvalidDataException("IFF CAMG chunk is truncated.");
                }

                viewMode = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            }
            else if (chunkId.SequenceEqual("BODY"u8))
            {
                body = chunk;
                break;
            }

            position = checked(chunkEnd + (chunkSize & 1));
            if (position > formEnd)
            {
                throw new InvalidDataException("IFF chunk padding is truncated.");
            }
        }

        if (header is null || body.IsEmpty)
        {
            throw new InvalidDataException("IFF image requires BMHD and non-empty BODY chunks.");
        }

        return DecodeBitmap(header.Value, colorMap, body, viewMode, isPbm, maxPixels);
    }

    private static BitmapHeader ReadHeader(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 20)
        {
            throw new InvalidDataException("IFF BMHD chunk is truncated.");
        }

        return new BitmapHeader(
            BinaryPrimitives.ReadUInt16BigEndian(chunk),
            BinaryPrimitives.ReadUInt16BigEndian(chunk[2..]),
            chunk[8],
            chunk[9],
            chunk[10],
            BinaryPrimitives.ReadUInt16BigEndian(chunk[12..]));
    }

    private static IndexedImageData DecodeBitmap(
        BitmapHeader header,
        ReadOnlySpan<byte> colorMap,
        ReadOnlySpan<byte> body,
        uint viewMode,
        bool isPbm,
        int maxPixels)
    {
        if (header.Width == 0 || header.Height == 0)
        {
            throw new InvalidDataException("IFF image dimensions must be positive.");
        }

        if (header.Compression is not 0 and not 1)
        {
            throw new InvalidDataException("Only uncompressed and ByteRun1-compressed IFF images are supported.");
        }

        if (header.Masking > 2)
        {
            throw new InvalidDataException("IFF image uses an unsupported masking mode.");
        }

        if (!isPbm && (header.Planes is < 1 or > 8 || (viewMode & HamFlag) != 0))
        {
            throw new InvalidDataException("Only indexed ILBM images with one to eight non-HAM planes are supported.");
        }

        if (isPbm && header.Planes is < 1 or > 8)
        {
            throw new InvalidDataException("PBM image declares an unsupported plane count.");
        }

        var width = checked((header.Width + 15) & ~15);
        var pixelCount = checked(width * header.Height);
        if (pixelCount > maxPixels)
        {
            throw new InvalidDataException(
                $"IFF image declares {pixelCount} stored pixels, exceeding the {maxPixels}-pixel limit.");
        }

        var planeRowBytes = checked(((header.Width + 15) / 16) * 2);
        var storedPlanes = isPbm ? 1 : header.Planes + (header.Masking == 1 ? 1 : 0);
        var encodedRowBytes = isPbm ? checked(planeRowBytes * 8) : planeRowBytes;
        var pixels = new byte[pixelCount];
        var row = new byte[encodedRowBytes];
        var bodyPosition = 0;
        for (var y = 0; y < header.Height; y++)
        {
            for (var plane = 0; plane < storedPlanes; plane++)
            {
                DecodePlaneRow(body, ref bodyPosition, row, header.Compression);
                var destination = pixels.AsSpan(y * width, width);
                if (isPbm)
                {
                    row.CopyTo(destination);
                }
                else
                {
                    for (var x = 0; x < width; x++)
                    {
                        if ((row[x >> 3] & (0x80 >> (x & 7))) != 0)
                        {
                            destination[x] |= checked((byte)(1 << plane));
                        }
                    }
                }
            }
        }

        var palette = CreatePalette(colorMap, header, viewMode, isPbm);
        var transparentIndex = header.Masking == 2 ? header.TransparentColor : 0;
        if (transparentIndex > byte.MaxValue)
        {
            throw new InvalidDataException("IFF transparent color is outside the indexed palette range.");
        }

        palette[transparentIndex] = palette[transparentIndex] with { Alpha = 0 };
        if (transparentIndex != 0)
        {
            pixels.AsSpan().Replace((byte)transparentIndex, (byte)0);
        }

        return new IndexedImageData(width, header.Height, pixels, palette, transparentIndex);
    }

    private static void DecodePlaneRow(
        ReadOnlySpan<byte> body,
        ref int position,
        Span<byte> destination,
        byte compression)
    {
        if (compression == 0)
        {
            if (destination.Length > body.Length - position)
            {
                throw new InvalidDataException("IFF BODY pixel data is truncated.");
            }

            body.Slice(position, destination.Length).CopyTo(destination);
            position += destination.Length;
            return;
        }

        var written = 0;
        while (written < destination.Length)
        {
            if (position >= body.Length)
            {
                throw new InvalidDataException("IFF ByteRun1 data is truncated.");
            }

            var control = body[position++];
            if (control <= 127)
            {
                var count = control + 1;
                if (count > destination.Length - written || count > body.Length - position)
                {
                    throw new InvalidDataException("IFF ByteRun1 literal packet exceeds its row or BODY bounds.");
                }

                body.Slice(position, count).CopyTo(destination[written..]);
                position += count;
                written += count;
            }
            else
            {
                var count = 257 - control;
                if (count > destination.Length - written || position >= body.Length)
                {
                    throw new InvalidDataException("IFF ByteRun1 repeat packet exceeds its row or BODY bounds.");
                }

                destination.Slice(written, count).Fill(body[position++]);
                written += count;
            }
        }
    }

    private static Rgba32[] CreatePalette(
        ReadOnlySpan<byte> colorMap,
        BitmapHeader header,
        uint viewMode,
        bool isPbm)
    {
        if (colorMap.Length > 256 * 3)
        {
            throw new InvalidDataException("IFF palette contains more than 256 colors.");
        }

        var palette = new Rgba32[256];
        Array.Fill(palette, new Rgba32(0, 0, 0));
        var colorCount = colorMap.Length / 3;
        for (var index = 0; index < colorCount; index++)
        {
            var offset = index * 3;
            palette[index] = new Rgba32(colorMap[offset], colorMap[offset + 1], colorMap[offset + 2]);
        }

        if (!isPbm && header.Planes == 6 && (colorCount == 32 || (viewMode & ExtraHalfBrightFlag) != 0))
        {
            for (var index = 0; index < 32; index++)
            {
                var source = palette[index];
                palette[index + 32] = new Rgba32(
                    (byte)(source.Red >> 1),
                    (byte)(source.Green >> 1),
                    (byte)(source.Blue >> 1));
            }
        }
        else if (!isPbm && colorCount > 0)
        {
            var requiredColors = 1 << header.Planes;
            for (var index = colorCount; index < requiredColors; index++)
            {
                palette[index] = palette[index % colorCount];
            }
        }

        return palette;
    }

    private readonly record struct BitmapHeader(
        int Width,
        int Height,
        int Planes,
        byte Masking,
        byte Compression,
        int TransparentColor);
}
