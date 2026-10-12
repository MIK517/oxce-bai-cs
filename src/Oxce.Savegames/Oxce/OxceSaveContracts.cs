using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Savegames.Oxce;

public sealed record OxceSaveLoadOptions(
    string MasterId,
    IReadOnlySet<string> ActiveMods,
    YamlReadOptions? Yaml = null,
    IDiagnosticSink? Diagnostics = null)
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
    YamlWriteOptions? Yaml = null)
{
    /// <summary>
    /// SavedGame globeLon/globeLat/globeZoom to write. A new save writes the reference defaults
    /// (0, 0, 0) without it; a rewritten save keeps the loaded values.
    /// </summary>
    public OxceGlobeView? GlobeView { get; init; }
}

/// <summary>SavedGame::_globeLon, _globeLat (radians) and _globeZoom: where the geoscape globe looks.</summary>
public sealed record OxceGlobeView(double Longitude, double Latitude, int Zoom);

public sealed class OxceSaveDocument
{
    internal OxceSaveDocument(YamlMappingNode header, YamlMappingNode body, IReadOnlyList<string> modLabels,
        OxceGlobeView globeView)
    {
        Header = header;
        Body = body;
        ModLabels = modLabels;
        GlobeView = globeView;
    }

    /// <summary>The loaded globe view (SavedGame::load tryRead; absent keys are 0).</summary>
    public OxceGlobeView GlobeView { get; }

    internal YamlMappingNode Header { get; }
    internal YamlMappingNode Body { get; }
    internal IReadOnlyList<string> ModLabels { get; }
}

public sealed record LoadedOxceCampaign(CampaignState Campaign, OxceSaveDocument Source);
