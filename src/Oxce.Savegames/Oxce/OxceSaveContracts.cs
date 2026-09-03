using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Savegames.Oxce;

public sealed record OxceSaveLoadOptions(
    string MasterId,
    IReadOnlySet<string> ActiveMods,
    YamlReadOptions? Yaml = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MasterId);
        ArgumentNullException.ThrowIfNull(ActiveMods);
        if (!ActiveMods.Contains(MasterId))
            throw new ArgumentException("The active mod set must contain its master.", nameof(ActiveMods));
    }
}

public sealed record OxceSaveWriteOptions(
    string Version = "OXCE .NET compatibility slice",
    string Engine = "Extended",
    string Build = "",
    YamlWriteOptions? Yaml = null);

public sealed class OxceSaveDocument
{
    internal OxceSaveDocument(YamlMappingNode header, YamlMappingNode body, IReadOnlyList<string> modLabels)
    {
        Header = header;
        Body = body;
        ModLabels = modLabels;
    }

    internal YamlMappingNode Header { get; }
    internal YamlMappingNode Body { get; }
    internal IReadOnlyList<string> ModLabels { get; }
}

public sealed record LoadedOxceCampaign(CampaignState Campaign, OxceSaveDocument Source);
