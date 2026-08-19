using Oxce.Formats.Yaml;
using Oxce.Mods.Files;

namespace Oxce.Mods.Discovery;

public sealed record ModDiscoveryOptions
{
    public const int DefaultMaximumMods = 10_000;

    public int MaximumMods { get; init; } = DefaultMaximumMods;

    public DirectoryScanOptions LayerScan { get; init; } = new();

    public YamlReadOptions MetadataYaml { get; init; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumMods);
        ArgumentNullException.ThrowIfNull(LayerScan);
        ArgumentNullException.ThrowIfNull(MetadataYaml);
    }
}
