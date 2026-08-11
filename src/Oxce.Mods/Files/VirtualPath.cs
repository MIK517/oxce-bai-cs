namespace Oxce.Mods.Files;

public static class VirtualPath
{
    public static string NormalizeFile(string path) => Normalize(path, allowEmpty: false, allowTrailingSlash: false);

    public static string NormalizeDirectory(string path) => Normalize(path, allowEmpty: true, allowTrailingSlash: true);

    private static string Normalize(string path, bool allowEmpty, bool allowTrailingSlash)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('\\', '/');
        if (normalized.Length != 0 &&
            (normalized[0] == '/' || Path.IsPathRooted(normalized) || normalized.Contains(':')))
        {
            throw new ArgumentException("A virtual path must be relative and cannot contain a drive or URI prefix.", nameof(path));
        }

        if (allowTrailingSlash)
        {
            normalized = normalized.TrimEnd('/');
        }

        if (normalized.Length == 0)
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new ArgumentException("A virtual file path cannot be empty.", nameof(path));
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new ArgumentException("A virtual path cannot contain empty, current, or parent segments.", nameof(path));
            }

            if (segment.Contains('\0'))
            {
                throw new ArgumentException("A virtual path cannot contain a null character.", nameof(path));
            }
        }

        return normalized.ToLowerInvariant();
    }
}
