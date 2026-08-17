using System.Buffers.Binary;
using Oxce.Formats.Video;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class FlcDecoderTests
{
    [Fact]
    public void DecodeRejectsUnsupportedDepthAndInvalidDimensions()
    {
        Assert.Throws<InvalidDataException>(() => FlcDecoder.Decode(StreamForHeader(width: 1, height: 1, depth: 16)));
        Assert.Throws<InvalidDataException>(() => FlcDecoder.Decode(StreamForHeader(width: 0, height: 1)));
        Assert.Throws<InvalidDataException>(() => FlcDecoder.Decode(
            StreamForHeader(width: 2, height: 2),
            limits: new FlcDecoderLimits { MaximumPixels = 3 }));
    }

    [Fact]
    public void DecodeRejectsTruncatedAndUnknownRecords()
    {
        using var truncated = StreamForHeader(width: 1, height: 1);
        truncated.SetLength(127);
        Assert.Throws<InvalidDataException>(() => FlcDecoder.Decode(truncated));

        using var unknown = StreamForHeader(width: 1, height: 1);
        unknown.Position = unknown.Length;
        unknown.Write([6, 0, 0, 0, 0x34, 0x12]);
        unknown.Position = 0;
        Assert.Throws<InvalidDataException>(() => FlcDecoder.Decode(unknown));
    }

    [Fact]
    public void DecodeReadsTftdAudioBeyondDeclaredFileSize()
    {
        using var stream = StreamForHeader(width: 1, height: 1, type: FlcDecoder.FlcType);
        Span<byte> record = stackalloc byte[19];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(record[4..], 0xAAAA);
        BinaryPrimitives.WriteUInt16LittleEndian(record[8..], 8000);
        record[16] = 1;
        record[17] = 128;
        record[18] = 255;
        stream.Position = stream.Length;
        stream.Write(record);
        stream.Position = 0;
        var sink = new AudioSink();

        var summary = FlcDecoder.Decode(stream, sink);

        Assert.Equal(1, summary.AudioChunks);
        Assert.Equal(8000, sink.SampleRate);
        Assert.Equal(new byte[] { 1, 128, 255 }, sink.Samples);
    }

    private static MemoryStream StreamForHeader(
        ushort width,
        ushort height,
        ushort depth = 8,
        ushort type = FlcDecoder.FliType)
    {
        var header = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 128);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), type);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), depth);
        var stream = new MemoryStream();
        stream.Write(header);
        stream.Position = 0;
        return stream;
    }

    private sealed class AudioSink : IFlcFrameSink
    {
        public int SampleRate { get; private set; }

        public byte[] Samples { get; private set; } = [];

        public void OnFrame(FlcFrameInfo frame, ReadOnlySpan<byte> pixels, ReadOnlySpan<Oxce.Core.Graphics.Rgba32> palette)
        {
            throw new InvalidOperationException();
        }

        public void OnAudio(FlcAudioInfo audio, ReadOnlySpan<byte> unsignedPcm)
        {
            SampleRate = audio.SampleRate;
            Samples = unsignedPcm.ToArray();
        }
    }
}
