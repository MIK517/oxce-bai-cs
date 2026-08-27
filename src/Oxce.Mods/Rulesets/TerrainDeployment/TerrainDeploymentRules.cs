using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.TerrainDeployment;

public sealed record MapBlockRule(
    string Name, int Width, int Length, int Height, IReadOnlyList<int> Groups,
    IReadOnlyList<int> RevealedFloors, YamlNode? Items, YamlNode? FuseTimers,
    YamlNode? RandomizedItems, YamlNode? ExtendedItems, IReadOnlyList<int> CraftInventoryTile);

public sealed record TerrainRule(
    IReadOnlyList<string> MapDataSets, IReadOnlyList<MapBlockRule> MapBlocks,
    string EnviroEffects, IReadOnlyList<string> CivilianTypes, IReadOnlyList<string> Music,
    int MinimumDepth, int MaximumDepth, RuleIndexReference Ambience, float AmbientVolume,
    IReadOnlyList<RuleIndexReference> RandomAmbience, int MinimumAmbienceDelay,
    int MaximumAmbienceDelay, string MapScript, IReadOnlyList<string> MapScripts);

public enum MapScriptCommandType
{
    AddBlock, AddLine, AddCraft, AddUfo, DigTunnel, FillArea, CheckBlock, RemoveBlock, Resize,
}

public enum MapDirection { None, Vertical, Horizontal, Both }

public sealed record MapRectangle(int X, int Y, int Width, int Height);

public sealed record MapScriptCommand(
    MapScriptCommandType Type, IReadOnlyList<MapRectangle> Rectangles, IReadOnlyList<int> CraftGroups,
    IReadOnlyList<int> Conditionals, IReadOnlyList<int> Size, IReadOnlyList<int> Groups,
    IReadOnlyList<int> Blocks, IReadOnlyList<int> Frequencies, IReadOnlyList<int> MaximumUses,
    MapDirection Direction, int VerticalGroup, int HorizontalGroup, int CrossingGroup,
    bool CanBeSkipped, bool MarkAsReinforcementsBlock, int ExecutionChances, int Executions,
    string UfoName, string CraftName, IReadOnlyList<string> RandomTerrain, int Label,
    YamlNode? TunnelData, YamlNode? VerticalLevels);

public sealed record MapScriptRule(
    string Id, IReadOnlyList<MapScriptCommand> Commands, RuleOperationSource LastUpdateSource);

public sealed record McdPatchEntry(
    ulong Index, IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans, IReadOnlyList<int>? Lofts);

public sealed record McdPatchRule(
    string Id, IReadOnlyList<McdPatchEntry> Entries, RuleOperationSource LastUpdateSource);

public sealed record AlienRaceRule(
    string BaseCustomDeploy, string BaseCustomMission, IReadOnlyList<string> Members,
    IReadOnlyList<IReadOnlyList<string>> RandomMembers, int RetaliationAggression,
    IReadOnlyList<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> RetaliationMissionWeights,
    int ListOrder);

public sealed record EnvironmentalConditionRule(
    int GlobalChance, int ChancePerTurn, int FirstTurn, int LastTurn, string Message,
    int Color, string WeaponOrAmmo, int Side, int BodyPart);

public sealed record EnviroEffectsRule(
    IReadOnlyDictionary<string, EnvironmentalConditionRule> EnvironmentalConditions,
    IReadOnlyDictionary<string, string> PaletteTransformations,
    IReadOnlyDictionary<string, string> ArmorTransformations,
    int MapBackgroundColor, bool IgnoreAutoNightVisionUserSetting,
    string InventoryShockIndicator, string MapShockIndicator);

public sealed record StartingConditionRule(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> DefaultArmor,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NameCollections,
    IReadOnlyDictionary<string, int> RequiredItems,
    IReadOnlyDictionary<string, string> CraftTransformations,
    bool DestroyRequiredItems, bool RequireCommanderOnboard);

public sealed record AlienDeploymentRule(
    IReadOnlyDictionary<string, string> Strings, IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans, IReadOnlyList<string> Terrains,
    IReadOnlyList<string> RandomRaces, IReadOnlyList<string> MapScripts,
    IReadOnlyList<string> Music, IReadOnlyDictionary<string, int> CiviliansByType,
    IReadOnlyList<int> Depth, IReadOnlyList<int> Duration,
    IReadOnlyList<YamlNode> DeploymentData, IReadOnlyList<YamlNode> Reinforcements,
    YamlNode? Briefing, YamlNode? SuccessEvents, YamlNode? DespawnEvents,
    YamlNode? FailureEvents, YamlNode? GenMission,
    IReadOnlyList<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> HuntMissionWeights,
    IReadOnlyList<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> AlienBaseUpgrades,
    IReadOnlyList<YamlNode> AlienRaceEvolution);

internal static class TerrainReadOnly
{
    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(source));
}

internal sealed class TerrainBuilder(string id)
{
    public string Id { get; } = id;
    public List<string> MapDataSets { get; set; } = [];
    public List<MapBlockRule> MapBlocks { get; } = [];
    public string EnviroEffects { get; set; } = "";
    public List<string> CivilianTypes { get; } = ["MALE_CIVILIAN", "FEMALE_CIVILIAN"];
    public List<string> Music { get; } = [];
    public int MinimumDepth { get; set; }
    public int MaximumDepth { get; set; }
    public RuleIndexReference Ambience { get; set; } = new(-1, "");
    public float AmbientVolume { get; set; } = 0.5f;
    public List<RuleIndexReference> RandomAmbience { get; set; } = [];
    public int MinimumAmbienceDelay { get; set; } = 20;
    public int MaximumAmbienceDelay { get; set; } = 60;
    public string MapScript { get; set; } = "DEFAULT";
    public List<string> MapScripts { get; set; } = [];
}

internal sealed class AlienRaceBuilder(string id, int listOrder)
{
    public string Id { get; } = id;
    public string BaseCustomDeploy { get; set; } = "";
    public string BaseCustomMission { get; set; } = "";
    public List<string> Members { get; set; } = [];
    public List<List<string>> RandomMembers { get; set; } = [];
    public int RetaliationAggression { get; set; }
    public List<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> RetaliationWeights { get; } = [];
    public int ListOrder { get; set; } = listOrder;
}

internal sealed class EnviroEffectsBuilder
{
    public Dictionary<string, EnvironmentalConditionRule> Conditions { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> PaletteTransformations { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ArmorTransformations { get; } = new(StringComparer.Ordinal);
    public int MapBackgroundColor { get; set; } = 15;
    public bool IgnoreNightVision { get; set; }
    public string InventoryShockIndicator { get; set; } = "";
    public string MapShockIndicator { get; set; } = "";
}

internal sealed class StartingConditionBuilder
{
    public Dictionary<string, Dictionary<string, int>> DefaultArmor { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> Collections { get; } = StartingConditionCollectionKeys.ToDictionary(
        key => key, _ => new List<string>(), StringComparer.Ordinal);
    public Dictionary<string, int> RequiredItems { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CraftTransformations { get; } = new(StringComparer.Ordinal);
    public bool DestroyRequiredItems { get; set; }
    public bool RequireCommanderOnboard { get; set; }
    public static readonly string[] StartingConditionCollectionKeys =
    [
        "allowedArmors", "forbiddenArmors", "forbiddenArmorsInNextStage", "allowedVehicles",
        "forbiddenVehicles", "allowedItems", "forbiddenItems", "allowedItemCategories",
        "forbiddenItemCategories", "allowedCraft", "forbiddenCraft", "allowedSoldierTypes",
        "forbiddenSoldierTypes",
    ];
}

internal sealed class AlienDeploymentBuilder
{
    public Dictionary<string, string> Strings { get; } = DeploymentDefaults.Strings();
    public Dictionary<string, int> Integers { get; } = DeploymentDefaults.Integers();
    public Dictionary<string, bool> Booleans { get; } = DeploymentDefaults.Booleans();
    public List<string> Terrains { get; set; } = [];
    public List<string> RandomRaces { get; set; } = [];
    public List<string> MapScripts { get; set; } = [];
    public List<string> Music { get; set; } = [];
    public Dictionary<string, int> CiviliansByType { get; } = new(StringComparer.Ordinal);
    public List<int> Depth { get; set; } = [0, 0];
    public List<int> Duration { get; set; } = [0, 0];
    public List<YamlNode> Data { get; set; } = [];
    public List<YamlNode> Reinforcements { get; set; } = [];
    public YamlNode? Briefing { get; set; }
    public YamlNode? SuccessEvents { get; set; }
    public YamlNode? DespawnEvents { get; set; }
    public YamlNode? FailureEvents { get; set; }
    public YamlNode? GenMission { get; set; }
    public List<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> HuntMissionWeights { get; } = [];
    public List<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> AlienBaseUpgrades { get; } = [];
    public List<YamlNode> AlienRaceEvolution { get; set; } = [];
}

internal static class DeploymentDefaults
{
    public static Dictionary<string, string> Strings() => new(StringComparer.Ordinal)
    {
        ["customUfo"] = "",
        ["enviroEffects"] = "",
        ["startingCondition"] = "",
        ["unlockedResearch"] = "",
        ["unlockedResearchOnFailure"] = "",
        ["unlockedResearchOnDespawn"] = "",
        ["counterSuccess"] = "",
        ["counterFailure"] = "",
        ["counterDespawn"] = "",
        ["counterAll"] = "",
        ["decreaseCounterSuccess"] = "",
        ["decreaseCounterFailure"] = "",
        ["decreaseCounterDespawn"] = "",
        ["decreaseCounterAll"] = "",
        ["missionBountyItem"] = "",
        ["nextStage"] = "",
        ["race"] = "",
        ["script"] = "DEFAULT",
        ["winCutscene"] = "",
        ["loseCutscene"] = "",
        ["abortCutscene"] = "",
        ["alert"] = "STR_ALIENS_TERRORISE",
        ["alertBackground"] = "BACK03.SCR",
        ["alertDescription"] = "",
        ["alienBaseDiscoveredMessage"] = "",
        ["markerName"] = "STR_TERROR_SITE",
        ["objectivePopup"] = "",
        ["missionCompleteText"] = "",
        ["missionFailedText"] = "",
        ["baseSelfDestructCode"] = "",
        ["upgradeRace"] = "",
    };
    public static Dictionary<string, int> Integers() => new(StringComparer.Ordinal)
    {
        ["missionBountyItemCount"] = 1,
        ["bughuntMinTurn"] = 0,
        ["width"] = 0,
        ["length"] = 0,
        ["height"] = 0,
        ["civilians"] = 0,
        ["minBrutalAggression"] = 0,
        ["civilianSpawnNodeRank"] = 0,
        ["shade"] = -1,
        ["minShade"] = -1,
        ["maxShade"] = -1,
        ["markerIcon"] = -1,
        ["objectiveType"] = -1,
        ["objectivesRequired"] = 0,
        ["despawnPenalty"] = 0,
        ["abortPenalty"] = 0,
        ["points"] = 0,
        ["cheatTurn"] = 20,
        ["turnLimit"] = 0,
        ["chronoTrigger"] = 0,
        ["fakeUnderwaterSpawnChance"] = 0,
        ["escapeType"] = 0,
        ["vipSurvivalPercentage"] = 0,
        ["genMissionFreq"] = 0,
        ["genMissionLimit"] = 1000,
        ["baseDetectionRange"] = 0,
        ["baseDetectionChance"] = 100,
        ["huntMissionMaxFrequency"] = 60,
    };
    public static Dictionary<string, bool> Booleans() => new(StringComparer.Ordinal)
    {
        ["forcePercentageOutsideUfo"] = false,
        ["ignoreLivingCivilians"] = false,
        ["markCiviliansAsVIP"] = false,
        ["finalDestination"] = false,
        ["alienBase"] = false,
        ["isHidden"] = false,
        ["keepCraftAfterFailedMission"] = false,
        ["allowObjectiveRecovery"] = false,
        ["genMissionRaceFromAlienBase"] = true,
        ["huntMissionRaceFromAlienBase"] = true,
        ["resetAlienBaseAgeAfterUpgrade"] = false,
        ["resetAlienBaseAge"] = false,
        ["noWeaponPile"] = false,
    };
}
