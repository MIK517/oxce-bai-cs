using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.Mods.Files;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class IndexedImageCodecTests
{
    [Fact]
    public void RawDecodeCopiesVisiblePixelsAndConsumesOverscan()
    {
        var destination = Enumerable.Repeat((byte)9, 4).ToArray();
        var reader = Reader("010203040506");

        RawIndexedImageCodec.Decode(reader, destination);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, destination);
        reader.RequireEnd();
    }

    [Fact]
    public void SpkDecodeHandlesTransparentLiteralUnknownAndOverscanCommands()
    {
        var destination = Enumerable.Repeat((byte)99, 18).ToArray();
        var reader = Reader(
            "FFFF0200" +
            "FEFF0300010203040506" +
            "3412" +
            "FFFF0100" +
            "FEFF03000708090A0B0C" +
            "FFFF0100");

        SpkImageCodec.Decode(reader, destination);

        Assert.Equal(
            new byte[] { 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 0, 0, 7, 8, 9, 10, 11, 12 },
            destination);
        reader.RequireEnd();
    }

    [Theory]
    [InlineData("FF")]
    [InlineData("FFFF")]
    [InlineData("FEFF010001")]
    public void SpkDecodeRejectsTruncatedCommands(string hex)
    {
        var reader = Reader(hex);

        Assert.Throws<InvalidDataException>(() => SpkImageCodec.Decode(reader, new byte[8]));
    }

    [Fact]
    public void BdyDecodeClipsRunsAtRowsLikeTheReferenceSurface()
    {
        var destination = new byte[18];
        var reader = Reader(
            "0301020304" +
            "FC09" +
            "0705060708090AAABB" +
            "FB0B");

        BdyImageCodec.Decode(reader, destination, width: 6);

        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 9, 9, 5, 6, 7, 8, 9, 10, 11, 11, 11, 11, 11, 11 },
            destination);
        reader.RequireEnd();
    }

    [Theory]
    [InlineData("81")]
    [InlineData("020102")]
    public void BdyDecodeRejectsTruncatedRuns(string hex)
    {
        var reader = Reader(hex);

        Assert.Throws<InvalidDataException>(() => BdyImageCodec.Decode(reader, new byte[8], width: 4));
    }

    [Fact]
    public void BdyDecodeRequiresWholeRows()
    {
        var reader = Reader(string.Empty);

        Assert.Throws<ArgumentException>(() => BdyImageCodec.Decode(reader, new byte[5], width: 4));
    }

    [Fact]
    public void PckDecodeUsesSixteenBitTabCountAndSequentialFrames()
    {
        var image = Reader("010102FE0203040506FF000708090A0B0C0D0E0F101112FF03FF");
        var offsets = Reader("000005000C00");

        var frames = PckSpriteSetCodec.Decode(image, offsets, width: 4, height: 3);

        Assert.Equal(3, frames.Count);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 2, 0, 0, 3, 4, 5, 6 }, frames[0]);
        Assert.Equal(new byte[] { 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 }, frames[1]);
        Assert.Equal(new byte[12], frames[2]);
        offsets.RequireEnd();
        image.RequireEnd();
    }

    [Fact]
    public void PckDecodeRecognizesThirtyTwoBitTabAndTwoByteSingleFrameTab()
    {
        var pck = "0001FF0002FF0003FF";
        var tab32 = Reader("000000000300000006000000");
        var tab16Single = Reader("0000");

        var frames32 = PckSpriteSetCodec.Decode(Reader(pck), tab32, width: 1, height: 1);
        var frame16 = PckSpriteSetCodec.Decode(Reader("0009FF"), tab16Single, width: 1, height: 1);

        Assert.Equal(new byte[][] { [1], [2], [3] }, frames32);
        Assert.Equal(new byte[][] { [9] }, frame16);
    }

    [Fact]
    public void PckDecodeWithoutTabReadsOneFrameAndLeavesFollowingData()
    {
        var image = Reader("000102FF000304FF");

        var frames = PckSpriteSetCodec.Decode(image, offsets: null, width: 2, height: 1);

        Assert.Equal(new byte[][] { [1, 2] }, frames);
        Assert.Equal(4, image.Remaining);
    }

    [Theory]
    [InlineData("", "00")]
    [InlineData("00", "0000")]
    [InlineData("00FE", "0000")]
    public void PckDecodeRejectsMalformedImages(string imageHex, string tabHex)
    {
        Assert.Throws<InvalidDataException>(
            () => PckSpriteSetCodec.Decode(Reader(imageHex), Reader(tabHex), width: 2, height: 2));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("000000")]
    [InlineData("0000000000")]
    public void PckDecodeRejectsIncompleteTabEntries(string tabHex)
    {
        Assert.Throws<InvalidDataException>(
            () => PckSpriteSetCodec.Decode(Reader("00FF"), Reader(tabHex), width: 1, height: 1));
    }

    [Fact]
    public void PckDecodeLimitsExpandedSizeBeforeAllocatingFrames()
    {
        Assert.Throws<InvalidDataException>(
            () => PckSpriteSetCodec.Decode(
                Reader("00FF"),
                Reader("00000100"),
                width: 4,
                height: 4,
                maxDecodedBytes: 31));
    }

    [Fact]
    public void CatalogEntryStreamsIntoBoundedDecoderAndIndexedSurface()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oxce-image-codec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "GEOGRAPH"));
        try
        {
            File.WriteAllBytes(
                Path.Combine(root, "GEOGRAPH", "fixture.spk"),
                Convert.FromHexString("FEFF020001020304"));
            var layer = VirtualFileLayer.ScanDirectory(root, "fixture");
            var catalog = new VirtualFileCatalog([layer]);
            var entry = catalog.GetRequired("geograph/FIXTURE.SPK");
            using var stream = entry.OpenRead();
            var reader = BinaryDataReader.FromStream(stream, maxBytes: 8);
            var surface = new IndexedSurface(2, 2);

            SpkImageCodec.Decode(reader, surface.Pixels);

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, surface.Pixels.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
