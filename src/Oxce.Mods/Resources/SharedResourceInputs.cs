using System.Buffers.Binary;
using Oxce.Mods.Files;

namespace Oxce.Mods.Resources;

// Keep resource resolution and cache dependency discovery on the same inventory and
// read boundary. These are the only asset bytes inspected while compiling content.
internal static class SharedResourceInputs
{
    internal static IReadOnlyList<(string SetId, string Path)> Sprites { get; } = Array.AsReadOnly(new[]
    {
        ("BIGOBS.PCK", "UNITS/BIGOBS.TAB"),
        ("FLOOROB.PCK", "UNITS/FLOOROB.TAB"),
        ("HANDOB.PCK", "UNITS/HANDOB.TAB"),
        ("SMOKE.PCK", "UFOGRAPH/SMOKE.TAB"),
        ("HIT.PCK", "UFOGRAPH/HIT.TAB"),
        ("BASEBITS.PCK", "GEOGRAPH/BASEBITS.TAB"),
        ("INTICON.PCK", "GEOGRAPH/INTICON.TAB"),
    });

    internal static IReadOnlyList<(string SetId, string Preferred, string Fallback)> Sounds { get; } =
        Array.AsReadOnly(new[]
        {
            ("GEO.CAT", "SOUND/SAMPLE.CAT", "SOUND/SOUND2.CAT"),
            ("BATTLE.CAT", "SOUND/SAMPLE2.CAT", "SOUND/SOUND1.CAT"),
        });

    internal static VirtualFileEntry? FindSound(VirtualFileCatalog files, string preferred, string fallback) =>
        files.TryGet(preferred, out var entry) || files.TryGet(fallback, out entry) ? entry : null;

    internal static SharedResourceHeader ReadHeader(VirtualFileEntry entry)
    {
        using var stream = entry.OpenRead();
        var length = stream.Length;
        Span<byte> prefix = stackalloc byte[sizeof(uint)];
        prefix.Clear();
        stream.ReadExactly(prefix[..(int)Math.Min(length, prefix.Length)]);
        return new SharedResourceHeader(length, BinaryPrimitives.ReadUInt32LittleEndian(prefix));
    }
}

internal readonly record struct SharedResourceHeader(long Length, uint FirstWord);
