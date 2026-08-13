using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class PckSpriteSetCodec
{
    public const int DefaultMaximumDecodedBytes = 256 * 1024 * 1024;

    private const byte TransparentRun = 254;
    private const byte EndOfFrame = 255;

    public static IReadOnlyList<byte[]> Decode(
        BinaryDataReader image,
        BinaryDataReader? offsets,
        int width,
        int height,
        int maxDecodedBytes = DefaultMaximumDecodedBytes)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDecodedBytes);

        var frameLength = checked(width * height);
        var frameCount = offsets is null ? 1 : ReadFrameCount(offsets);
        var decodedBytes = checked((long)frameCount * frameLength);
        if (decodedBytes > maxDecodedBytes)
        {
            throw new InvalidDataException(
                $"PCK sprite set expands to {decodedBytes} bytes, exceeding the {maxDecodedBytes}-byte limit.");
        }

        var frames = new byte[frameCount][];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            frames[frameIndex] = DecodeFrame(image, frameLength, width, frameIndex);
        }

        return frames;
    }

    private static int ReadFrameCount(BinaryDataReader offsets)
    {
        var length = offsets.Remaining;
        if (length == 0)
        {
            return 0;
        }

        var entryWidth = DetermineOffsetWidth(offsets);
        if (length % entryWidth != 0)
        {
            throw new InvalidDataException(
                $"PCK TAB input length {length} is not divisible by its {entryWidth}-byte entry width.");
        }

        var frameCount = length / entryWidth;
        offsets.Skip(offsets.Remaining);
        return frameCount;
    }

    private static int DetermineOffsetWidth(BinaryDataReader offsets)
    {
        if (offsets.Remaining < sizeof(uint))
        {
            if (offsets.Remaining % sizeof(ushort) != 0)
            {
                throw new InvalidDataException("PCK TAB input is too short to contain a complete offset.");
            }

            return sizeof(ushort);
        }

        var position = offsets.Position;
        var firstWord = offsets.ReadUInt32LittleEndian();
        offsets.Seek(position);
        return firstWord == 0 ? sizeof(uint) : sizeof(ushort);
    }

    private static byte[] DecodeFrame(
        BinaryDataReader image,
        int frameLength,
        int width,
        int frameIndex)
    {
        if (image.IsAtEnd)
        {
            throw new InvalidDataException($"PCK frame {frameIndex} is missing its initial transparent-row count.");
        }

        var pixels = new byte[frameLength];
        long outputPosition = checked((long)image.ReadByte() * width);
        while (!image.IsAtEnd)
        {
            var value = image.ReadByte();
            if (value == EndOfFrame)
            {
                return pixels;
            }

            if (value == TransparentRun)
            {
                if (image.IsAtEnd)
                {
                    throw new InvalidDataException($"PCK frame {frameIndex} ends inside a transparent run.");
                }

                outputPosition = checked(outputPosition + image.ReadByte());
                continue;
            }

            if (outputPosition < frameLength)
            {
                pixels[(int)outputPosition] = value;
            }

            outputPosition = checked(outputPosition + 1);
        }

        throw new InvalidDataException($"PCK frame {frameIndex} has no end marker.");
    }
}
