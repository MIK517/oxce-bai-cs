using System.Buffers.Binary;

namespace Oxce.Formats.Binary;

public sealed class BinaryDataReader
{
    public const int DefaultMaximumBytes = 64 * 1024 * 1024;

    private readonly ReadOnlyMemory<byte> _data;

    public BinaryDataReader(ReadOnlyMemory<byte> data, int maxBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (data.Length > maxBytes)
        {
            throw new InvalidDataException($"Binary input exceeds the {maxBytes}-byte limit.");
        }

        _data = data;
    }

    public int Length => _data.Length;

    public int Position { get; private set; }

    public int Remaining => Length - Position;

    public bool IsAtEnd => Position == Length;

    public static BinaryDataReader FromFile(
        string path,
        int maxBytes = DefaultMaximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var fullPath = Path.GetFullPath(path);
        using var input = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        if (input.Length > maxBytes)
        {
            throw new InvalidDataException($"Binary input '{fullPath}' exceeds the {maxBytes}-byte limit.");
        }

        using var output = new MemoryStream(capacity: checked((int)input.Length));
        var buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > maxBytes)
            {
                throw new InvalidDataException($"Binary input '{fullPath}' exceeds the {maxBytes}-byte limit.");
            }

            output.Write(buffer, 0, count);
        }

        return new BinaryDataReader(output.ToArray(), maxBytes);
    }

    public void Seek(int position)
    {
        if ((uint)position > (uint)Length)
        {
            throw new InvalidDataException(
                $"Binary seek position {position} is outside the 0..{Length} range.");
        }

        Position = position;
    }

    public void Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        EnsureAvailable(count);
        Position += count;
    }

    public byte ReadByte() => TakeSpan(1)[0];

    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    public ushort ReadUInt16LittleEndian() => BinaryPrimitives.ReadUInt16LittleEndian(TakeSpan(sizeof(ushort)));

    public ushort ReadUInt16BigEndian() => BinaryPrimitives.ReadUInt16BigEndian(TakeSpan(sizeof(ushort)));

    public short ReadInt16LittleEndian() => BinaryPrimitives.ReadInt16LittleEndian(TakeSpan(sizeof(short)));

    public short ReadInt16BigEndian() => BinaryPrimitives.ReadInt16BigEndian(TakeSpan(sizeof(short)));

    public uint ReadUInt32LittleEndian() => BinaryPrimitives.ReadUInt32LittleEndian(TakeSpan(sizeof(uint)));

    public uint ReadUInt32BigEndian() => BinaryPrimitives.ReadUInt32BigEndian(TakeSpan(sizeof(uint)));

    public int ReadInt32LittleEndian() => BinaryPrimitives.ReadInt32LittleEndian(TakeSpan(sizeof(int)));

    public int ReadInt32BigEndian() => BinaryPrimitives.ReadInt32BigEndian(TakeSpan(sizeof(int)));

    public ulong ReadUInt64LittleEndian() => BinaryPrimitives.ReadUInt64LittleEndian(TakeSpan(sizeof(ulong)));

    public ulong ReadUInt64BigEndian() => BinaryPrimitives.ReadUInt64BigEndian(TakeSpan(sizeof(ulong)));

    public long ReadInt64LittleEndian() => BinaryPrimitives.ReadInt64LittleEndian(TakeSpan(sizeof(long)));

    public long ReadInt64BigEndian() => BinaryPrimitives.ReadInt64BigEndian(TakeSpan(sizeof(long)));

    public float ReadSingleLittleEndian() => BinaryPrimitives.ReadSingleLittleEndian(TakeSpan(sizeof(float)));

    public float ReadSingleBigEndian() => BinaryPrimitives.ReadSingleBigEndian(TakeSpan(sizeof(float)));

    public double ReadDoubleLittleEndian() => BinaryPrimitives.ReadDoubleLittleEndian(TakeSpan(sizeof(double)));

    public double ReadDoubleBigEndian() => BinaryPrimitives.ReadDoubleBigEndian(TakeSpan(sizeof(double)));

    public ReadOnlyMemory<byte> ReadMemory(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        EnsureAvailable(count);
        var result = _data.Slice(Position, count);
        Position += count;
        return result;
    }

    public BinaryDataReader ReadSubReader(int count)
    {
        var data = ReadMemory(count);
        return new BinaryDataReader(data, count);
    }

    public void RequireEnd()
    {
        if (!IsAtEnd)
        {
            throw new InvalidDataException(
                $"Binary input has {Remaining} unread byte(s) at offset {Position}.");
        }
    }

    private ReadOnlySpan<byte> TakeSpan(int count)
    {
        EnsureAvailable(count);
        var result = _data.Span.Slice(Position, count);
        Position += count;
        return result;
    }

    private void EnsureAvailable(int count)
    {
        if (count > Remaining)
        {
            throw new InvalidDataException(
                $"Binary read of {count} byte(s) at offset {Position} exceeds the {Length}-byte input.");
        }
    }
}
