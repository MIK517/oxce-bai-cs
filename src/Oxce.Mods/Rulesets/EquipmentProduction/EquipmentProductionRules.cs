using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.EquipmentProduction;

public sealed record ItemCategoryRule(string ReplaceBy, bool Hidden, int ListOrder, IReadOnlyList<string> InventoryOrder);

public sealed record WeaponSetRule(IReadOnlyList<string> Weapons);

public sealed record CraftStats(IReadOnlyDictionary<string, int> Integers, double MaximumStorageSpace)
{
    public int Get(string key) => Integers[key];
}

public sealed record UfoStats(CraftStats Craft, string CraftCustomDeployment, string MissionCustomDeployment);

public sealed record CraftWeaponRule(
    string UfopediaType,
    string Tooltip,
    RuleIndexReference Sprite,
    RuleIndexReference Sound,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    string Launcher,
    string Clip,
    CraftStats Stats);

public sealed record CraftRule(
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBuyBaseFunctions,
    RuleIndexReference Sprite,
    IReadOnlyList<RuleIndexReference> SkinSprites,
    RuleIndexReference Marker,
    IReadOnlyList<RuleIndexReference> SelectSounds,
    IReadOnlyList<RuleIndexReference> TakeoffSounds,
    CraftStats Stats,
    IReadOnlyList<int> CraftInventoryTile,
    IReadOnlyList<int> Groups,
    IReadOnlyList<int> AllowedSoldierGroups,
    IReadOnlyList<int> AllowedArmorGroups,
    IReadOnlyDictionary<int, int> ArmorGroupLimits,
    IReadOnlyList<IReadOnlyList<int>> WeaponTypes,
    IReadOnlyList<string> WeaponStrings,
    IReadOnlyList<string> FixedWeapons,
    IReadOnlyList<string> RequiredPilotBonuses)
{
    public int EffectiveMaximumUnits => Integers["maxUnitsLimit"] < 0
        ? Stats.Get("soldiers")
        : Integers["maxUnitsLimit"];

    public int EffectiveMaximumVehiclesAndLargeSoldiers => Integers["maxHWPUnitsLimit"] < 0
        ? Stats.Get("vehicles")
        : Integers["maxHWPUnitsLimit"];
}

public sealed record UfoRule(
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    string Size,
    string ModSprite,
    string HitImage,
    RuleIndexReference Marker,
    RuleIndexReference LandedMarker,
    RuleIndexReference CrashedMarker,
    IReadOnlyDictionary<string, RuleIndexReference> Sounds,
    UfoStats Stats,
    IReadOnlyDictionary<string, UfoStats> RaceBonuses)
{
    public int EffectiveRadius => Integers["radius"] >= 0 ? Integers["radius"] : Size switch
    {
        "STR_VERY_SMALL" => 2,
        "STR_SMALL" => 3,
        "STR_MEDIUM_UC" => 4,
        "STR_LARGE" => 5,
        "STR_VERY_LARGE" => 6,
        _ => 2,
    };
}

public sealed record ResearchProtectedTopics(string Prerequisite, IReadOnlyList<string> Topics);

public sealed record ResearchRule(
    string Lookup,
    string Cutscene,
    string SpawnedItem,
    int SpawnedItemCount,
    string SpawnedEvent,
    IReadOnlyDictionary<string, ulong> Events,
    int Cost,
    int Points,
    IReadOnlyList<string> SpawnedItemList,
    IReadOnlyList<string> DecreaseCounters,
    IReadOnlyList<string> IncreaseCounters,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Unlocks,
    IReadOnlyList<string> Disables,
    IReadOnlyList<string> Reenables,
    IReadOnlyList<string> GetOneFree,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBaseFunctions,
    bool SequentialGetOneFree,
    IReadOnlyList<ResearchProtectedTopics> GetOneFreeProtected,
    string? NeededItem,
    bool NeedItem,
    bool DestroyItem,
    bool ReturnsItem,
    bool UnlockFinalMission,
    bool Repeatable,
    int ListOrder)
{
    public string EffectiveLookup(string id) => Lookup == id ? string.Empty : Lookup;
}

public sealed record RandomProducedItems(int Weight, IReadOnlyDictionary<string, int> Items);

public sealed record ManufactureRule(
    string Category,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBaseFunctions,
    int Space,
    int Time,
    int Cost,
    int Points,
    bool Refund,
    IReadOnlyDictionary<string, int> RequiredItems,
    IReadOnlyDictionary<string, int> ProducedItems,
    IReadOnlyList<RandomProducedItems> RandomProducedItems,
    string SpawnedPersonType,
    string SpawnedPersonName,
    YamlMappingNode? SpawnedSoldierTemplate,
    IReadOnlyList<int> TransferTimes,
    IReadOnlyDictionary<string, ulong> Events,
    int ListOrder);

public sealed record ManufactureShortcutRule(
    string StartFrom,
    IReadOnlyList<string> BreakDownItems,
    bool BreakDownRequirements,
    bool BreakDownRequiredBaseFunctions);

internal sealed class ItemCategoryBuilder(string id, int listOrder)
{
    public string Id { get; } = id;
    public string ReplaceBy { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public int ListOrder { get; set; } = listOrder;
    public List<string> InventoryOrder { get; } = [];
}

internal sealed class WeaponSetBuilder(string id)
{
    public string Id { get; } = id;
    public List<string> Weapons { get; } = [];
}

internal class CraftStatsBuilder
{
    public CraftStatsBuilder(int radarRange = 0, int radarChance = 0, int sightRange = 0,
        int maximumItems = 0, double maximumStorage = 0)
    {
        foreach (var key in IntegerKeys) Integers[key] = 0;
        Integers["radarRange"] = radarRange;
        Integers["radarChance"] = radarChance;
        Integers["sightRange"] = sightRange;
        Integers["maxItems"] = maximumItems;
        MaximumStorageSpace = maximumStorage;
    }

    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal);
    public double MaximumStorageSpace { get; set; }

    public static readonly string[] IntegerKeys =
    [
        "fuelMax", "damageMax", "speedMax", "accel", "radarRange", "radarChance", "sightRange",
        "hitBonus", "avoidBonus", "avoidBonus2", "powerBonus", "armor", "shieldCapacity",
        "shieldRecharge", "shieldRechargeInGeoscape", "shieldBleedThrough", "soldiers", "vehicles", "maxItems",
    ];
}

internal sealed class UfoStatsBuilder : CraftStatsBuilder
{
    public UfoStatsBuilder(int radarRange = 0, int sightRange = 0) : base(radarRange: radarRange, sightRange: sightRange) { }
    public string CraftCustomDeployment { get; set; } = string.Empty;
    public string MissionCustomDeployment { get; set; } = string.Empty;
}

internal sealed class CraftWeaponBuilder(string id)
{
    public string Id { get; } = id;
    public string UfopediaType { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public RuleIndexReference Sprite { get; set; } = new(-1, string.Empty);
    public RuleIndexReference Sound { get; set; } = new(-1, string.Empty);
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["damage"] = 0,
        ["shieldDamageModifier"] = 100,
        ["range"] = 0,
        ["accuracy"] = 0,
        ["reloadCautious"] = 0,
        ["reloadStandard"] = 0,
        ["reloadAggressive"] = 0,
        ["ammoMax"] = 0,
        ["rearmRate"] = 1,
        ["projectileType"] = 2,
        ["projectileSpeed"] = 0,
        ["weaponType"] = 0,
        ["tractorBeamPower"] = 0,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["unifiedDamageFormula"] = false,
        ["underwaterOnly"] = false,
        ["hidePediaInfo"] = false,
        ["bulletSaving"] = false,
    };
    public string Launcher { get; set; } = string.Empty;
    public string Clip { get; set; } = string.Empty;
    public CraftStatsBuilder Stats { get; } = new();
}

internal sealed class CraftBuilder
{
    public CraftBuilder(string id, int listOrder)
    {
        Id = id;
        Integers["listOrder"] = listOrder;
        for (var slot = 0; slot < 4; slot++) WeaponTypes.Add(Enumerable.Repeat(0, 8).ToList());
    }

    public string Id { get; }
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["hangarType"] = -1,
        ["weapons"] = 0,
        ["maxUnitsLimit"] = -1,
        ["pilots"] = 0,
        ["maxHWPUnitsLimit"] = -1,
        ["maxSmallSoldiers"] = -1,
        ["maxLargeSoldiers"] = -1,
        ["maxSmallVehicles"] = -1,
        ["maxLargeVehicles"] = -1,
        ["maxSmallUnits"] = -1,
        ["maxLargeUnits"] = -1,
        ["maxSoldiers"] = -1,
        ["maxVehicles"] = -1,
        ["monthlyBuyLimit"] = 0,
        ["costBuy"] = 0,
        ["costRent"] = 0,
        ["costSell"] = 0,
        ["repairRate"] = 1,
        ["refuelRate"] = 1,
        ["transferTime"] = 24,
        ["score"] = 0,
        ["maxSkinIndex"] = 0,
        ["missilePower"] = 0,
        ["listOrder"] = 0,
        ["maxAltitude"] = -1,
        ["shieldRechargedAtBase"] = 1000,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["onlyOneSoldierGroupAllowed"] = false,
        ["keepCraftAfterFailedMission"] = false,
        ["allowLanding"] = true,
        ["spacecraft"] = false,
        ["notifyWhenRefueled"] = false,
        ["autoPatrol"] = false,
        ["undetectable"] = false,
        ["patrolWithoutFuel"] = false,
        ["mapVisible"] = true,
        ["forceShowInMonthlyCosts"] = false,
        ["useAllStartTiles"] = false,
    };
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    {
        ["requiresBuyCountry"] = "",
        ["monthlyBuyLimitMessage"] = "",
        ["refuelItem"] = "",
        ["defaultAltitude"] = "STR_VERY_LOW",
        ["customPreview"] = "",
    };
    public List<string> Requirements { get; } = [];
    public List<string> RequiredBuyBaseFunctions { get; } = [];
    public RuleIndexReference Sprite { get; set; } = new(-1, string.Empty);
    public List<RuleIndexReference> SkinSprites { get; set; } = [];
    public RuleIndexReference Marker { get; set; } = new(-1, string.Empty);
    public List<RuleIndexReference> SelectSounds { get; set; } = [];
    public List<RuleIndexReference> TakeoffSounds { get; set; } = [];
    public CraftStatsBuilder Stats { get; } = new(672, 100, 1696, 999999, 99999);
    public List<int> CraftInventoryTile { get; set; } = [];
    public List<int> Groups { get; } = [];
    public List<int> AllowedSoldierGroups { get; } = [];
    public List<int> AllowedArmorGroups { get; } = [];
    public Dictionary<int, int> ArmorGroupLimits { get; set; } = [];
    public List<List<int>> WeaponTypes { get; } = [];
    public string[] WeaponStrings { get; } = ["STR_WEAPON_ONE", "STR_WEAPON_TWO", "", ""];
    public string[] FixedWeapons { get; } = ["", "", "", ""];
    public List<string> RequiredPilotBonuses { get; set; } = [];
}

internal sealed class UfoBuilder(string id)
{
    public string Id { get; } = id;
    public string Size { get; set; } = "STR_VERY_SMALL";
    public string ModSprite { get; set; } = string.Empty;
    public string HitImage { get; set; } = string.Empty;
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["radius"] = -1,
        ["visibility"] = 0,
        ["blobSize"] = -1,
        ["sprite"] = -1,
        ["power"] = 0,
        ["range"] = 0,
        ["score"] = 0,
        ["reload"] = 0,
        ["breakOffTime"] = 0,
        ["missionScore"] = 1,
        ["hunterKillerPercentage"] = 0,
        ["huntMode"] = 0,
        ["huntSpeed"] = 100,
        ["huntBehavior"] = 2,
        ["softlockThreshold"] = 100,
        ["missilePower"] = 0,
        ["missileStopChance"] = 0,
        ["splashdownSurvivalChance"] = 100,
        ["fakeWaterLandingChance"] = 0,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["unmanned"] = false,
        ["instaHyper"] = false,
        ["noAlert"] = false,
    };
    public RuleIndexReference Marker { get; set; } = new(-1, string.Empty);
    public RuleIndexReference LandedMarker { get; set; } = new(-1, string.Empty);
    public RuleIndexReference CrashedMarker { get; set; } = new(-1, string.Empty);
    public Dictionary<string, RuleIndexReference> Sounds { get; } = new(StringComparer.Ordinal)
    {
        ["fireSound"] = new(-1, ""),
        ["alertSound"] = new(-1, ""),
        ["huntAlertSound"] = new(-1, ""),
        ["hitSound"] = new(-1, ""),
    };
    public UfoStatsBuilder Stats { get; } = new(672, 268);
    public Dictionary<string, UfoStatsBuilder> RaceBonuses { get; } = new(StringComparer.Ordinal) { [""] = new() };
}

internal sealed class ResearchBuilder(string id, int listOrder)
{
    public string Id { get; } = id;
    public string Lookup { get; set; } = string.Empty;
    public string Cutscene { get; set; } = string.Empty;
    public string SpawnedItem { get; set; } = string.Empty;
    public int SpawnedItemCount { get; set; } = 1;
    public string SpawnedEvent { get; set; } = string.Empty;
    public SortedDictionary<string, ulong> Events { get; } = new(StringComparer.Ordinal);
    public int Cost { get; set; }
    public int Points { get; set; }
    public List<string> SpawnedItemList { get; } = [];
    public List<string> DecreaseCounters { get; } = [];
    public List<string> IncreaseCounters { get; } = [];
    public List<string> Dependencies { get; } = [];
    public List<string> Unlocks { get; } = [];
    public List<string> Disables { get; } = [];
    public List<string> Reenables { get; } = [];
    public List<string> GetOneFree { get; } = [];
    public List<string> Requirements { get; } = [];
    public List<string> RequiredBaseFunctions { get; } = [];
    public bool SequentialGetOneFree { get; set; }
    public List<ResearchProtectedBuilder> GetOneFreeProtected { get; } = [];
    public string? NeededItem { get; set; }
    public bool NeedItem { get; set; }
    public bool DestroyItem { get; set; }
    public bool ReturnsItem { get; set; }
    public bool UnlockFinalMission { get; set; }
    public bool Repeatable { get; set; }
    public int ListOrder { get; set; } = listOrder;
}

internal sealed record ResearchProtectedBuilder(string Prerequisite, List<string> Topics);

internal sealed class ManufactureBuilder
{
    public ManufactureBuilder(string id, int listOrder)
    {
        Id = id;
        ListOrder = listOrder;
        ProducedItems[id] = 1;
    }
    public string Id { get; }
    public string Category { get; set; } = string.Empty;
    public List<string> Requirements { get; } = [];
    public List<string> RequiredBaseFunctions { get; } = [];
    public int Space { get; set; }
    public int Time { get; set; }
    public int Cost { get; set; }
    public int Points { get; set; }
    public bool Refund { get; set; }
    public Dictionary<string, int> RequiredItems { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ProducedItems { get; } = new(StringComparer.Ordinal);
    public List<RandomProducedItems> RandomProducedItems { get; set; } = [];
    public string SpawnedPersonType { get; set; } = string.Empty;
    public string SpawnedPersonName { get; set; } = string.Empty;
    public YamlMappingNode? SpawnedSoldierTemplate { get; set; }
    public List<int> TransferTimes { get; set; } = [];
    public SortedDictionary<string, ulong> Events { get; } = new(StringComparer.Ordinal);
    public int ListOrder { get; set; }
}

internal sealed class ManufactureShortcutBuilder(string id)
{
    public string Id { get; } = id;
    public string StartFrom { get; set; } = string.Empty;
    public List<string> BreakDownItems { get; set; } = [];
    public bool BreakDownRequirements { get; set; }
    public bool BreakDownRequiredBaseFunctions { get; set; } = true;
}

internal static class EquipmentReadOnly
{
    public static ReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
        where TKey : notnull => new(new Dictionary<TKey, TValue>(source));
}
