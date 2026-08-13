using System.Buffers.Binary;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Terrain;

public static class McdTerrainCodec
{
    public const int RecordSize = 62;
    public const int DefaultMaximumRecords = 1_000_000;

    public static McdTerrainData Decode(
        BinaryDataReader input,
        int maxRecords = DefaultMaximumRecords)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecords);
        var recordCount = input.Remaining / RecordSize;
        if (recordCount > maxRecords)
        {
            throw new InvalidDataException(
                $"MCD contains {recordCount} complete records, exceeding the {maxRecords}-record limit.");
        }

        var records = new McdTerrainRecord[recordCount];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = new McdTerrainRecord(input.ReadMemory(RecordSize));
        }

        return new McdTerrainData(records, input.ReadMemory(input.Remaining));
    }
}

public sealed class McdTerrainData
{
    internal McdTerrainData(McdTerrainRecord[] records, ReadOnlyMemory<byte> trailingData)
    {
        Records = records;
        TrailingData = trailingData;
    }

    public IReadOnlyList<McdTerrainRecord> Records { get; }

    public ReadOnlyMemory<byte> TrailingData { get; }
}

public sealed class McdTerrainRecord
{
    private readonly ReadOnlyMemory<byte> _data;

    internal McdTerrainRecord(ReadOnlyMemory<byte> data)
    {
        if (data.Length != McdTerrainCodec.RecordSize)
        {
            throw new ArgumentException("An MCD record must contain exactly 62 bytes.", nameof(data));
        }

        _data = data;
    }

    public ReadOnlyMemory<byte> Frames => _data[..8];

    public ReadOnlyMemory<byte> LoftIds => _data.Slice(8, 12);

    public ushort MiniMapIndex => BinaryPrimitives.ReadUInt16LittleEndian(_data.Span[20..22]);

    public bool IsUfoDoor => this[30] != 0;

    public bool StopsLineOfSight => this[31] != 0;

    public bool HasNoFloor => this[32] != 0;

    public byte BigWall => this[33];

    public bool IsGravLift => this[34] != 0;

    public bool IsDoor => this[35] != 0;

    public bool BlocksFire => this[36] != 0;

    public bool BlocksSmoke => this[37] != 0;

    public byte TimeUnitsWalk => this[39];

    public byte TimeUnitsSlide => this[40];

    public byte TimeUnitsFly => this[41];

    public byte Armor => this[42];

    public byte HighExplosiveBlock => this[43];

    public byte DieMcd => this[44];

    public byte Flammable => this[45];

    public byte AlternateMcd => this[46];

    public sbyte TerrainLevel => unchecked((sbyte)this[48]);

    public byte PositionLevel => this[49];

    public byte LightBlock => this[51];

    public byte FootstepSound => this[52];

    public byte TileType => this[53];

    public byte HighExplosiveType => this[54];

    public byte HighExplosiveStrength => this[55];

    public byte SmokeBlockage => this[56];

    public byte Fuel => this[57];

    public byte LightSource => this[58];

    public byte TargetType => this[59];

    public bool IsXcomBase => this[60] != 0;

    public ReadOnlyMemory<byte> RawData => _data;

    private byte this[int index] => _data.Span[index];
}
