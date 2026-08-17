using System.Buffers.Binary;

namespace Oxce.Formats.Audio;

public static class GmCatMusicCodec
{
    public const int DefaultMaximumMidiBytes = 4 * 1024 * 1024;

    private const int MaximumExpandedCommands = 1_000_000;
    private const int MaximumSubsequenceDepth = 64;
    private const ushort MidiDivision = 24;
    private const uint MidiDeltaMask = 0x0FFFFFFF;

    private static readonly byte[] PatchVolumes =
    [
        100,100,100,100,100,90,100,100,100,100,100,90,100,100,100,100,
        100,100,85,100,100,100,100,100,100,100,100,100,90,90,110,80,
        100,100,100,90,70,100,100,100,100,100,100,100,100,100,100,100,
        100,100,90,100,100,100,100,100,100,120,100,100,100,120,100,127,
        100,100,90,100,100,100,100,100,100,95,100,100,100,100,100,100,
        100,100,100,100,100,100,100,115,100,100,100,100,100,100,100,100,
        100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,
        100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,
    ];

    public static byte[] DecodeEntry(
        ReadOnlySpan<byte> entryData,
        int maxMidiBytes = DefaultMaximumMidiBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxMidiBytes);
        var stream = ParseEntry(entryData);
        var output = new BoundedMidiWriter(maxMidiBytes);
        WriteHeader(output, stream.Tracks.Length);
        WriteTempoTrack(output, stream.Tempo);
        var budget = new CommandBudget(MaximumExpandedCommands);
        foreach (var track in stream.Tracks)
        {
            WriteTrack(output, stream, track, budget);
        }

        return output.ToArray();
    }

    private static GmStream ParseEntry(ReadOnlySpan<byte> entryData)
    {
        if (entryData.IsEmpty)
        {
            throw new InvalidDataException("GM.CAT entry is empty.");
        }

        var streamOffset = checked(entryData[0] + 1);
        if (streamOffset > entryData.Length)
        {
            throw new InvalidDataException("GM.CAT entry name is truncated.");
        }

        var cursor = new SpanCursor(entryData[streamOffset..]);
        var tempo = cursor.ReadByte("GM stream tempo");
        if (tempo == 0)
        {
            throw new InvalidDataException("GM stream tempo cannot be zero.");
        }

        var subsequences = new byte[cursor.ReadByte("GM subsequence count")][];
        for (var index = 0; index < subsequences.Length; index++)
        {
            subsequences[index] = cursor.ReadStoredSequence($"GM subsequence {index}");
        }

        var tracks = new GmTrack[cursor.ReadByte("GM track count")];
        for (var index = 0; index < tracks.Length; index++)
        {
            var channel = cursor.ReadByte($"GM track {index} channel");
            if (channel > 15)
            {
                throw new InvalidDataException($"GM track {index} channel {channel} is outside the MIDI 0..15 range.");
            }

            tracks[index] = new GmTrack(channel, cursor.ReadStoredSequence($"GM track {index}"));
        }

        cursor.RequireEnd();
        return new GmStream(tempo, subsequences, tracks);
    }

    private static void WriteHeader(BoundedMidiWriter output, int trackCount)
    {
        output.Write("MThd"u8);
        output.WriteUInt32BigEndian(6);
        output.WriteUInt16BigEndian(1);
        output.WriteUInt16BigEndian(checked((ushort)(trackCount + 1)));
        output.WriteUInt16BigEndian(MidiDivision);
    }

    private static void WriteTempoTrack(BoundedMidiWriter output, byte tempo)
    {
        output.Write("MTrk"u8);
        output.WriteUInt32BigEndian(11);
        output.WriteByte(0);
        WriteTempoEvent(output, tempo);
        output.WriteByte(0);
        WriteEndEvent(output);
    }

    private static void WriteTrack(
        BoundedMidiWriter output,
        GmStream stream,
        GmTrack track,
        CommandBudget budget)
    {
        output.Write("MTrk"u8);
        var lengthOffset = output.Length;
        output.WriteUInt32BigEndian(0);
        output.Write(
        [
            0, (byte)(0xB0 | track.Channel),
            0x78, 0, 0, 0x79, 0, 0, 0x7B, 0,
        ]);

        var status = new OutputStatus();
        WriteSequence(output, stream, track.Channel, track.Sequence, status, budget, depth: 0);
        WriteDelta(output, status.Delta);
        WriteEndEvent(output);
        output.WriteUInt32BigEndianAt(lengthOffset, checked((uint)(output.Length - lengthOffset - sizeof(uint))));
    }

    private static void WriteSequence(
        BoundedMidiWriter output,
        GmStream stream,
        byte channel,
        ReadOnlySpan<byte> sequence,
        OutputStatus status,
        CommandBudget budget,
        int depth)
    {
        if (depth > MaximumSubsequenceDepth)
        {
            throw new InvalidDataException(
                $"GM subsequence expansion exceeds the {MaximumSubsequenceDepth}-level recursion limit.");
        }

        var cursor = new SpanCursor(sequence);
        byte previousCommand = 0;
        while (!cursor.IsAtEnd)
        {
            budget.Consume();
            status.Delta = unchecked(status.Delta + cursor.ReadDelta());
            var next = cursor.PeekByte("GM command");
            byte command;
            if ((next & 0x80) != 0)
            {
                command = cursor.ReadByte("GM command");
                switch (command)
                {
                    case 0xFF:
                    case 0xFD:
                        return;
                    case 0xFE:
                        var subsequenceIndex = cursor.ReadByte("GM subsequence index");
                        if (subsequenceIndex >= stream.Subsequences.Length)
                        {
                            throw new InvalidDataException(
                                $"GM sequence references missing subsequence {subsequenceIndex}.");
                        }

                        WriteSequence(
                            output,
                            stream,
                            channel,
                            stream.Subsequences[subsequenceIndex],
                            status,
                            budget,
                            checked(depth + 1));
                        previousCommand = 0;
                        continue;
                    default:
                        command &= 0xF0;
                        previousCommand = command;
                        break;
                }
            }
            else
            {
                if (previousCommand == 0)
                {
                    throw new InvalidDataException("GM sequence uses running status without a preceding command.");
                }

                command = previousCommand;
            }

            var first = cursor.ReadDataByte("GM event data");
            switch (command)
            {
                case 0x80:
                case 0x90:
                    WriteNote(output, ref cursor, command, channel, first, status);
                    break;
                case 0xB0:
                    WriteControl(output, ref cursor, channel, first, status);
                    break;
                case 0xC0:
                    if (WriteProgram(output, channel, first, status))
                    {
                        return;
                    }

                    break;
                case 0xE0:
                    WritePitch(output, ref cursor, channel, first, status);
                    break;
                default:
                    throw new InvalidDataException($"GM sequence contains unsupported command 0x{command:X2}.");
            }
        }
    }

    private static void WriteNote(
        BoundedMidiWriter output,
        ref SpanCursor cursor,
        byte command,
        byte channel,
        byte note,
        OutputStatus status)
    {
        var velocity = cursor.ReadDataByte("GM note velocity");
        if (velocity != 0)
        {
            var scale = channel == 9 ? 80 : PatchVolumes[status.Patch];
            velocity = (byte)(velocity * scale >> 7);
        }

        WriteDelta(output, status.Delta);
        output.WriteByte((byte)(command | channel));
        output.WriteByte(note);
        output.WriteByte(velocity);
        status.Delta = 0;
    }

    private static bool WriteProgram(
        BoundedMidiWriter output,
        byte channel,
        byte program,
        OutputStatus status)
    {
        if (program == 0x7E)
        {
            return true;
        }

        status.Patch = program;
        if (program is 0x57 or 0x3F)
        {
            program = 0x3E;
        }

        WriteDelta(output, status.Delta);
        output.WriteByte((byte)(0xC0 | channel));
        output.WriteByte(program);
        status.Delta = 0;
        return false;
    }

    private static void WriteControl(
        BoundedMidiWriter output,
        ref SpanCursor cursor,
        byte channel,
        byte controller,
        OutputStatus status)
    {
        var value = cursor.ReadDataByte("GM controller value");
        if (controller == 0x7E || (controller == 0 && value == 0))
        {
            return;
        }

        WriteDelta(output, status.Delta);
        if (controller == 0)
        {
            WriteTempoEvent(output, checked((uint)(2 * value)));
        }
        else
        {
            if (controller == 0x5B)
            {
                value = 0x1E;
            }

            output.WriteByte((byte)(0xB0 | channel));
            output.WriteByte(controller);
            output.WriteByte(value);
        }

        status.Delta = 0;
    }

    private static void WritePitch(
        BoundedMidiWriter output,
        ref SpanCursor cursor,
        byte channel,
        byte first,
        OutputStatus status)
    {
        var second = cursor.ReadDataByte("GM pitch value");
        WriteDelta(output, status.Delta);
        output.WriteByte((byte)(0xE0 | channel));
        output.WriteByte(first);
        output.WriteByte(second);
        status.Delta = 0;
    }

    private static void WriteDelta(BoundedMidiWriter output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        var count = 0;
        value &= MidiDeltaMask;
        do
        {
            bytes[count++] = (byte)(value & 0x7F);
            value >>= 7;
        } while (value != 0 && count <= 3);

        while (--count != 0)
        {
            output.WriteByte((byte)(bytes[count] | 0x80));
        }

        output.WriteByte(bytes[0]);
    }

    private static void WriteTempoEvent(BoundedMidiWriter output, uint beatsPerMinute)
    {
        if (beatsPerMinute == 0)
        {
            throw new InvalidDataException("GM tempo event cannot be zero.");
        }

        var microseconds = 60_000_000u / beatsPerMinute;
        output.WriteByte(0xFF);
        output.WriteByte(0x51);
        output.WriteByte(3);
        output.WriteByte((byte)(microseconds >> 16));
        output.WriteByte((byte)(microseconds >> 8));
        output.WriteByte((byte)microseconds);
    }

    private static void WriteEndEvent(BoundedMidiWriter output)
    {
        output.WriteByte(0xFF);
        output.WriteByte(0x2F);
        output.WriteByte(0);
    }

    private sealed record GmStream(byte Tempo, byte[][] Subsequences, GmTrack[] Tracks);

    private sealed record GmTrack(byte Channel, byte[] Sequence);

    private sealed class OutputStatus
    {
        internal uint Delta { get; set; }

        internal byte Patch { get; set; }
    }

    private sealed class CommandBudget(int remaining)
    {
        private int _remaining = remaining;

        internal void Consume()
        {
            if (_remaining-- == 0)
            {
                throw new InvalidDataException(
                    $"GM sequence expansion exceeds the {MaximumExpandedCommands}-command limit.");
            }
        }
    }

    private ref struct SpanCursor
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        internal SpanCursor(ReadOnlySpan<byte> data)
        {
            _data = data;
        }

        internal bool IsAtEnd => _position == _data.Length;

        internal byte PeekByte(string field)
        {
            EnsureAvailable(1, field);
            return _data[_position];
        }

        internal byte ReadByte(string field)
        {
            EnsureAvailable(1, field);
            return _data[_position++];
        }

        internal byte ReadDataByte(string field)
        {
            var value = ReadByte(field);
            if (value > 0x7F)
            {
                throw new InvalidDataException($"{field} byte 0x{value:X2} is outside the MIDI data range.");
            }

            return value;
        }

        internal byte[] ReadStoredSequence(string field)
        {
            EnsureAvailable(sizeof(uint), $"{field} size");
            var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(_data[_position..]);
            _position += sizeof(uint);
            if (storedSize < sizeof(uint))
            {
                throw new InvalidDataException($"{field} stored size {storedSize} is smaller than its header.");
            }

            var unsignedSequenceSize = storedSize - sizeof(uint);
            if (unsignedSequenceSize > (uint)(_data.Length - _position))
            {
                throw new InvalidDataException($"{field} declares data beyond the end of the GM stream.");
            }

            var sequenceSize = (int)unsignedSequenceSize;
            var result = _data.Slice(_position, sequenceSize).ToArray();
            _position += sequenceSize;
            return result;
        }

        internal uint ReadDelta()
        {
            uint result = 0;
            for (var continuationCount = 0; ; continuationCount++)
            {
                var value = ReadByte("GM delta");
                result += (uint)(value & 0x7F);
                if ((value & 0x80) == 0)
                {
                    return result;
                }

                if (continuationCount == 3 || IsAtEnd)
                {
                    throw new InvalidDataException("GM delta exceeds four bytes or is truncated.");
                }

                result <<= 7;
            }
        }

        internal void RequireEnd()
        {
            if (!IsAtEnd)
            {
                throw new InvalidDataException($"GM stream has {_data.Length - _position} trailing byte(s).");
            }
        }

        private void EnsureAvailable(int count, string field)
        {
            if (count < 0 || count > _data.Length - _position)
            {
                throw new InvalidDataException($"{field} is truncated at offset {_position}.");
            }
        }
    }

    private sealed class BoundedMidiWriter
    {
        private readonly int _maximumBytes;
        private readonly List<byte> _data;

        internal BoundedMidiWriter(int maximumBytes)
        {
            _maximumBytes = maximumBytes;
            _data = new List<byte>(Math.Min(maximumBytes, 65_536));
        }

        internal int Length => _data.Count;

        internal void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _data.Add(value);
        }

        internal void Write(ReadOnlySpan<byte> values)
        {
            EnsureCapacity(values.Length);
            foreach (var value in values)
            {
                _data.Add(value);
            }
        }

        internal void WriteUInt16BigEndian(ushort value)
        {
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        internal void WriteUInt32BigEndian(uint value)
        {
            WriteByte((byte)(value >> 24));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        internal void WriteUInt32BigEndianAt(int offset, uint value)
        {
            if (offset < 0 || offset > _data.Count - sizeof(uint))
            {
                throw new InvalidOperationException("MIDI length patch is outside the output buffer.");
            }

            _data[offset] = (byte)(value >> 24);
            _data[offset + 1] = (byte)(value >> 16);
            _data[offset + 2] = (byte)(value >> 8);
            _data[offset + 3] = (byte)value;
        }

        internal byte[] ToArray() => _data.ToArray();

        private void EnsureCapacity(int count)
        {
            if (count > _maximumBytes - _data.Count)
            {
                throw new InvalidDataException(
                    $"Converted MIDI exceeds the {_maximumBytes}-byte output limit.");
            }
        }
    }
}
