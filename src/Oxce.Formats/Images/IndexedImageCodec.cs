using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class IndexedImageCodec
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static IndexedImageData Decode(
        BinaryDataReader input,
        int maxPixels = IndexedPngCodec.DefaultMaximumPixels)
    {
        ArgumentNullException.ThrowIfNull(input);
        var encoded = input.ReadMemory(input.Remaining);
        var signature = encoded.Span;
        var nested = new BinaryDataReader(encoded, input.Length);
        if (signature.StartsWith(PngSignature))
        {
            return IndexedPngCodec.Decode(nested, maxPixels);
        }

        if (signature.StartsWith("GIF87a"u8) || signature.StartsWith("GIF89a"u8))
        {
            return IndexedGifCodec.Decode(nested, maxPixels);
        }

        if (signature.StartsWith("BM"u8))
        {
            return IndexedBmpCodec.Decode(nested, maxPixels);
        }

        if (signature.Length >= 12
            && signature.StartsWith("FORM"u8)
            && (signature.Slice(8, 4).SequenceEqual("PBM "u8)
                || signature.Slice(8, 4).SequenceEqual("ILBM"u8)))
        {
            return IndexedLbmCodec.Decode(nested, maxPixels);
        }

        throw new InvalidDataException("Input is not a supported indexed PNG, GIF, BMP, PBM, or ILBM image.");
    }
}
