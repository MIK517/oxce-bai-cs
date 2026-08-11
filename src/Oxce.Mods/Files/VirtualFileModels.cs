namespace Oxce.Mods.Files;

public sealed record VirtualFileProvenance
{
    public VirtualFileProvenance(string layerId, string? modId, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        if (modId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        }

        LayerId = layerId;
        ModId = modId;
        Origin = origin;
    }

    public string LayerId { get; }

    public string? ModId { get; }

    public string Origin { get; }
}

public sealed record VirtualFileSource
{
    public VirtualFileSource(string relativePath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        RelativePath = relativePath;
        SourcePath = sourcePath;
    }

    public string RelativePath { get; }

    public string SourcePath { get; }
}

public sealed record VirtualFileEntry
{
    internal VirtualFileEntry(
        string canonicalPath,
        string sourcePath,
        VirtualFileProvenance provenance)
    {
        CanonicalPath = canonicalPath;
        SourcePath = sourcePath;
        Provenance = provenance;
    }

    public string CanonicalPath { get; }

    public string SourcePath { get; }

    public VirtualFileProvenance Provenance { get; }

    public Stream OpenRead() => new FileStream(
        SourcePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 81920,
        FileOptions.SequentialScan);
}

public sealed class DirectoryScanOptions
{
    public const int DefaultMaximumFiles = 100_000;
    public const int DefaultMaximumDepth = 64;
    public const int DefaultMaximumRelativePathLength = 4096;

    public int MaximumFiles { get; init; } = DefaultMaximumFiles;

    public int MaximumDepth { get; init; } = DefaultMaximumDepth;

    public int MaximumRelativePathLength { get; init; } = DefaultMaximumRelativePathLength;

    public bool IgnoreRulesets { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRelativePathLength);
    }
}
