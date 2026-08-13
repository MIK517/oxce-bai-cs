using System.Buffers.Binary;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Terrain;

public static class LoftempsCodec
{
    public const int DefaultMaximumValues = 16 * 1024 * 1024;

    public static LoftempsData Decode(
        BinaryDataReader input,
        int maxValues = DefaultMaximumValues)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxValues);
        var valueCount = input.Remaining / sizeof(ushort);
        if (valueCount > maxValues)
        {
            throw new InvalidDataException(
                $"LOFTEMPS contains {valueCount} complete values, exceeding the {maxValues}-value limit.");
        }

        var values = new ushort[valueCount];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16LittleEndian(input.ReadMemory(sizeof(ushort)).Span);
        }

        return new LoftempsData(values, input.ReadMemory(input.Remaining));
    }
}

public sealed class LoftempsData
{
    internal LoftempsData(ushort[] values, ReadOnlyMemory<byte> trailingData)
    {
        Values = values;
        TrailingData = trailingData;
    }

    public IReadOnlyList<ushort> Values { get; }

    public ReadOnlyMemory<byte> TrailingData { get; }
}
