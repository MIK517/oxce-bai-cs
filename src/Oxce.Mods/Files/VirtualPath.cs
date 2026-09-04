using System.Buffers;
using System.Text;

namespace Oxce.Mods.Files;

public static class VirtualPath
{
    public const int CanonicalizationVersion = 1;

    public static string NormalizeFile(string path) => Normalize(path, allowEmpty: false, allowTrailingSlash: false);

    public static string NormalizeDirectory(string path) => Normalize(path, allowEmpty: true, allowTrailingSlash: true);

    private static string Normalize(string path, bool allowEmpty, bool allowTrailingSlash)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('\\', '/');
        ValidateRoot(normalized, nameof(path));
        if (allowTrailingSlash)
        {
            normalized = normalized.TrimEnd('/');
        }
        var containsNonAscii = Validate(normalized, allowEmpty, nameof(path));
        return CanonicalizeCase(normalized, nameof(path), containsNonAscii);
    }

    internal static string NormalizeFileSpelling(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('\\', '/');
        ValidateRoot(normalized, nameof(path));
        _ = Validate(normalized, allowEmpty: false, nameof(path));
        return normalized;
    }

    private static string CanonicalizeCase(string value, string parameterName, bool containsNonAscii)
    {
        if (!containsNonAscii)
        {
            return value.ToLowerInvariant();
        }

        var changed = false;
        var outputLength = 0;
        for (var offset = 0; offset < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(offset), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "A virtual path must contain valid Unicode scalar values.", parameterName);
            }

            var canonical = CanonicalizeRune(rune);
            changed |= canonical != rune;
            outputLength += canonical.Utf16SequenceLength;
            offset += consumed;
        }

        if (!changed)
        {
            return value;
        }

        return string.Create(outputLength, value, static (destination, source) =>
        {
            var sourceOffset = 0;
            var destinationOffset = 0;
            while (sourceOffset < source.Length)
            {
                _ = Rune.DecodeFromUtf16(source.AsSpan(sourceOffset), out var rune, out var consumed);
                var canonical = CanonicalizeRune(rune);
                destinationOffset += canonical.EncodeToUtf16(destination[destinationOffset..]);
                sourceOffset += consumed;
            }
        });
    }

    private static Rune CanonicalizeRune(Rune value)
    {
        var lower = Rune.ToLowerInvariant(value);
        return Rune.ToUpperInvariant(lower) == Rune.ToUpperInvariant(value) ? lower : value;
    }

    private static bool Validate(string normalized, bool allowEmpty, string parameterName)
    {
        if (normalized.Length == 0)
        {
            if (allowEmpty)
            {
                return false;
            }

            throw new ArgumentException("A virtual file path cannot be empty.", parameterName);
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new ArgumentException(
                    "A virtual path cannot contain empty, current, or parent segments.", parameterName);
            }

            if (segment.Contains('\0'))
            {
                throw new ArgumentException("A virtual path cannot contain a null character.", parameterName);
            }
        }

        var containsNonAscii = normalized.AsSpan().IndexOfAnyExceptInRange((char)0, (char)0x7f) >= 0;
        if (!containsNonAscii)
        {
            return false;
        }

        for (var offset = 0; offset < normalized.Length;)
        {
            var status = Rune.DecodeFromUtf16(normalized.AsSpan(offset), out _, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "A virtual path must contain valid Unicode scalar values.", parameterName);
            }
            offset += consumed;
        }
        return true;
    }

    private static void ValidateRoot(string normalized, string parameterName)
    {
        if (normalized.Length != 0 &&
            (normalized[0] == '/' || Path.IsPathRooted(normalized) || normalized.Contains(':')))
        {
            throw new ArgumentException(
                "A virtual path must be relative and cannot contain a drive or URI prefix.", parameterName);
        }
    }
}
