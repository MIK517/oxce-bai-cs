using System.Buffers;
using System.Buffers.Binary;

namespace Oxce.Formats.Binary;

public sealed class BinaryDataWriter
{
    public const int DefaultMaximumBytes = 64 * 1024 * 1024;

    private readonly ArrayBufferWriter<byte> _buffer;
    private readonly int _maxBytes;

    public BinaryDataWriter(
        int maxBytes = DefaultMaximumBytes,
        int? initialCapacity = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var capacity = initialCapacity ?? Math.Min(256, maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity > maxBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCapacity),
                capacity,
                "Initial capacity cannot exceed the maximum output size.");
        }

        _maxBytes = maxBytes;
        _buffer = new ArrayBufferWriter<byte>(capacity);
    }

    public int Length => _buffer.WrittenCount;

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.WrittenMemory;

    public void WriteByte(byte value) => TakeSpan(1)[0] = value;

    public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

    public void WriteUInt16LittleEndian(ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(TakeSpan(sizeof(ushort)), value);

    public void WriteUInt16BigEndian(ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(TakeSpan(sizeof(ushort)), value);

    public void WriteInt16LittleEndian(short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(TakeSpan(sizeof(short)), value);

    public void WriteInt16BigEndian(short value) =>
        BinaryPrimitives.WriteInt16BigEndian(TakeSpan(sizeof(short)), value);

    public void WriteUInt32LittleEndian(uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(TakeSpan(sizeof(uint)), value);

    public void WriteUInt32BigEndian(uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(TakeSpan(sizeof(uint)), value);

    public void WriteInt32LittleEndian(int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(TakeSpan(sizeof(int)), value);

    public void WriteInt32BigEndian(int value) =>
        BinaryPrimitives.WriteInt32BigEndian(TakeSpan(sizeof(int)), value);

    public void WriteUInt64LittleEndian(ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(TakeSpan(sizeof(ulong)), value);

    public void WriteUInt64BigEndian(ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(TakeSpan(sizeof(ulong)), value);

    public void WriteInt64LittleEndian(long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(TakeSpan(sizeof(long)), value);

    public void WriteInt64BigEndian(long value) =>
        BinaryPrimitives.WriteInt64BigEndian(TakeSpan(sizeof(long)), value);

    public void WriteSingleLittleEndian(float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(TakeSpan(sizeof(float)), value);

    public void WriteSingleBigEndian(float value) =>
        BinaryPrimitives.WriteSingleBigEndian(TakeSpan(sizeof(float)), value);

    public void WriteDoubleLittleEndian(double value) =>
        BinaryPrimitives.WriteDoubleLittleEndian(TakeSpan(sizeof(double)), value);

    public void WriteDoubleBigEndian(double value) =>
        BinaryPrimitives.WriteDoubleBigEndian(TakeSpan(sizeof(double)), value);

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(TakeSpan(value.Length));
    }

    public void WriteZeroes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        TakeSpan(count).Clear();
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(_buffer.WrittenSpan);
    }

    private Span<byte> TakeSpan(int count)
    {
        if (count > _maxBytes - Length)
        {
            throw new InvalidDataException($"Binary output exceeds the {_maxBytes}-byte limit.");
        }

        var result = _buffer.GetSpan(count)[..count];
        _buffer.Advance(count);
        return result;
    }
}
