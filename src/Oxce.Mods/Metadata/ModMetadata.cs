namespace Oxce.Mods.Metadata;

public sealed record ModMetadata
{
    public required string Path { get; init; }

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Author { get; init; }

    public required string MasterId { get; init; }

    public required ModVersion Version { get; init; }

    public required string VersionDisplay { get; init; }

    public required bool IsMaster { get; init; }

    public required int ReservedSpace { get; init; }

    public required string RequiredExtendedEngine { get; init; }

    public required string RequiredExtendedVersion { get; init; }

    public required string ResourceConfigFile { get; init; }

    public required IReadOnlyList<string> ExternalResourceDirectories { get; init; }

    public ModVersion? RequiredMasterVersion { get; init; }

    public bool CanActivate(string activeMasterId) =>
        IsMaster || MasterId.Length == 0 || string.Equals(MasterId, activeMasterId, StringComparison.Ordinal);
}
