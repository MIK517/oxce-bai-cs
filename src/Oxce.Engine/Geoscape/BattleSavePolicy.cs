using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Geoscape;

/// <summary>Where a save the campaign or the battle asked for goes (<c>SaveGameState</c>, <c>BattlescapeState::init</c>'s turn-1 block).</summary>
public static class BattleSavePolicy
{
    public const string BattleAutosaveFile = "_autobattle_.asav";
    public const string GeoscapeAutosaveFile = "_autogeo_.asav";

    /// <summary>
    /// The file to write for a requested save, or null for none. The ironman save replaces the campaign's save; the turn-1 battle
    /// save is the ironman save for an ironman campaign, else the battle autosave when the autosave option is on.
    /// </summary>
    public static string? PathFor(CampaignSaveKind kind, bool ironmanCampaign, bool autosaveOption, string campaignSavePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(campaignSavePath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(campaignSavePath))!;
        return kind switch
        {
            CampaignSaveKind.Ironman => campaignSavePath,
            CampaignSaveKind.BattleTurnOne => ironmanCampaign ? campaignSavePath
                : autosaveOption ? Path.Combine(folder, BattleAutosaveFile) : null,
            _ => Path.Combine(folder, GeoscapeAutosaveFile),
        };
    }
}
