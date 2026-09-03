using Oxce.Formats.Yaml;
using Oxce.Mods.Files;

namespace Oxce.Mods.Discovery;

public sealed record ModDiscoveryOptions
{
    public const int DefaultMaximumMods = 10_000;
    public const int DefaultMaximumArchives = 1_000;

    public int MaximumMods { get; init; } = DefaultMaximumMods;

    public int MaximumArchives { get; init; } = DefaultMaximumArchives;

    public DirectoryScanOptions LayerScan { get; init; } = new();

    public ZipArchiveScanOptions ArchiveScan { get; init; } = new();

    public IReadOnlyList<string> ExternalResourceRoots { get; init; } = Array.Empty<string>();

    public YamlReadOptions MetadataYaml { get; init; } = new();

    public CancellationToken CancellationToken { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumMods);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumArchives);
        ArgumentNullException.ThrowIfNull(LayerScan);
        ArgumentNullException.ThrowIfNull(ArchiveScan);
        ArgumentNullException.ThrowIfNull(ExternalResourceRoots);
        if (ExternalResourceRoots.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("External resource roots cannot contain empty paths.", nameof(ExternalResourceRoots));
        }

        ArgumentNullException.ThrowIfNull(MetadataYaml);
    }
}
