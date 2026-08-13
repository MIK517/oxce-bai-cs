using System.Buffers.Binary;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Containers;

public sealed class CatArchive
{
    public const int DefaultMaximumEntries = 65_536;

    private readonly CatArchiveEntry[] _entries;

    private CatArchive(CatArchiveEntry[] entries)
    {
        _entries = entries;
    }

    public IReadOnlyList<CatArchiveEntry> Entries => _entries;

    public CatArchiveEntry this[int index] => _entries[index];

    public static CatArchive Parse(
        BinaryDataReader input,
        int maxEntries = DefaultMaximumEntries)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        var data = input.ReadMemory(input.Remaining);
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("CAT input is too short to contain its first offset.");
        }

        var firstOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        if (firstOffset == 0)
        {
            return new CatArchive([]);
        }

        if (firstOffset >= data.Length || firstOffset % 8 != 0)
        {
            throw new InvalidDataException(
                $"CAT first offset {firstOffset} is not a valid 8-byte table boundary within the {data.Length}-byte input.");
        }

        var entryCount = checked((int)(firstOffset / 8));
        if (entryCount > maxEntries)
        {
            throw new InvalidDataException(
                $"CAT table declares {entryCount} entries, exceeding the {maxEntries}-entry limit.");
        }

        var offsets = new int[entryCount];
        for (var index = 0; index < offsets.Length; index++)
        {
            var tablePosition = checked(index * 8);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.Span.Slice(tablePosition, sizeof(uint)));
            if (offset < firstOffset || offset >= data.Length)
            {
                throw new InvalidDataException(
                    $"CAT entry {index} offset {offset} is outside the data region {firstOffset}..{data.Length - 1}.");
            }

            if (index != 0 && offset < offsets[index - 1])
            {
                throw new InvalidDataException(
                    $"CAT entry {index} offset {offset} precedes entry {index - 1} offset {offsets[index - 1]}.");
            }

            offsets[index] = checked((int)offset);
        }

        var entries = new CatArchiveEntry[entryCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var end = index + 1 < offsets.Length ? offsets[index + 1] : data.Length;
            entries[index] = new CatArchiveEntry(offsets[index], data.Slice(offsets[index], end - offsets[index]));
        }

        return new CatArchive(entries);
    }
}

public sealed class CatArchiveEntry
{
    internal CatArchiveEntry(int offset, ReadOnlyMemory<byte> data)
    {
        Offset = offset;
        Data = data;
    }

    public int Offset { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public Stream OpenRead() => new MemoryStream(Data.ToArray(), writable: false);
}
