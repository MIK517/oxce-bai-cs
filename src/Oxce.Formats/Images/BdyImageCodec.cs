using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class BdyImageCodec
{
    public static void Decode(BinaryDataReader input, Span<byte> destination, int width)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (destination.Length % width != 0)
        {
            throw new ArgumentException("The indexed destination length must be a whole number of rows.", nameof(destination));
        }

        long outputPosition = 0;
        while (!input.IsAtEnd)
        {
            var control = input.ReadByte();
            if (control >= 129)
            {
                if (input.IsAtEnd)
                {
                    throw new InvalidDataException("BDY repeat run is missing its pixel value.");
                }

                var pixelCount = 257 - control;
                var value = input.ReadByte();
                var rowRemaining = width - (int)(outputPosition % width);
                var written = Math.Min(pixelCount, rowRemaining);
                FillVisible(destination, outputPosition, written, value);
                outputPosition = checked(outputPosition + written);
            }
            else
            {
                var pixelCount = control + 1;
                if (input.Remaining < pixelCount)
                {
                    throw new InvalidDataException(
                        $"BDY literal run declares {pixelCount} pixels with only {input.Remaining} byte(s) remaining.");
                }

                var values = input.ReadMemory(pixelCount).Span;
                var rowRemaining = width - (int)(outputPosition % width);
                var written = Math.Min(pixelCount, rowRemaining);
                CopyVisible(values[..written], destination, outputPosition);
                outputPosition = checked(outputPosition + written);
            }
        }
    }

    private static void FillVisible(Span<byte> destination, long outputPosition, int count, byte value)
    {
        if (outputPosition >= destination.Length)
        {
            return;
        }

        var visibleCount = Math.Min(count, destination.Length - (int)outputPosition);
        destination.Slice((int)outputPosition, visibleCount).Fill(value);
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
