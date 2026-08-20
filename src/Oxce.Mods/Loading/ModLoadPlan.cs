using Oxce.Mods.Discovery;
using Oxce.Mods.Files;

namespace Oxce.Mods.Loading;

public sealed record ModLoadGroup(ModCandidate Mod, IReadOnlyList<VirtualFileEntry> Rulesets);

public sealed class ModLoadPlan
{
    internal ModLoadPlan(IEnumerable<ModLoadGroup> groups, bool isValid)
    {
        Groups = Array.AsReadOnly(groups.ToArray());
        IsValid = isValid;
    }

    public IReadOnlyList<ModLoadGroup> Groups { get; }

    public bool IsValid { get; }

    public VirtualFileCatalog CreateVirtualFileCatalog() => new(Groups.SelectMany(group => group.Mod.Layers));
}
