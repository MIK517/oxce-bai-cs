using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class TerrainDataCodecTests
{
    [Fact]
    public void McdDecodeUsesPackedFieldOffsetsAndSignedTerrainLevel()
    {
        var bytes = Enumerable.Range(0, McdTerrainCodec.RecordSize)
            .Select(value => (byte)value)
            .Concat(new byte[] { 0xAA })
            .ToArray();
        bytes[48] = 0xFE;

        var data = McdTerrainCodec.Decode(new BinaryDataReader(bytes));

        var record = Assert.Single(data.Records);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, record.Frames.ToArray());
        Assert.Equal(0x1514, record.MiniMapIndex);
        Assert.True(record.IsUfoDoor);
        Assert.Equal(39, record.TimeUnitsWalk);
        Assert.Equal(44, record.DieMcd);
        Assert.Equal(46, record.AlternateMcd);
        Assert.Equal(-2, record.TerrainLevel);
        Assert.Equal(60, record.RawData.Span[60]);
        Assert.Equal(new byte[] { 0xAA }, data.TrailingData.ToArray());
    }

    [Fact]
    public void McdDecodeEnforcesRecordLimit()
    {
        Assert.Throws<InvalidDataException>(
            () => McdTerrainCodec.Decode(
                new BinaryDataReader(new byte[McdTerrainCodec.RecordSize * 2]),
                maxRecords: 1));
    }

    [Fact]
    public void LoftempsDecodeReadsLittleEndianValuesAndPreservesOddByte()
    {
        var data = LoftempsCodec.Decode(Reader("3412CDABEF"));

        Assert.Collection(
            data.Values,
            value => Assert.Equal(0x1234, value),
            value => Assert.Equal(0xABCD, value));
        Assert.Equal(new byte[] { 0xEF }, data.TrailingData.ToArray());
    }

    [Fact]
    public void LoftempsDecodeEnforcesValueLimit()
    {
        Assert.Throws<InvalidDataException>(
            () => LoftempsCodec.Decode(Reader("00000000"), maxValues: 1));
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));
}
