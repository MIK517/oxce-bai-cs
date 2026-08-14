using System.Buffers.Binary;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class IndexedLbmCodecTests
{
    private const string Pbm =
        "464F524D0000005050424D20424D4844000000140005000200000000080201000002010100050002434D41500000000C000000FF000000FF000000FF424F4459000000130F00010203010000000000000000000000F10300";

    private const string Ilbm =
        "464F524D0000004E494C424D424D4844000000140008000200000000020101000000010100080002434D41500000000C000000FF000000FF000000FF424F44590000001201550001330001F00001FF0001FF0001AA00";

    [Fact]
    public void DecodeReadsCompressedChunkyPixelsAndTransparentColor()
    {
        var image = IndexedLbmCodec.Decode(Reader(Pbm));

        Assert.Equal(16, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(
            Convert.FromHexString("0001000301000000000000000000000003030303030303030303030303030303"),
            image.Pixels.ToArray());
        Assert.Equal(2, image.OriginalTransparentIndex);
        Assert.Equal(0, image.Palette[2].Alpha);
        Assert.Equal(byte.MaxValue, image.Palette[0].Alpha);
    }

    [Fact]
    public void DecodeReadsCompressedPlanarPixelsAndStencilPlane()
    {
        var image = IndexedLbmCodec.Decode(Reader(Ilbm));

        Assert.Equal(16, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(
            Convert.FromHexString("0405060700010203000000000000000007030703070307030000000000000000"),
            image.Pixels.ToArray());
        Assert.Equal(0, image.OriginalTransparentIndex);
        Assert.Equal(0, image.Palette[0].Alpha);
    }

    [Fact]
    public void DecodeReadsUncompressedChunkyRows()
    {
        var compressed = Convert.FromHexString(Pbm);
        var bodyOffset = compressed.AsSpan().IndexOf("BODY"u8);
        var encoded = new byte[bodyOffset + 8 + 32];
        compressed.AsSpan(0, bodyOffset + 8).CopyTo(encoded);
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(4), checked((uint)(encoded.Length - 8)));
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(bodyOffset + 4), 32);
        encoded[30] = 0;
        Convert.FromHexString("00010203010200000000000000000000").CopyTo(encoded, bodyOffset + 8);
        encoded.AsSpan(bodyOffset + 24, 16).Fill(3);

        var image = IndexedLbmCodec.Decode(new BinaryDataReader(encoded));

        Assert.Equal(
            Convert.FromHexString("0001000301000000000000000000000003030303030303030303030303030303"),
            image.Pixels.ToArray());
    }

    [Fact]
    public void DecodeRejectsTruncationInvalidCompressionAndPixelLimit()
    {
        var invalidCompression = Convert.FromHexString(Pbm);
        invalidCompression[30] = 2;

        Assert.Throws<InvalidDataException>(() => IndexedLbmCodec.Decode(Reader(Pbm[..^4])));
        Assert.Throws<InvalidDataException>(() => IndexedLbmCodec.Decode(new BinaryDataReader(invalidCompression)));
        Assert.Throws<InvalidDataException>(() => IndexedLbmCodec.Decode(Reader(Pbm), maxPixels: 31));
    }

    [Fact]
    public void GeneralCodecDispatchesPbmAndIlbmSignatures()
    {
        Assert.Equal(16, IndexedImageCodec.Decode(Reader(Pbm)).Width);
        Assert.Equal(16, IndexedImageCodec.Decode(Reader(Ilbm)).Width);
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
