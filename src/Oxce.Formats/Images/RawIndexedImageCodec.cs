using Oxce.Formats.Binary;

namespace Oxce.Formats.Images;

public static class RawIndexedImageCodec
{
    public static void Decode(BinaryDataReader input, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(input);
        var available = input.Remaining;
        var count = Math.Min(available, destination.Length);
        input.ReadMemory(count).Span.CopyTo(destination);
        input.Skip(available - count);
    }
}
