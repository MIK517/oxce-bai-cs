using System.IO.Compression;

namespace Oxce.Mods.Files;

internal sealed record ZipEntryDescriptor(
    int Index,
    string EntryName,
    string CanonicalPath,
    long Length);

internal sealed class ZipArchiveIndex
{
    private ZipArchiveIndex(
        string archivePath,
        ZipEntryDescriptor[] entries,
        string[] topLevelDirectories,
        int rejectedEntryCount)
    {
        ArchivePath = archivePath;
        Entries = entries;
        TopLevelDirectories = topLevelDirectories;
        RejectedEntryCount = rejectedEntryCount;
    }

    public string ArchivePath { get; }

    public IReadOnlyList<ZipEntryDescriptor> Entries { get; }

    public IReadOnlyList<string> TopLevelDirectories { get; }

    public int RejectedEntryCount { get; }

    public static ZipArchiveIndex Read(string archivePath, ZipArchiveScanOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        options ??= new ZipArchiveScanOptions();
        options.Validate();
        var fullPath = Path.GetFullPath(archivePath);
        var archiveLength = new FileInfo(fullPath).Length;
        if (archiveLength > options.MaximumArchiveBytes)
        {
            throw new InvalidDataException(
                $"ZIP archive '{fullPath}' exceeds the {options.MaximumArchiveBytes}-byte compressed-size limit.");
        }

        using var input = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > options.MaximumEntries)
        {
            throw new InvalidDataException(
                $"ZIP archive '{fullPath}' exceeds the {options.MaximumEntries}-entry limit.");
        }

        var entries = new List<ZipEntryDescriptor>(archive.Entries.Count);
        var topLevelDirectories = new List<string>();
        var seenTopLevelDirectories = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;
        long totalLength = 0;
        for (var index = 0; index < archive.Entries.Count; ++index)
        {
            var entry = archive.Entries[index];
            if (entry.Name.Length == 0)
            {
                var directoryName = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (directoryName.Length != 0 && !directoryName.Contains('/') &&
                    TryNormalizeEntry(directoryName, options, out var canonicalDirectory) &&
                    seenTopLevelDirectories.Add(canonicalDirectory!))
                {
                    topLevelDirectories.Add(directoryName);
                }

                continue;
            }

            if (!TryNormalizeEntry(entry.FullName, options, out var canonicalPath))
            {
                ++rejected;
                continue;
            }

            if (entry.Length > options.MaximumEntryBytes)
            {
                throw new InvalidDataException(
                    $"ZIP entry '{entry.FullName}' exceeds the {options.MaximumEntryBytes}-byte expanded-size limit.");
            }

            if (entry.Length > options.MaximumExpandedBytes - totalLength)
            {
                throw new InvalidDataException(
                    $"ZIP archive '{fullPath}' exceeds the {options.MaximumExpandedBytes}-byte expanded-size limit.");
            }

            totalLength += entry.Length;

            entries.Add(new ZipEntryDescriptor(index, entry.FullName, canonicalPath!, entry.Length));
        }

        return new ZipArchiveIndex(fullPath, entries.ToArray(), topLevelDirectories.ToArray(), rejected);
    }

    public VirtualFileLayer CreateLayer(
        string prefix,
        VirtualFileProvenance provenance,
        bool ignoreRulesets = false)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(provenance);
        var canonicalPrefix = prefix.Length == 0 ? string.Empty : VirtualPath.NormalizeDirectory(prefix) + "/";
        var sources = Entries
            .Where(entry => canonicalPrefix.Length == 0 || entry.CanonicalPath.StartsWith(canonicalPrefix, StringComparison.Ordinal))
            .Select(entry =>
            {
                var relativePath = canonicalPrefix.Length == 0
                    ? entry.CanonicalPath
                    : entry.CanonicalPath[canonicalPrefix.Length..];
                return VirtualFileSource.FromZip(
                    relativePath,
                    $"{ArchivePath}!/{entry.EntryName.Replace('\\', '/')}",
                    ArchivePath,
                    entry.Index,
                    entry.EntryName);
            });
        return VirtualFileLayer.FromEntries(provenance, sources, ignoreRulesets);
    }

    public bool ContainsPrefix(string prefix)
    {
        var canonicalPrefix = VirtualPath.NormalizeDirectory(prefix) + "/";
        return Entries.Any(entry => entry.CanonicalPath.StartsWith(canonicalPrefix, StringComparison.Ordinal));
    }

    private static bool TryNormalizeEntry(
        string entryName,
        ZipArchiveScanOptions options,
        out string? canonicalPath)
    {
        canonicalPath = null;
        var normalized = entryName.Replace('\\', '/');
        if (normalized.Length == 0 || normalized.Length > options.MaximumRelativePathLength ||
            normalized[0] == '/' || Path.IsPathRooted(normalized) || normalized.Contains(':'))
        {
            return false;
        }

        var segments = normalized.Split('/');
        if (segments.Length - 1 > options.MaximumDepth ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains('\0')))
        {
            return false;
        }

        canonicalPath = normalized.ToLowerInvariant();
        return true;
    }
}

public sealed record ZipArchiveScanOptions
{
    public const int DefaultMaximumEntries = 100_000;
    public const long DefaultMaximumArchiveBytes = 4L * 1024 * 1024 * 1024;
    public const long DefaultMaximumEntryBytes = 2L * 1024 * 1024 * 1024;
    public const long DefaultMaximumExpandedBytes = 16L * 1024 * 1024 * 1024;

    public int MaximumEntries { get; init; } = DefaultMaximumEntries;

    public int MaximumDepth { get; init; } = DirectoryScanOptions.DefaultMaximumDepth;

    public int MaximumRelativePathLength { get; init; } = DirectoryScanOptions.DefaultMaximumRelativePathLength;

    public long MaximumArchiveBytes { get; init; } = DefaultMaximumArchiveBytes;

    public long MaximumEntryBytes { get; init; } = DefaultMaximumEntryBytes;

    public long MaximumExpandedBytes { get; init; } = DefaultMaximumExpandedBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRelativePathLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumExpandedBytes);
    }
}
