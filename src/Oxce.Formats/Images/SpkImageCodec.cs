using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class SpkImageCodec
{
    private const ushort TransparentRun = ushort.MaxValue;
    private const ushort LiteralRun = ushort.MaxValue - 1;

    public static void Decode(BinaryDataReader input, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(input);
        long outputPosition = 0;
        while (!input.IsAtEnd)
        {
            if (input.Remaining < sizeof(ushort))
            {
                throw new InvalidDataException("SPK input ends with an incomplete command word.");
            }

            var command = input.ReadUInt16LittleEndian();
            if (command is not TransparentRun and not LiteralRun)
            {
                continue;
            }

            if (input.Remaining < sizeof(ushort))
            {
                throw new InvalidDataException("SPK run command is missing its length word.");
            }

            var pixelCount = checked((int)input.ReadUInt16LittleEndian() * 2);
            if (command == TransparentRun)
            {
                ClearVisible(destination, outputPosition, pixelCount);
            }
            else
            {
                if (input.Remaining < pixelCount)
                {
                    throw new InvalidDataException(
                        $"SPK literal run declares {pixelCount} pixels with only {input.Remaining} byte(s) remaining.");
                }

                CopyVisible(input.ReadMemory(pixelCount).Span, destination, outputPosition);
            }

            outputPosition = checked(outputPosition + pixelCount);
        }
    }

    private static void ClearVisible(Span<byte> destination, long outputPosition, int pixelCount)
    {
        if (outputPosition >= destination.Length)
        {
            return;
        }

        var visibleCount = Math.Min(pixelCount, destination.Length - (int)outputPosition);
        destination.Slice((int)outputPosition, visibleCount).Clear();
    }

    private static void CopyVisible(ReadOnlySpan<byte> source, Span<byte> destination, long outputPosition)
    {
        if (outputPosition >= destination.Length)
        {
            return;
        }

        var visibleCount = Math.Min(source.Length, destination.Length - (int)outputPosition);
        source[..visibleCount].CopyTo(destination[(int)outputPosition..]);
    }
}
