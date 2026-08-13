using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class CatArchiveTests
{
    [Fact]
    public void ParseDerivesEntrySizesFromOffsetsAndIgnoresStoredSizes()
    {
        var archive = Parse(
            "1800000063000000" +
            "1C00000001000000" +
            "22000000FFFFFFFF" +
            "03414243" +
            "020058595A00" +
            "010203");

        Assert.Equal(3, archive.Entries.Count);
        Assert.Equal(24, archive[0].Offset);
        Assert.Equal("03414243", Convert.ToHexString(archive[0].Data.Span));
        Assert.Equal("020058595A00", Convert.ToHexString(archive[1].Data.Span));
        Assert.Equal("010203", Convert.ToHexString(archive[2].Data.Span));
    }

    [Fact]
    public void ParseAllowsDuplicateOffsetsLikeTheReference()
    {
        var archive = Parse("10000000000000001000000000000000AA");

        Assert.Empty(archive[0].Data.ToArray());
        Assert.Equal(new byte[] { 0xAA }, archive[1].Data.ToArray());
    }

    [Fact]
    public void ParseTreatsZeroFirstOffsetAsAnEmptyArchive()
    {
        var archive = Parse("00000000");

        Assert.Empty(archive.Entries);
    }

    [Fact]
    public void EntryOpensIndependentReadableStream()
    {
        var archive = Parse("0800000001000000AABB");
        using var stream = archive[0].OpenRead();

        Assert.Equal(0xAA, stream.ReadByte());
        Assert.Equal(0xBB, stream.ReadByte());
        Assert.Equal(-1, stream.ReadByte());
    }

    [Theory]
    [InlineData("")]
    [InlineData("010203")]
    [InlineData("0700000000000000")]
    [InlineData("0800000000000000")]
    [InlineData("10000000000000000F00000000000000AA")]
    public void ParseRejectsMalformedTables(string hex)
    {
        Assert.Throws<InvalidDataException>(() => Parse(hex));
    }

    [Fact]
    public void ParseEnforcesEntryLimitBeforeAllocatingEntries()
    {
        Assert.Throws<InvalidDataException>(
            () => CatArchive.Parse(Reader("10000000000000001000000000000000AA"), maxEntries: 1));
    }

    private static CatArchive Parse(string hex) => CatArchive.Parse(Reader(hex));

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
