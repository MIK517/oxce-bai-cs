using Oxce.Core.Compatibility;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;

namespace Oxce.Mods.Loading;

public sealed record ModLoadGroup(ModCandidate Mod, IReadOnlyList<VirtualFileEntry> Rulesets);

public sealed class ModLoadPlan
{
    internal ModLoadPlan(IEnumerable<ModLoadGroup> groups, bool isValid, InputValidationMode validationMode)
    {
        Groups = Array.AsReadOnly(groups.ToArray());
        IsValid = isValid;
        ValidationMode = validationMode.Validate();
    }

    public IReadOnlyList<ModLoadGroup> Groups { get; }

    public bool IsValid { get; }

    /// <summary>The input validation mode applied to content built from this plan (ADR 0026).</summary>
    public InputValidationMode ValidationMode { get; }

    private VirtualFileCatalog? _virtualFiles;

    /// <summary>
    /// The plan's immutable layered file catalog, built once and shared by every build stage
    /// (indexing large installations is a measurable part of startup).
    /// </summary>
    public VirtualFileCatalog VirtualFiles =>
        LazyInitializer.EnsureInitialized(ref _virtualFiles, CreateVirtualFileCatalog);

    public VirtualFileCatalog CreateVirtualFileCatalog() => new(Groups.SelectMany(group => group.Mod.Layers), ValidationMode);
}
