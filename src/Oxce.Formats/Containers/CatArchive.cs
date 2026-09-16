using System.Buffers.Binary;
using Oxce.Core.Compatibility;
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
        int maxEntries = DefaultMaximumEntries,
        InputValidationMode validationMode = InputValidationMode.Strict)
    {
        ArgumentNullException.ThrowIfNull(input);
        var data = input.ReadMemory(input.Remaining);
        var offsets = ReadEntryOffsets(data.Span, data.Length, maxEntries, validationMode);
        var entries = new CatArchiveEntry[offsets.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var end = index + 1 < offsets.Length ? offsets[index + 1] : data.Length;
            entries[index] = new CatArchiveEntry(offsets[index], data.Slice(offsets[index], end - offsets[index]));
        }

        return new CatArchive(entries);
    }

    /// <summary>
    /// Reads the offset table the way <c>CatFile</c> does: sizes are derived from the next
    /// retained offset, and stored sizes are ignored. <paramref name="table"/> must hold at
    /// least the table itself (the first offset, rounded down to whole entries, or the whole
    /// input when that is shorter). Strict mode rejects a malformed table; compatibility mode
    /// returns no entries when the first offset is outside the input and skips entries that
    /// point outside it, as the reference does. Decreasing offsets are rejected in both modes
    /// because the reference would derive a wrapped size from them.
    /// </summary>
    public static int[] ReadEntryOffsets(
        ReadOnlySpan<byte> table,
        long length,
        int maxEntries = DefaultMaximumEntries,
        InputValidationMode validationMode = InputValidationMode.Strict)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        var strict = validationMode.Validate() == InputValidationMode.Strict;
        if (length < sizeof(uint) || table.Length < sizeof(uint))
        {
            if (strict || table.Length < Math.Min(length, sizeof(uint)))
                throw new InvalidDataException("CAT input is too short to contain its first offset.");
            return [];
        }

        var firstOffset = BinaryPrimitives.ReadUInt32LittleEndian(table);
        if (firstOffset == 0)
        {
            return [];
        }

        if (firstOffset >= length || (strict && firstOffset % 8 != 0))
        {
            if (!strict) return [];
            throw new InvalidDataException(
                $"CAT first offset {firstOffset} is not a valid 8-byte table boundary within the {length}-byte input.");
        }

        var entryCount = firstOffset / 8;
        if (entryCount > (uint)maxEntries)
        {
            throw new InvalidDataException(
                $"CAT table declares {entryCount} entries, exceeding the {maxEntries}-entry limit.");
        }

        if (table.Length < entryCount * 8)
        {
            throw new ArgumentException("The CAT table buffer is shorter than the declared table.", nameof(table));
        }

        var offsets = new List<int>((int)entryCount);
        for (var index = 0; index < (int)entryCount; index++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(table.Slice(index * 8, sizeof(uint)));
            if (offset >= length || (strict && offset < firstOffset))
            {
                if (!strict && offset >= length) continue;
                throw new InvalidDataException(
                    $"CAT entry {index} offset {offset} is outside the data region {firstOffset}..{length - 1}.");
            }

            if (offsets.Count != 0 && offset < offsets[^1])
            {
                throw new InvalidDataException(
                    $"CAT entry {index} offset {offset} precedes the previous entry offset {offsets[^1]}.");
            }

            offsets.Add(checked((int)offset));
        }

        return offsets.ToArray();
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
