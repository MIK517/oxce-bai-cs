using System.Buffers.Binary;
using Oxce.Formats.Audio;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class GmCatMusicCodecTests
{
    [Fact]
    public void DecodeEntryWritesStandardFormatOneMidiStructure()
    {
        var midi = GmCatMusicCodec.DecodeEntry(BuildEntry(
            tempo: 120,
            subsequences: [],
            tracks: [(0, new byte[] { 0, 0xC0, 0, 0, 0x90, 60, 100, 0, 0xFF })]));

        Assert.True(midi.AsSpan().StartsWith("MThd"u8));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32BigEndian(midi.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(8)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(10)));
        Assert.Equal(24, BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(12)));
        Assert.Equal(2, CountMarker(midi, "MTrk"u8));
        Assert.True(midi.AsSpan().EndsWith(new byte[] { 0, 0xFF, 0x2F, 0 }));
    }

    [Fact]
    public void DecodeEntryExpandsSubsequencesAndRequiresExplicitStatusAfterward()
    {
        var subsequence = new byte[] { 0, 0xC0, 1, 0, 0xFD };
        var valid = BuildEntry(120, [subsequence], [(0, new byte[] { 0, 0xFE, 0, 0, 0x90, 60, 1, 0, 0xFF })]);
        var invalid = BuildEntry(120, [subsequence], [(0, new byte[] { 0, 0xFE, 0, 0, 60, 1 })]);

        var midi = GmCatMusicCodec.DecodeEntry(valid);

        Assert.True(midi.AsSpan().IndexOf(new byte[] { 0, 0xC0, 1, 0, 0x90, 60 }) >= 0);
        Assert.Throws<InvalidDataException>(() => GmCatMusicCodec.DecodeEntry(invalid));
    }

    [Fact]
    public void DecodeEntryRejectsRecursiveSubsequenceExpansion()
    {
        var recursive = new byte[] { 0, 0xFE, 0 };
        var entry = BuildEntry(120, [recursive], [(0, recursive)]);

        var exception = Assert.Throws<InvalidDataException>(() => GmCatMusicCodec.DecodeEntry(entry));

        Assert.Contains("recursion limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeEntryEnforcesOutputLimit()
    {
        var entry = BuildEntry(120, [], [(0, new byte[] { 0, 0xFF })]);

        Assert.Throws<InvalidDataException>(() => GmCatMusicCodec.DecodeEntry(entry, maxMidiBytes: 25));
    }

    [Theory]
    [MemberData(nameof(MalformedEntries))]
    public void DecodeEntryRejectsMalformedStreams(byte[] entry)
    {
        Assert.Throws<InvalidDataException>(() => GmCatMusicCodec.DecodeEntry(entry));
    }

    public static TheoryData<byte[]> MalformedEntries => new()
    {
        Array.Empty<byte>(),
        new byte[] { 2, 0 },
        new byte[] { 0 },
        new byte[] { 0, 0, 0, 0 },
        new byte[] { 0, 120, 1, 3, 0, 0, 0 },
        new byte[] { 0, 120, 1, 8, 0, 0, 0, 0 },
        BuildEntry(120, [], [(16, new byte[] { 0, 0xFF })]),
        BuildEntry(120, [], [(0, new byte[] { 0, 60, 1 })]),
        BuildEntry(120, [], [(0, new byte[] { 0, 0xFE, 0 })]),
        BuildEntry(120, [], [(0, new byte[] { 0, 0x90, 60, 0x80 })]),
        BuildEntry(120, [], [(0, new byte[] { 0x81, 0x81, 0x81, 0x81, 0 })]),
        Append(BuildEntry(120, [], []), 0xAA),
    };

    private static byte[] BuildEntry(
        byte tempo,
        IReadOnlyList<byte[]> subsequences,
        IReadOnlyList<(byte Channel, byte[] Sequence)> tracks)
    {
        using var output = new MemoryStream();
        output.WriteByte(0);
        output.WriteByte(tempo);
        output.WriteByte(checked((byte)subsequences.Count));
        foreach (var sequence in subsequences)
        {
            WriteSequence(output, sequence);
        }

        output.WriteByte(checked((byte)tracks.Count));
        foreach (var (channel, sequence) in tracks)
        {
            output.WriteByte(channel);
            WriteSequence(output, sequence);
        }

        return output.ToArray();
    }

    private static void WriteSequence(Stream output, byte[] sequence)
    {
        Span<byte> size = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(size, checked((uint)(sequence.Length + sizeof(uint))));
        output.Write(size);
        output.Write(sequence);
    }

    private static byte[] Append(byte[] data, byte value) => [.. data, value];

    private static int CountMarker(ReadOnlySpan<byte> data, ReadOnlySpan<byte> marker)
    {
        var count = 0;
        for (var offset = 0; offset <= data.Length - marker.Length; offset++)
        {
            if (data.Slice(offset, marker.Length).SequenceEqual(marker))
            {
                count++;
            }
        }

        return count;
    }
}
