using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.PersonnelTactical;

public sealed record UnitStatsRule(IReadOnlyDictionary<string, short> Values)
{
    public short Get(string key) => Values[key];
}

public sealed record InventorySlotRule(int X, int Y);

public sealed record InventoryRule(
    int X,
    int Y,
    int Type,
    IReadOnlyList<InventorySlotRule> Slots,
    IReadOnlyDictionary<string, int> Costs,
    int ListOrder,
    int Hand);

public sealed record ArmorMoveCostRule(int TimePercent, int EnergyPercent);

public sealed record ArmorRule(
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, int?> NullableIntegers,
    IReadOnlyDictionary<string, double> Reals,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyDictionary<string, bool?> NullableBooleans,
    IReadOnlyList<string> CorpseBattle,
    IReadOnlyList<string> BuiltInWeapons,
    IReadOnlyList<string> Units,
    IReadOnlyList<int> Ranks,
    IReadOnlyList<int> Loftemps,
    IReadOnlyList<double> DamageModifiers,
    UnitStatsRule Stats,
    IReadOnlyDictionary<string, ArmorMoveCostRule> MoveCosts,
    IReadOnlyDictionary<string, RuleIndexReference> ResourceIndexes,
    IReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>> ResourceIndexLists,
    IReadOnlyDictionary<string, IReadOnlyList<int>> SpriteColors,
    IReadOnlyDictionary<int, string> LayerSpecificPrefixes,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LayerDefinitions)
{
    public int TotalSize => Integers["size"] * Integers["size"];
    public int EffectiveSpaceOccupied => Integers["spaceOccupied"] < 0 ? TotalSize : Integers["spaceOccupied"];
    public bool InfiniteSupply => Strings["storeItem"] == "STR_NONE";

    public IReadOnlyList<string> EffectiveLayers(string version)
    {
        if (!LayerDefinitions.TryGetValue(version, out var layers)) return [];
        var prefix = Strings["layersDefaultPrefix"];
        return Array.AsReadOnly(layers.Select((layer, index) =>
                layer.Length == 0 ? string.Empty : FormattableString.Invariant(
                    $"{LayerSpecificPrefixes.GetValueOrDefault(index, prefix)}__{index}__{layer}"))
            .Where(layer => layer.Length != 0)
            .ToArray());
    }
}

public sealed record SkillRule(
    int TargetMode,
    int BattleType,
    bool IsPsiRequired,
    bool CheckHandsOnly,
    bool CheckHandsOnly2,
    ItemUseValues<int?> Cost,
    ItemUseValues<bool?> Flat,
    IReadOnlyList<string> CompatibleWeapons,
    IReadOnlyList<string> RequiredBonuses);

public sealed record SoldierRule(
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBuyBaseFunctions,
    UnitStatsRule MinimumStats,
    UnitStatsRule MaximumStats,
    UnitStatsRule StatCaps,
    UnitStatsRule TrainingStatCaps,
    UnitStatsRule DogfightExperience,
    IReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>> Sounds,
    IReadOnlyDictionary<string, RuleIndexReference> Sprites,
    IReadOnlyList<string> SoldierNames,
    IReadOnlyList<YamlNode> StatStrings,
    IReadOnlyList<string> RankStrings,
    IReadOnlyList<string> Skills,
    YamlMappingNode? SpawnedSoldierTemplate);

public sealed record UnitRule(
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    bool? AvoidsFire,
    UnitStatsRule Stats,
    IReadOnlyList<IReadOnlyList<string>> BuiltInWeaponSets,
    IReadOnlyList<IReadOnlyDictionary<string, ulong>> WeightedBuiltInWeaponSets,
    IReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>> Sounds,
    RuleIndexReference MoveSound,
    YamlMappingNode? SpawnedSoldierTemplate);

public sealed record SoldierBonusRule(
    IReadOnlyDictionary<string, int> Integers,
    UnitStatsRule Stats,
    int ListOrder);

public sealed record SoldierTransformationRule(
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBaseFunctions,
    IReadOnlyList<string> AllowedSoldierTypes,
    IReadOnlyList<string> RequiredPreviousTransformations,
    IReadOnlyList<string> ForbiddenPreviousTransformations,
    IReadOnlyList<string> RemovedTransformations,
    IReadOnlyDictionary<string, int> RequiredItems,
    IReadOnlyDictionary<string, int> RequiredCommendations,
    IReadOnlyDictionary<string, UnitStatsRule> StatSets,
    IReadOnlyDictionary<string, ulong> Events);

public sealed record CommendationKillCriterion(int Threshold, IReadOnlyList<string> Values);

public sealed record CommendationRule(
    string Description,
    int Sprite,
    IReadOnlyDictionary<string, IReadOnlyList<int>> Criteria,
    IReadOnlyList<IReadOnlyList<CommendationKillCriterion>> KillCriteria,
    IReadOnlyList<string> SoldierBonusTypes,
    IReadOnlyList<string> MissionMarkers,
    IReadOnlyList<string> MissionTypes,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> Units);

internal sealed class UnitStatsBuilder(short initial = 0)
{
    public Dictionary<string, short> Values { get; } = Keys.ToDictionary(key => key, _ => initial, StringComparer.Ordinal);
    public static readonly string[] Keys =
    [
        "tu", "stamina", "health", "bravery", "reactions", "firing", "throwing", "strength",
        "psiStrength", "psiSkill", "melee", "mana",
    ];
}

internal sealed class InventoryBuilder(string id, int listOrder)
{
    public string Id { get; } = id;
    public int X { get; set; }
    public int Y { get; set; }
    public int Type { get; set; }
    public List<InventorySlotRule> Slots { get; set; } = [];
    public Dictionary<string, int> Costs { get; set; } = new(StringComparer.Ordinal);
    public int ListOrder { get; set; } = listOrder;
}

internal sealed class ArmorBuilder
{
    public ArmorBuilder(string id, int listOrder)
    {
        Id = id;
        Integers["listOrder"] = listOrder;
        for (var index = 0; index < 20; index++) DamageModifiers.Add(1);
        ResourceIndexes["moveSound"] = new(-1, string.Empty);
        ResourceIndexLists["customArmorPreviewIndex"] = [new(-1, string.Empty)];
        foreach (var key in SoundListKeys) ResourceIndexLists[key] = [];
    }

    public string Id { get; }
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    {
        ["ufopediaType"] = "",
        ["spriteSheet"] = "",
        ["spriteInv"] = "",
        ["corpseGeo"] = "",
        ["storeItem"] = "",
        ["selfDestructItem"] = "",
        ["specialWeapon"] = "",
        ["requires"] = "",
        ["requiresAward"] = "",
        ["requiresBonus"] = "",
        ["layersDefaultPrefix"] = "",
    };
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["frontArmor"] = 0,
        ["sideArmor"] = 0,
        ["leftArmorDiff"] = 0,
        ["rearArmor"] = 0,
        ["underArmor"] = 0,
        ["drawingRoutine"] = 0,
        ["movementType"] = 0,
        ["specab"] = 0,
        ["turnCost"] = 1,
        ["size"] = 1,
        ["spaceOccupied"] = -1,
        ["weight"] = 0,
        ["visibilityAtDark"] = 0,
        ["visibilityAtDay"] = 0,
        ["personalLight"] = 15,
        ["personalLightHostile"] = 0,
        ["personalLightNeutral"] = 0,
        ["camouflageAtDay"] = 0,
        ["camouflageAtDark"] = 0,
        ["antiCamouflageAtDay"] = 0,
        ["antiCamouflageAtDark"] = 0,
        ["heatVision"] = 0,
        ["visibilityThroughFire"] = 100,
        ["psiVision"] = 0,
        ["psiCamouflage"] = 0,
        ["deathFrames"] = 3,
        ["forcedTorso"] = 0,
        ["spriteFaceGroup"] = 0,
        ["spriteHairGroup"] = 0,
        ["spriteRankGroup"] = 0,
        ["spriteUtileGroup"] = 0,
        ["standHeight"] = -1,
        ["kneelHeight"] = -1,
        ["floatHeight"] = -1,
        ["meleeOriginVoxelVerticalOffset"] = 0,
        ["group"] = 0,
        ["listOrder"] = 0,
    };
    public Dictionary<string, int?> NullableIntegers { get; } = new(StringComparer.Ordinal)
    {
        ["targetWeightAsHostile"] = null,
        ["targetWeightAsHostileCivilians"] = null,
        ["targetWeightAsFriendly"] = null,
        ["targetWeightAsNeutral"] = null,
    };
    public Dictionary<string, double> Reals { get; } = new(StringComparer.Ordinal)
    {
        ["overKill"] = 0.5,
        ["meleeDodgeBackPenalty"] = 0,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["allowInv"] = true,
        ["drawBubbles"] = false,
        ["turnBeforeFirstStep"] = false,
        ["constantAnimation"] = false,
        ["alwaysVisible"] = false,
        ["isPilotArmor"] = false,
        ["allowTwoMainWeapons"] = false,
        ["instantWoundRecovery"] = false,
    };
    public Dictionary<string, bool?> NullableBooleans { get; } = new(StringComparer.Ordinal)
    {
        ["fearImmune"] = null,
        ["bleedImmune"] = null,
        ["painImmune"] = null,
        ["zombiImmune"] = null,
        ["ignoresMeleeThreat"] = null,
        ["createsMeleeThreat"] = null,
        ["allowsRunning"] = null,
        ["allowsStrafing"] = null,
        ["allowsSneaking"] = null,
        ["allowsKneeling"] = null,
        ["allowsMoving"] = true,
    };
    public List<string> CorpseBattle { get; set; } = [];
    public List<string> BuiltInWeapons { get; } = [];
    public List<string> Units { get; } = [];
    public List<int> Ranks { get; } = [];
    public List<int> Loftemps { get; set; } = [];
    public List<int> FaceColors { get; set; } = [];
    public List<int> HairColors { get; set; } = [];
    public List<int> RankColors { get; set; } = [];
    public List<int> UtileColors { get; set; } = [];
    public List<double> DamageModifiers { get; } = [];
    public UnitStatsBuilder Stats { get; } = new();
    public Dictionary<string, ArmorMoveCostRule> MoveCosts { get; } = CreateMoveCosts();
    public Dictionary<string, RuleIndexReference> ResourceIndexes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<RuleIndexReference>> ResourceIndexLists { get; } = new(StringComparer.Ordinal);
    public Dictionary<int, string> LayerSpecificPrefixes { get; set; } = [];
    public Dictionary<string, List<string>> LayerDefinitions { get; set; } = new(StringComparer.Ordinal);

    public static readonly string[] SoundListKeys =
    [
        "deathMale", "deathFemale", "selectUnitMale", "selectUnitFemale", "startMovingMale",
        "startMovingFemale", "selectWeaponMale", "selectWeaponFemale", "annoyedMale", "annoyedFemale",
    ];
    private static Dictionary<string, ArmorMoveCostRule> CreateMoveCosts() => new(StringComparer.Ordinal)
    {
        ["basePercent"] = new(100, 100),
        ["baseFlyPercent"] = new(100, 100),
        ["baseClimbPercent"] = new(100, 100),
        ["baseNormalPercent"] = new(100, 100),
        ["walkPercent"] = new(100, 50),
        ["runPercent"] = new(75, 75),
        ["strafePercent"] = new(100, 50),
        ["sneakPercent"] = new(100, 50),
        ["flyWalkPercent"] = new(100, 50),
        ["flyRunPercent"] = new(75, 75),
        ["flyStrafePercent"] = new(100, 50),
        ["flyUpPercent"] = new(100, 0),
        ["flyDownPercent"] = new(100, 0),
        ["climbUpPercent"] = new(100, 50),
        ["climbDownPercent"] = new(100, 50),
        ["gravLiftPercent"] = new(100, 0),
    };
}

internal sealed class SkillBuilder(string id)
{
    public string Id { get; } = id;
    public int TargetMode { get; set; }
    public int BattleType { get; set; }
    public bool IsPsiRequired { get; set; }
    public bool CheckHandsOnly { get; set; } = true;
    public bool CheckHandsOnly2 { get; set; }
    public ItemUseValuesBuilder<int?> Cost { get; } = new(0, 0);
    public ItemUseValuesBuilder<bool?> Flat { get; } = new(false, false);
    public List<string> CompatibleWeapons { get; } = [];
    public List<string> RequiredBonuses { get; } = [];
}

internal sealed class SoldierBuilder
{
    public SoldierBuilder(string id, int listOrder)
    {
        Id = id;
        Integers["listOrder"] = listOrder;
        foreach (var key in SoundKeys) Sounds[key] = [];
        Sprites["rankSprite"] = new(42, string.Empty);
        Sprites["rankBattleSprite"] = new(20, string.Empty);
        Sprites["rankTinySprite"] = new(0, string.Empty);
        Sprites["skillIconSprite"] = new(1, string.Empty);
    }
    public string Id { get; }
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    {
        ["requiresBuyCountry"] = "",
        ["armor"] = "",
        ["specialWeapon"] = "",
        ["armorForAvatar"] = "",
        ["monthlyBuyLimitMessage"] = "",
    };
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["group"] = 0,
        ["listOrder"] = 0,
        ["monthlyBuyLimit"] = 0,
        ["costBuy"] = 0,
        ["costSalary"] = 0,
        ["costSalarySquaddie"] = 0,
        ["costSalarySergeant"] = 0,
        ["costSalaryCaptain"] = 0,
        ["costSalaryColonel"] = 0,
        ["costSalaryCommander"] = 0,
        ["standHeight"] = 0,
        ["kneelHeight"] = 0,
        ["floatHeight"] = 0,
        ["femaleFrequency"] = 50,
        ["value"] = 20,
        ["transferTime"] = 0,
        ["moraleLossWhenKilled"] = 100,
        ["avatarOffsetX"] = 67,
        ["avatarOffsetY"] = 48,
        ["flagOffset"] = 0,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["allowPromotion"] = true,
        ["allowPiloting"] = true,
        ["showTypeInInventory"] = false,
    };
    public List<string> Requirements { get; } = [];
    public List<string> RequiredBuyBaseFunctions { get; } = [];
    public UnitStatsBuilder MinimumStats { get; } = new();
    public UnitStatsBuilder MaximumStats { get; } = new();
    public UnitStatsBuilder StatCaps { get; } = new();
    public UnitStatsBuilder TrainingStatCaps { get; } = new();
    public UnitStatsBuilder DogfightExperience { get; } = new();
    public Dictionary<string, List<RuleIndexReference>> Sounds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RuleIndexReference> Sprites { get; } = new(StringComparer.Ordinal);
    public List<string> SoldierNames { get; } = [];
    public List<YamlNode> StatStrings { get; } = [];
    public List<string> RankStrings { get; } = [];
    public List<string> Skills { get; } = [];
    public YamlMappingNode? SpawnedSoldierTemplate { get; set; }
    public static readonly string[] SoundKeys =
    [
        "deathMale", "deathFemale", "panicMale", "panicFemale", "berserkMale", "berserkFemale",
        "selectUnitMale", "selectUnitFemale", "startMovingMale", "startMovingFemale",
        "selectWeaponMale", "selectWeaponFemale", "annoyedMale", "annoyedFemale",
    ];
}

internal sealed class UnitBuilder
{
    public UnitBuilder(string id)
    {
        Id = id;
        foreach (var key in SoundKeys) Sounds[key] = [];
    }
    public string Id { get; }
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    {
        ["civilianRecoveryType"] = "",
        ["spawnedPersonName"] = "",
        ["liveAlien"] = "STR_NULL",
        ["race"] = "",
        ["rank"] = "",
        ["armor"] = "",
        ["spawnUnit"] = "",
        ["meleeWeapon"] = "",
        ["psiWeapon"] = "ALIEN_PSI_WEAPON",
    };
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["showFullNameInAlienInventory"] = -1,
        ["standHeight"] = 0,
        ["kneelHeight"] = 0,
        ["floatHeight"] = 0,
        ["value"] = 0,
        ["moraleLossWhenKilled"] = 100,
        ["intelligence"] = 0,
        ["aggression"] = 0,
        ["spotter"] = 0,
        ["sniper"] = 0,
        ["energyRecovery"] = 30,
        ["specab"] = 0,
        ["pickUpWeaponsMoreActively"] = -1,
        ["berserkChance"] = 33,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["livingWeapon"] = false,
        ["capturable"] = true,
        ["canSurrender"] = false,
        ["autoSurrender"] = false,
        ["isLeeroyJenkins"] = false,
        ["isBrutal"] = false,
        ["isNotBrutal"] = false,
        ["isCheatOnMovement"] = false,
        ["waitIfOutsideWeaponRange"] = false,
        ["vip"] = false,
        ["cosmetic"] = false,
        ["ignoredByAI"] = false,
        ["canPanic"] = true,
        ["canBeMindControlled"] = true,
    };
    public bool? AvoidsFire { get; set; }
    public UnitStatsBuilder Stats { get; } = new();
    public List<List<string>> BuiltInWeaponSets { get; set; } = [];
    public List<SortedDictionary<string, ulong>> WeightedBuiltInWeaponSets { get; } = [];
    public Dictionary<string, List<RuleIndexReference>> Sounds { get; } = new(StringComparer.Ordinal);
    public RuleIndexReference MoveSound { get; set; } = new(-1, string.Empty);
    public YamlMappingNode? SpawnedSoldierTemplate { get; set; }
    public static readonly string[] SoundKeys =
    [
        "deathSound", "panicSound", "berserkSound", "aggroSound", "selectUnitSound",
        "startMovingSound", "selectWeaponSound", "annoyedSound",
    ];
}

internal sealed class SoldierBonusBuilder(string id, int listOrder)
{
    public string Id { get; } = id;
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["visibilityAtDark"] = 0,
        ["visibilityAtDay"] = 0,
        ["psiVision"] = 0,
        ["heatVision"] = 0,
        ["visibilityThroughFire"] = 0,
        ["frontArmor"] = 0,
        ["sideArmor"] = 0,
        ["leftArmorDiff"] = 0,
        ["rearArmor"] = 0,
        ["underArmor"] = 0,
    };
    public UnitStatsBuilder Stats { get; } = new();
    public int ListOrder { get; set; } = listOrder;
}

internal sealed class TransformationBuilder
{
    public TransformationBuilder(string id, int listOrder)
    {
        Id = id;
        Integers["listOrder"] = listOrder;
        StatSets["requiredMaxStats"] = new UnitStatsBuilder(9999);
        foreach (var key in StatSetKeys.Where(key => key != "requiredMaxStats")) StatSets[key] = new();
    }
    public string Id { get; }
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    {
        ["producedItem"] = "",
        ["producedSoldierType"] = "",
        ["producedSoldierArmor"] = "",
        ["soldierBonusType"] = "",
    };
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    {
        ["listOrder"] = 0,
        ["cost"] = 0,
        ["transferTime"] = 0,
        ["recoveryTime"] = 0,
        ["minRank"] = 0,
        ["upperBoundType"] = 0,
    };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["keepSoldierArmor"] = false,
        ["createsClone"] = false,
        ["needsCorpseRecovered"] = true,
        ["allowsDeadSoldiers"] = false,
        ["allowsLiveSoldiers"] = false,
        ["allowsWoundedSoldiers"] = false,
        ["includeBonusesForMinStats"] = false,
        ["includeBonusesForMaxStats"] = false,
        ["showMinMax"] = false,
        ["lowerBoundAtMinStats"] = true,
        ["upperBoundAtMaxStats"] = false,
        ["upperBoundAtStatCaps"] = false,
        ["reset"] = false,
        ["resetRank"] = false,
    };
    public List<string> Requirements { get; } = [];
    public List<string> RequiredBaseFunctions { get; } = [];
    public List<string> AllowedSoldierTypes { get; } = [];
    public List<string> RequiredPreviousTransformations { get; } = [];
    public List<string> ForbiddenPreviousTransformations { get; } = [];
    public List<string> RemovedTransformations { get; } = [];
    public Dictionary<string, int> RequiredItems { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> RequiredCommendations { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, UnitStatsBuilder> StatSets { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, ulong> Events { get; } = new(StringComparer.Ordinal);
    public static readonly string[] StatSetKeys =
    [
        "requiredMinStats", "requiredMaxStats", "flatOverallStatChange", "percentOverallStatChange",
        "percentGainedStatChange", "flatMin", "flatMax", "percentMin", "percentMax",
        "percentGainedMin", "percentGainedMax", "rerollStats",
    ];
}

internal sealed class CommendationBuilder(string id)
{
    public string Id { get; } = id;
    public string Description { get; set; } = string.Empty;
    public int Sprite { get; set; }
    public Dictionary<string, List<int>> Criteria { get; } = new(StringComparer.Ordinal);
    public List<List<CommendationKillCriterion>> KillCriteria { get; } = [];
    public List<string> SoldierBonusTypes { get; } = [];
    public List<string> MissionMarkers { get; } = [];
    public List<string> MissionTypes { get; } = [];
    public List<string> Requirements { get; } = [];
    public List<string> Units { get; } = [];
}

internal static class PersonnelReadOnly
{
    public static ReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
        where TKey : notnull => new(new Dictionary<TKey, TValue>(source));
}
