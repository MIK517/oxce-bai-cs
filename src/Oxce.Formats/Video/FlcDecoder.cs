using System.Buffers.Binary;
using Oxce.Core.Graphics;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Video;

public interface IFlcFrameSink
{
    void OnFrame(FlcFrameInfo frame, ReadOnlySpan<byte> pixels, ReadOnlySpan<Rgba32> palette);

    void OnAudio(FlcAudioInfo audio, ReadOnlySpan<byte> unsignedPcm);
}

public readonly record struct FlcHeader(
    uint DeclaredSize,
    ushort Type,
    ushort DeclaredFrameCount,
    int Width,
    int Height,
    ushort Depth,
    ushort Speed)
{
    public bool IsFli => Type == FlcDecoder.FliType;

    public bool IsFlc => Type == FlcDecoder.FlcType;
}

public readonly record struct FlcFrameInfo(int Index, ushort DelayOverride, int ChunkCount);

public readonly record struct FlcAudioInfo(int Index, int SampleRate);

public readonly record struct FlcDecodeSummary(
    FlcHeader Header,
    int DecodedFrames,
    int AudioChunks,
    int PrefixChunks,
    long BytesRead);

public sealed record FlcDecoderLimits
{
    public const long DefaultMaximumInputBytes = 128L * 1024 * 1024;

    public long MaximumInputBytes { get; init; } = DefaultMaximumInputBytes;

    public int MaximumDimension { get; init; } = 4096;

    public int MaximumPixels { get; init; } = 16 * 1024 * 1024;

    public int MaximumRecordBytes { get; init; } = 4 * 1024 * 1024;

    public int MaximumRecords { get; init; } = 1_000_000;

    public int MaximumChunksPerFrame { get; init; } = 65_535;
}

public static class FlcDecoder
{
    public const ushort FliType = 0xAF11;
    public const ushort FlcType = 0xAF12;

    private const ushort FrameType = 0xF1FA;
    private const ushort PrefixType = 0xF100;
    private const ushort AudioType = 0xAAAA;

    public static FlcDecodeSummary Decode(
        Stream input,
        IFlcFrameSink? sink = null,
        FlcDecoderLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
        {
            throw new ArgumentException("The FLI/FLC stream must be readable.", nameof(input));
        }

        limits ??= new FlcDecoderLimits();
        ValidateLimits(limits);
        var reader = new LimitedStreamReader(input, limits.MaximumInputBytes);
        var fileHeader = reader.ReadExactly(128, "FLI/FLC file header");
        var header = ParseHeader(fileHeader, input, limits);
        var pixels = new byte[checked(header.Width * header.Height)];
        var palette = Enumerable.Repeat(new Rgba32(0, 0, 0), 256).ToArray();
        var frames = 0;
        var audioChunks = 0;
        var prefixes = 0;
        var records = 0;

        while (reader.TryReadRecordHeader(out var recordSize, out var recordType))
        {
            records++;
            if (records > limits.MaximumRecords)
            {
                throw new InvalidDataException($"FLI/FLC record count exceeds the {limits.MaximumRecords}-record limit.");
            }

            switch (recordType)
            {
                case FrameType:
                    if (recordSize < 16 || recordSize > limits.MaximumRecordBytes)
                    {
                        throw new InvalidDataException($"Invalid FLI/FLC frame size {recordSize}.");
                    }

                    var frame = reader.ReadExactly(checked((int)recordSize - 6), "FLI/FLC frame");
                    frames++;
                    DecodeFrame(frame, pixels, palette, header.Width, header.Height, frames, sink, limits);
                    break;
                case PrefixType:
                    ValidateStandardRecordSize(recordSize, limits);
                    reader.SkipExactly(checked((int)recordSize - 6), "FLI/FLC prefix record");
                    prefixes++;
                    break;
                case AudioType:
                    if (recordSize > limits.MaximumRecordBytes)
                    {
                        throw new InvalidDataException($"FLI/FLC audio payload exceeds the {limits.MaximumRecordBytes}-byte record limit.");
                    }

                    var audio = reader.ReadExactly(checked((int)recordSize + 10), "TFTD FLC audio record");
                    var sampleRate = BinaryPrimitives.ReadUInt16LittleEndian(audio.AsSpan(2));
                    audioChunks++;
                    sink?.OnAudio(new FlcAudioInfo(audioChunks, sampleRate), audio.AsSpan(10));
                    break;
                default:
                    throw new InvalidDataException($"Unsupported top-level FLI/FLC record type 0x{recordType:X4}.");
            }
        }

        return new FlcDecodeSummary(header, frames, audioChunks, prefixes, reader.BytesRead);
    }

    private static FlcHeader ParseHeader(byte[] data, Stream input, FlcDecoderLimits limits)
    {
        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var header = new FlcHeader(
            declaredSize,
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16)));
        if (!header.IsFli && !header.IsFlc)
        {
            throw new InvalidDataException($"Unsupported FLI/FLC file type 0x{header.Type:X4}.");
        }

        if (header.Depth != 8)
        {
            throw new InvalidDataException($"Only 8-bit indexed FLI/FLC files are supported; depth is {header.Depth}.");
        }

        if (header.Width <= 0 || header.Height <= 0 ||
            header.Width > limits.MaximumDimension || header.Height > limits.MaximumDimension ||
            (long)header.Width * header.Height > limits.MaximumPixels)
        {
            throw new InvalidDataException($"FLI/FLC dimensions {header.Width}x{header.Height} exceed configured limits.");
        }

        if (declaredSize < 128)
        {
            throw new InvalidDataException($"Invalid declared FLI/FLC size {declaredSize}.");
        }

        if (input.CanSeek && declaredSize > input.Length - input.Position + 128)
        {
            throw new InvalidDataException("The declared FLI/FLC size exceeds the available input.");
        }

        return header;
    }

    private static void DecodeFrame(
        byte[] frame,
        byte[] pixels,
        Rgba32[] palette,
        int width,
        int height,
        int frameIndex,
        IFlcFrameSink? sink,
        FlcDecoderLimits limits)
    {
        var chunks = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        var delay = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2));
        if (chunks > limits.MaximumChunksPerFrame)
        {
            throw new InvalidDataException($"FLI/FLC frame chunk count exceeds the {limits.MaximumChunksPerFrame}-chunk limit.");
        }

        var reader = new BinaryDataReader(frame.AsMemory(10));
        for (var index = 0; index < chunks; index++)
        {
            var chunkSize = reader.ReadUInt32LittleEndian();
            var chunkType = reader.ReadUInt16LittleEndian();
            if (chunkSize < 6 || chunkSize - 6 > (uint)reader.Remaining)
            {
                throw new InvalidDataException($"Invalid FLI/FLC chunk size {chunkSize}.");
            }

            var payloadOffset = reader.Position;
            var chunk = reader.ReadSubReader(checked((int)chunkSize - 6));
            switch (chunkType)
            {
                case 0x04:
                    DecodePalette(chunk, palette, sixBit: false, cumulativeSkip: true);
                    break;
                case 0x07:
                    DecodeSs2(chunk, pixels, width, height);
                    break;
                case 0x0B:
                    DecodePalette(chunk, palette, sixBit: true, cumulativeSkip: false);
                    break;
                case 0x0C:
                    DecodeLc(chunk, pixels, width, height);
                    break;
                case 0x0D:
                    DecodeBlack(pixels, width, height);
                    break;
                case 0x0F:
                    DecodeBrun(chunk, pixels, width, height);
                    break;
                case 0x10:
                    // TFTD COPY chunks can under-report their payload by two bytes while
                    // the containing frame retains those bytes as trailing padding.
                    DecodeCopy(new BinaryDataReader(frame.AsMemory(10 + payloadOffset)), pixels);
                    break;
                case 0x12:
                    break;
                default:
                    throw new InvalidDataException($"Unsupported FLI/FLC visual chunk type 0x{chunkType:X4}.");
            }
        }

        sink?.OnFrame(new FlcFrameInfo(frameIndex, delay, chunks), pixels, palette);
    }

    private static void DecodePalette(BinaryDataReader reader, Rgba32[] palette, bool sixBit, bool cumulativeSkip)
    {
        var packetCount = reader.ReadUInt16LittleEndian();
        var previousCount = 0;
        for (var packet = 0; packet < packetCount; packet++)
        {
            var start = reader.ReadByte() + (cumulativeSkip ? previousCount : 0);
            var encodedCount = reader.ReadByte();
            var count = encodedCount == 0 ? 256 : encodedCount;
            if (start + count > palette.Length)
            {
                throw new InvalidDataException("FLI/FLC palette packet exceeds 256 entries.");
            }

            for (var index = 0; index < count; index++)
            {
                var red = reader.ReadByte();
                var green = reader.ReadByte();
                var blue = reader.ReadByte();
                if (sixBit)
                {
                    red <<= 2;
                    green <<= 2;
                    blue <<= 2;
                }

                palette[start + index] = new Rgba32(red, green, blue);
            }

            previousCount = count + (cumulativeSkip && packet + 1 < packetCount ? 1 : 0);
        }
    }

    private static void DecodeCopy(BinaryDataReader reader, byte[] pixels)
    {
        if (reader.Remaining < pixels.Length)
        {
            throw new InvalidDataException("Truncated FLI/FLC copy chunk.");
        }

        reader.ReadMemory(pixels.Length).Span.CopyTo(pixels);
    }

    private static void DecodeBrun(BinaryDataReader reader, byte[] pixels, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            _ = reader.ReadByte();
            var column = 0;
            while (column < width)
            {
                var count = reader.ReadSByte();
                if (count > 0)
                {
                    EnsureRowWrite(column, count, width, "BRUN");
                    pixels.AsSpan(row * width + column, count).Fill(reader.ReadByte());
                    column += count;
                }
                else if (count < 0)
                {
                    var literalCount = -count;
                    EnsureRowWrite(column, literalCount, width, "BRUN");
                    reader.ReadMemory(literalCount).Span.CopyTo(pixels.AsSpan(row * width + column));
                    column += literalCount;
                }
                else
                {
                    throw new InvalidDataException("A FLI/FLC BRUN packet has zero length.");
                }
            }
        }
    }

    private static void DecodeLc(BinaryDataReader reader, byte[] pixels, int width, int height)
    {
        var row = reader.ReadUInt16LittleEndian();
        var lines = reader.ReadUInt16LittleEndian();
        if (row + lines > height)
        {
            throw new InvalidDataException("FLI/FLC LC lines exceed the frame height.");
        }

        for (var line = 0; line < lines; line++, row++)
        {
            var packets = reader.ReadByte();
            var column = 0;
            for (var packet = 0; packet < packets; packet++)
            {
                column += reader.ReadByte();
                var count = reader.ReadSByte();
                if (count > 0)
                {
                    EnsureRowWrite(column, count, width, "LC");
                    reader.ReadMemory(count).Span.CopyTo(pixels.AsSpan(row * width + column));
                    column += count;
                }
                else if (count < 0)
                {
                    var repeat = -count;
                    EnsureRowWrite(column, repeat, width, "LC");
                    pixels.AsSpan(row * width + column, repeat).Fill(reader.ReadByte());
                    column += repeat;
                }
            }
        }
    }

    private static void DecodeSs2(BinaryDataReader reader, byte[] pixels, int width, int height)
    {
        var remainingLines = reader.ReadUInt16LittleEndian();
        var row = 0;
        while (remainingLines > 0)
        {
            var control = reader.ReadInt16LittleEndian();
            if ((control & 0xC000) == 0xC000)
            {
                row += -control;
                if (row > height)
                {
                    throw new InvalidDataException("FLI/FLC SS2 line skip exceeds the frame height.");
                }

                continue;
            }

            byte? lastPixel = null;
            if ((control & 0xC000) == 0x8000)
            {
                lastPixel = unchecked((byte)control);
                control = reader.ReadInt16LittleEndian();
            }

            if (control < 0 || row >= height)
            {
                throw new InvalidDataException("Invalid FLI/FLC SS2 packet control value.");
            }

            var column = 0;
            for (var packet = 0; packet < control; packet++)
            {
                column += reader.ReadByte();
                var wordCount = reader.ReadSByte();
                if (wordCount > 0)
                {
                    var byteCount = checked(wordCount * 2);
                    EnsureRowWrite(column, byteCount, width, "SS2");
                    reader.ReadMemory(byteCount).Span.CopyTo(pixels.AsSpan(row * width + column));
                    column += byteCount;
                }
                else if (wordCount < 0)
                {
                    var pairs = -wordCount;
                    var byteCount = checked(pairs * 2);
                    EnsureRowWrite(column, byteCount, width, "SS2");
                    var first = reader.ReadByte();
                    var second = reader.ReadByte();
                    for (var pair = 0; pair < pairs; pair++)
                    {
                        pixels[row * width + column++] = first;
                        pixels[row * width + column++] = second;
                    }
                }
            }

            if (lastPixel.HasValue)
            {
                pixels[(row + 1) * width - 1] = lastPixel.Value;
            }

            row++;
            remainingLines--;
        }
    }

    private static void DecodeBlack(byte[] pixels, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            // Preserve the reference implementation's height-sized memset.
            pixels.AsSpan(row * width, Math.Min(height, width)).Clear();
        }
    }

    private static void EnsureRowWrite(int column, int count, int width, string chunk)
    {
        if (column < 0 || count < 0 || column > width - count)
        {
            throw new InvalidDataException($"FLI/FLC {chunk} packet exceeds the frame row.");
        }
    }

    private static void ValidateStandardRecordSize(uint recordSize, FlcDecoderLimits limits)
    {
        if (recordSize < 6 || recordSize > limits.MaximumRecordBytes)
        {
            throw new InvalidDataException($"Invalid FLI/FLC record size {recordSize}.");
        }
    }

    private static void ValidateLimits(FlcDecoderLimits limits)
    {
        if (limits.MaximumInputBytes < 128 || limits.MaximumDimension <= 0 || limits.MaximumPixels <= 0 ||
            limits.MaximumRecordBytes < 16 || limits.MaximumRecords <= 0 || limits.MaximumChunksPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "FLI/FLC decoder limits must be positive and permit a file header.");
        }
    }

    private sealed class LimitedStreamReader(Stream stream, long maximumBytes)
    {
        public long BytesRead { get; private set; }

        public bool TryReadRecordHeader(out uint size, out ushort type)
        {
            var header = new byte[6];
            var first = stream.ReadByte();
            if (first < 0)
            {
                size = 0;
                type = 0;
                return false;
            }

            header[0] = (byte)first;
            ReadExactly(header.AsSpan(1), "FLI/FLC record header");
            BytesRead += 1;
            CheckLimit();
            size = BinaryPrimitives.ReadUInt32LittleEndian(header);
            type = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
            return true;
        }

        public byte[] ReadExactly(int count, string description)
        {
            var result = new byte[count];
            ReadExactly(result, description);
            return result;
        }

        public void SkipExactly(int count, string description)
        {
            var buffer = new byte[Math.Min(count, 81920)];
            while (count > 0)
            {
                var take = Math.Min(count, buffer.Length);
                ReadExactly(buffer.AsSpan(0, take), description);
                count -= take;
            }
        }

        private void ReadExactly(Span<byte> destination, string description)
        {
            try
            {
                stream.ReadExactly(destination);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException($"Truncated {description}.", exception);
            }

            BytesRead += destination.Length;
            CheckLimit();
        }

        private void CheckLimit()
        {
            if (BytesRead > maximumBytes)
            {
                throw new InvalidDataException($"FLI/FLC input exceeds the {maximumBytes}-byte limit.");
            }
        }
    }
}
