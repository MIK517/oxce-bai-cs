using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class BinaryDataTests
{
    [Fact]
    public void ReaderUsesExplicitEndianAndTracksBounds()
    {
        var reader = new BinaryDataReader(Convert.FromHexString(
            "01FF3412123478563412123456780000803F3F8000000807060504030201"));

        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(-1, reader.ReadSByte());
        Assert.Equal(0x1234, reader.ReadUInt16LittleEndian());
        Assert.Equal(0x1234, reader.ReadUInt16BigEndian());
        Assert.Equal(0x12345678U, reader.ReadUInt32LittleEndian());
        Assert.Equal(0x12345678U, reader.ReadUInt32BigEndian());
        Assert.Equal(1F, reader.ReadSingleLittleEndian());
        Assert.Equal(1F, reader.ReadSingleBigEndian());
        Assert.Equal(0x0102030405060708UL, reader.ReadUInt64LittleEndian());
        reader.RequireEnd();
    }

    [Fact]
    public void ReaderRejectsOutOfRangeReadsSeeksAndTrailingData()
    {
        var reader = new BinaryDataReader(new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidDataException>(() => reader.ReadUInt32LittleEndian());
        Assert.Equal(0, reader.Position);
        Assert.Throws<InvalidDataException>(() => reader.Seek(4));
        reader.Skip(2);
        Assert.Throws<InvalidDataException>(() => reader.Skip(2));
        Assert.Throws<InvalidDataException>(reader.RequireEnd);
    }

    [Fact]
    public void SubReaderCannotEscapeItsDeclaredRegion()
    {
        var reader = new BinaryDataReader(new byte[] { 1, 2, 3, 4 });
        var child = reader.ReadSubReader(2);

        Assert.Equal([1, 2], child.ReadMemory(2).ToArray());
        Assert.Throws<InvalidDataException>(() => child.ReadByte());
        Assert.Equal(2, reader.Position);
        Assert.Equal([3, 4], reader.ReadMemory(2).ToArray());
    }

    [Fact]
    public void WriterRoundTripsEveryPrimitiveAndEnforcesLimit()
    {
        var writer = new BinaryDataWriter(maxBytes: 30);
        writer.WriteByte(1);
        writer.WriteSByte(-1);
        writer.WriteUInt16LittleEndian(0x1234);
        writer.WriteUInt16BigEndian(0x1234);
        writer.WriteUInt32LittleEndian(0x12345678);
        writer.WriteUInt32BigEndian(0x12345678);
        writer.WriteSingleLittleEndian(1F);
        writer.WriteSingleBigEndian(1F);
        writer.WriteUInt64LittleEndian(0x0102030405060708);

        Assert.Equal(
            "01FF3412123478563412123456780000803F3F8000000807060504030201",
            Convert.ToHexString(writer.WrittenMemory.Span));
        Assert.Throws<InvalidDataException>(() => writer.WriteByte(0));
    }

    [Fact]
    public void ReaderAndWriterRejectOversizedBuffersBeforeUse()
    {
        Assert.Throws<InvalidDataException>(() => new BinaryDataReader(new byte[5], maxBytes: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryDataWriter(maxBytes: 4, initialCapacity: 5));
    }

    [Fact]
    public void StreamReaderStartsAtCurrentPositionLeavesStreamOpenAndEnforcesLimit()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        stream.Position = 1;

        var reader = BinaryDataReader.FromStream(stream, maxBytes: 3);

        Assert.Equal(new byte[] { 2, 3, 4 }, reader.ReadMemory(3).ToArray());
        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => BinaryDataReader.FromStream(stream, maxBytes: 3));
    }
}
