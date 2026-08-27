using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.Items;

public sealed class ItemScalarValues
{
    internal ItemScalarValues(ItemRuleBuilder builder)
    {
        Strings = ReadOnly(builder.Strings);
        NullableNames = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(builder.NullableNames, StringComparer.Ordinal));
        Integers = ReadOnly(builder.Integers);
        Reals = ReadOnly(builder.Reals);
        Booleans = ReadOnly(builder.Booleans);
    }

    public IReadOnlyDictionary<string, string> Strings { get; }
    public IReadOnlyDictionary<string, string?> NullableNames { get; }
    public IReadOnlyDictionary<string, int> Integers { get; }
    public IReadOnlyDictionary<string, double> Reals { get; }
    public IReadOnlyDictionary<string, bool> Booleans { get; }

    public string GetString(string key) => Strings[key];
    public string? NullableName(string key) => NullableNames[key];
    public int GetInteger(string key) => Integers[key];
    public double Real(string key) => Reals[key];
    public bool Boolean(string key) => Booleans[key];

    private static ReadOnlyDictionary<string, T> ReadOnly<T>(Dictionary<string, T> values) =>
        new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(values, StringComparer.Ordinal));
}

public sealed record ItemUseValues<T>(T Time, T Energy, T Morale, T Health, T Stun, T Mana);

public sealed record ItemActionRule(
    int Accuracy,
    int Range,
    int Shots,
    int SpendPerShot,
    bool FollowProjectiles,
    int AmmoSlot,
    int AmmoZombieUnitChanceOverride,
    int AmmoSpawnUnitChanceOverride,
    int AmmoSpawnItemChanceOverride,
    ItemUseValues<int?> Cost,
    ItemUseValues<bool?> Flat,
    bool Arcing,
    string Name,
    string ShortName);

public sealed record ItemFuseTriggerRule(
    bool DefaultBehavior,
    bool ThrowTrigger,
    bool ThrowExplode,
    bool ProximityTrigger,
    bool ProximityExplode);

public sealed record ItemDamageRule(
    int? PredefinedType,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, double> Reals,
    IReadOnlyDictionary<string, bool> Booleans);

public sealed record ItemRule(
    ItemScalarValues Values,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> BuyRequirements,
    IReadOnlyList<string> RequiredBuyBaseFunctions,
    IReadOnlyList<string> Categories,
    IReadOnlyDictionary<string, int> RecoveryDividers,
    IReadOnlyDictionary<string, IReadOnlyList<int>> RecoveryTransformations,
    IReadOnlyList<IReadOnlyList<string>> CompatibleAmmo,
    IReadOnlyList<int> TimeUnitsToLoad,
    IReadOnlyList<int> TimeUnitsToUnload,
    IReadOnlyList<string> SupportedInventorySections,
    IReadOnlyDictionary<string, string> ZombieUnitsByMaleArmor,
    IReadOnlyDictionary<string, string> ZombieUnitsByFemaleArmor,
    IReadOnlyDictionary<string, string> ZombieUnitsByType,
    IReadOnlyDictionary<string, RuleIndexReference> ResourceIndexes,
    IReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>> ResourceIndexLists,
    IReadOnlyDictionary<string, ItemActionRule> Actions,
    IReadOnlyDictionary<string, ItemUseValues<int?>> UseCosts,
    IReadOnlyDictionary<string, ItemUseValues<bool?>> UseFlats,
    ItemFuseTriggerRule FuseTriggers,
    ItemDamageRule Damage,
    ItemDamageRule MeleeDamage)
{
    public int EffectiveLoadOrder => Values.GetInteger("loadOrder") <= 0
        ? Values.GetInteger("listOrder")
        : Values.GetInteger("loadOrder");
}

internal sealed class ItemRuleBuilder
{
    public ItemRuleBuilder(string id, int listOrder)
    {
        Strings = ItemRuleSchema.CreateStrings(id);
        NullableNames = ItemRuleSchema.CreateNullableNames();
        Integers = ItemRuleSchema.CreateIntegers(listOrder);
        Reals = ItemRuleSchema.CreateReals();
        Booleans = ItemRuleSchema.CreateBooleans();
        Actions = new Dictionary<string, ItemActionBuilder>(StringComparer.Ordinal)
        {
            ["Aimed"] = ItemActionBuilder.Aimed(),
            ["Auto"] = ItemActionBuilder.Auto(),
            ["Snap"] = ItemActionBuilder.Snap(),
            ["Melee"] = ItemActionBuilder.Melee(),
        };
        ResourceIndexes = ItemRuleSchema.CreateResourceIndexes();
        ResourceIndexLists = ItemRuleSchema.CreateResourceIndexLists();
        UseCosts = new Dictionary<string, ItemUseValuesBuilder<int?>>(StringComparer.Ordinal)
        {
            ["Use"] = new(25, 0),
            ["MindControl"] = new(null, null),
            ["Panic"] = new(null, null),
            ["Throw"] = new(25, 0),
            ["Prime"] = new(50, 0),
            ["Unprime"] = new(25, 0),
        };
        UseFlats = new Dictionary<string, ItemUseValuesBuilder<bool?>>(StringComparer.Ordinal)
        {
            ["Use"] = new(false, true),
            ["Throw"] = new(false, true),
            ["Prime"] = new(false, true),
            ["Unprime"] = new(false, true),
        };
    }

    public Dictionary<string, string> Strings { get; }
    public Dictionary<string, string?> NullableNames { get; }
    public Dictionary<string, int> Integers { get; }
    public Dictionary<string, double> Reals { get; }
    public Dictionary<string, bool> Booleans { get; }
    public List<string> Requirements { get; } = [];
    public List<string> BuyRequirements { get; } = [];
    public List<string> RequiredBuyBaseFunctions { get; } = [];
    public List<string> Categories { get; } = [];
    public Dictionary<string, int> RecoveryDividers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<int>> RecoveryTransformations { get; } = new(StringComparer.Ordinal);
    public List<string>[] CompatibleAmmo { get; } = [[], [], [], []];
    public int[] TimeUnitsToLoad { get; } = [15, 15, 15, 15];
    public int[] TimeUnitsToUnload { get; } = [8, 8, 8, 8];
    public List<string> SupportedInventorySections { get; } = [];
    public Dictionary<string, string> ZombieUnitsByMaleArmor { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ZombieUnitsByFemaleArmor { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ZombieUnitsByType { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RuleIndexReference> ResourceIndexes { get; }
    public Dictionary<string, List<RuleIndexReference>> ResourceIndexLists { get; }
    public Dictionary<string, ItemActionBuilder> Actions { get; }
    public Dictionary<string, ItemUseValuesBuilder<int?>> UseCosts { get; }
    public Dictionary<string, ItemUseValuesBuilder<bool?>> UseFlats { get; }
    public ItemFuseTriggerBuilder FuseTriggers { get; } = new();
    public ItemDamageBuilder Damage { get; } = new();
    public ItemDamageBuilder MeleeDamage { get; } = new();
}

internal sealed class ItemActionBuilder
{
    public int Accuracy { get; set; }
    public int Range { get; set; }
    public int Shots { get; set; } = 1;
    public int SpendPerShot { get; set; } = 1;
    public bool FollowProjectiles { get; set; } = true;
    public int AmmoSlot { get; set; }
    public int AmmoZombieUnitChanceOverride { get; set; } = -1;
    public int AmmoSpawnUnitChanceOverride { get; set; } = -1;
    public int AmmoSpawnItemChanceOverride { get; set; } = -1;
    public ItemUseValuesBuilder<int?> Cost { get; private init; } = new(0, 0);
    public ItemUseValuesBuilder<bool?> Flat { get; private init; } = new(false, false);
    public bool Arcing { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;

    public static ItemActionBuilder Aimed() => new()
    {
        Range = 200,
        Name = "STR_AIMED_SHOT",
        Flat = new ItemUseValuesBuilder<bool?>(null, null),
    };
    public static ItemActionBuilder Auto() => new()
    {
        Range = 7,
        Shots = 3,
        Name = "STR_AUTO_SHOT",
        Cost = new ItemUseValuesBuilder<int?>(0, null),
        Flat = new ItemUseValuesBuilder<bool?>(null, null),
    };
    public static ItemActionBuilder Snap() => new()
    {
        Range = 15,
        Name = "STR_SNAP_SHOT",
        Cost = new ItemUseValuesBuilder<int?>(0, null),
        Flat = new ItemUseValuesBuilder<bool?>(null, null),
    };
    public static ItemActionBuilder Melee() => new() { Flat = new ItemUseValuesBuilder<bool?>(null, null) };
}

internal sealed class ItemUseValuesBuilder<T>(T time, T rest)
{
    public T Time { get; set; } = time;
    public T Energy { get; set; } = rest;
    public T Morale { get; set; } = rest;
    public T Health { get; set; } = rest;
    public T Stun { get; set; } = rest;
    public T Mana { get; set; } = rest;
    public ItemUseValues<T> Freeze() => new(Time, Energy, Morale, Health, Stun, Mana);
}

internal sealed class ItemFuseTriggerBuilder
{
    public bool DefaultBehavior { get; set; } = true;
    public bool ThrowTrigger { get; set; }
    public bool ThrowExplode { get; set; }
    public bool ProximityTrigger { get; set; }
    public bool ProximityExplode { get; set; }
}

internal sealed class ItemDamageBuilder
{
    public ItemDamageBuilder()
    {
        Integers["FixRadius"] = 0;
        Integers["RandomType"] = 8;
        Integers["ResistType"] = 0;
        Integers["ArmorIgnore"] = 0;
        Integers["FireThreshold"] = 1000;
        Integers["SmokeThreshold"] = 1000;
        Integers["RandomWoundType"] = 0;
        Integers["TileDamageMethod"] = 1;
        Integers["TileDamageLimit"] = -1;
        Reals["ArmorEffectiveness"] = 1;
        Reals["RadiusEffectiveness"] = 0;
        Reals["RadiusReduction"] = 10;
        Reals["ToHealth"] = 1;
        Reals["ToMana"] = 0;
        Reals["ToArmor"] = 0.1;
        Reals["ToArmorPre"] = 0;
        Reals["ToWound"] = 1;
        Reals["ToItem"] = 0;
        Reals["ToTile"] = 0.5;
        Reals["ToStun"] = 0.25;
        Reals["ToEnergy"] = 0;
        Reals["ToTime"] = 0;
        Reals["ToMorale"] = 0;
        foreach (var key in new[]
        {
            "FireBlastCalc", "IgnoreDirection", "IgnoreSelfDestruct", "IgnorePainImmunity",
            "IgnoreNormalMoraleLose", "IgnoreOverKill", "RandomHealth", "RandomMana", "RandomArmor",
            "RandomArmorPre", "RandomItem", "RandomTile", "RandomEnergy", "RandomTime", "RandomMorale",
        }) Booleans[key] = false;
        Booleans["RandomWound"] = true;
        Booleans["RandomStun"] = true;
    }

    public int? PredefinedType { get; set; }
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Reals { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal);

    public void ResetForPredefinedType(int type)
    {
        PredefinedType = type;
        Integers.Clear();
        Reals.Clear();
        Booleans.Clear();
    }
}

internal static class ItemRuleSchema
{
    public static readonly IReadOnlyDictionary<string, string> StringDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ufopediaType"] = "",
        ["name"] = "",
        ["nameAsAmmo"] = "",
        ["requiresBuyCountry"] = "",
        ["monthlyBuyLimitMessage"] = "",
        ["painKillerActionName"] = "STR_PAIN_KILLER",
        ["stimulantActionName"] = "STR_STIMULANT",
        ["healActionName"] = "STR_HEAL",
        ["medikitActionName"] = "STR_USE_MEDI_KIT",
        ["psiAttackName"] = "",
        ["primeActionName"] = "STR_PRIME_GRENADE",
        ["unprimeActionName"] = "",
        ["primeActionMessage"] = "STR_GRENADE_IS_ACTIVATED",
        ["unprimeActionMessage"] = "STR_GRENADE_IS_DEACTIVATED",
        ["sellActionMessage"] = "",
        ["medikitBackground"] = "",
    };

    public static readonly IReadOnlyDictionary<string, int> IntegerDefaults = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["vehicleFixedAmmoSlot"] = 0,
        ["monthlyBuyLimit"] = 0,
        ["costBuy"] = 0,
        ["costSell"] = 0,
        ["transferTime"] = 24,
        ["weight"] = 3,
        ["throwRange"] = 200,
        ["underwaterThrowRange"] = 200,
        ["throwDropoffRange"] = 99,
        ["underwaterThrowDropoffRange"] = 99,
        ["throwDropoff"] = 5,
        ["hitAnimFrames"] = -1,
        ["hitMissAnimFrames"] = -1,
        ["meleeAnimFrames"] = -1,
        ["meleeMissAnimFrames"] = -1,
        ["psiAnimFrames"] = -1,
        ["psiMissAnimFrames"] = -1,
        ["power"] = 0,
        ["powerForAnimation"] = 0,
        ["accuracyUse"] = 0,
        ["accuracyMindControl"] = 0,
        ["accuracyPanic"] = 20,
        ["accuracyThrow"] = 100,
        ["accuracyCloseQuarters"] = -1,
        ["noLOSAccuracyPenalty"] = -1,
        ["explodeInventory"] = -1,
        ["clipSize"] = 0,
        ["specialChance"] = 100,
        ["battleType"] = 0,
        ["fuseType"] = -3,
        ["inventoryMoveCostPercent"] = 100,
        ["defaultInvSlotX"] = 0,
        ["defaultInvSlotY"] = 0,
        ["waypoints"] = 0,
        ["invWidth"] = 1,
        ["invHeight"] = 1,
        ["painKiller"] = 0,
        ["heal"] = 0,
        ["stimulant"] = 0,
        ["woundRecovery"] = 0,
        ["healthRecovery"] = 0,
        ["stunRecovery"] = 0,
        ["energyRecovery"] = 0,
        ["manaRecovery"] = 0,
        ["moraleRecovery"] = 0,
        ["medikitType"] = 0,
        ["medikitTargetMatrix"] = 63,
        ["recoveryPoints"] = 0,
        ["armor"] = 20,
        ["turretType"] = -1,
        ["aiUseDelay"] = -1,
        ["aiMeleeHitCount"] = 25,
        ["prisonType"] = 0,
        ["attraction"] = 0,
        ["experienceTrainingMode"] = 0,
        ["manaExperience"] = 0,
        ["loadOrder"] = 0,
        ["listOrder"] = 0,
        ["maxRange"] = 200,
        ["minRange"] = 0,
        ["dropoff"] = 2,
        ["bulletSpeed"] = 0,
        ["explosionSpeed"] = 0,
        ["shotgunPellets"] = 0,
        ["shotgunBehavior"] = 0,
        ["shotgunSpread"] = 100,
        ["shotgunChoke"] = 100,
        ["spawnUnitFaction"] = -1,
        ["zombieUnitFaction"] = 1,
        ["spawnUnitChance"] = -1,
        ["zombieUnitChance"] = -1,
        ["spawnItemChance"] = -1,
        ["targetMatrix"] = 7,
        ["meleePower"] = 0,
        ["specialType"] = -1,
        ["vaporDensity"] = 0,
        ["vaporProbability"] = 15,
        ["vaporDensitySurface"] = 0,
        ["vaporProbabilitySurface"] = 15,
        ["kneelBonus"] = -1,
        ["oneHandedPenalty"] = -1,
        ["monthlySalary"] = 0,
        ["monthlyMaintenance"] = 0,
        ["sprayWaypoints"] = 0,
    };

    public static readonly IReadOnlyDictionary<string, double> RealDefaults = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["size"] = 0,
        ["painKillerRecovery"] = 1,
        ["powerRangeReduction"] = 0,
        ["powerRangeThreshold"] = 0,
    };

    public static readonly IReadOnlyDictionary<string, bool> BooleanDefaults = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["hidePower"] = false,
        ["ignoreAmmoPower"] = false,
        ["hiddenOnMinimap"] = false,
        ["twoHanded"] = false,
        ["blockBothHands"] = false,
        ["fixedWeapon"] = false,
        ["fixedWeaponShow"] = false,
        ["isConsumable"] = false,
        ["isFireExtinguisher"] = false,
        ["isAmmoRechargeable"] = false,
        ["specialUseEmptyHand"] = false,
        ["specialUseEmptyHandShow"] = false,
        ["medikitTargetSelf"] = false,
        ["medikitTargetImmune"] = false,
        ["recover"] = true,
        ["recoverCorpse"] = true,
        ["ignoreInBaseDefense"] = false,
        ["ignoreInCraftEquip"] = true,
        ["liveAlien"] = false,
        ["arcingShot"] = false,
        ["convertToCivilian"] = false,
        ["LOSRequired"] = false,
        ["underwaterOnly"] = false,
        ["landOnly"] = false,
        ["psiRequired"] = false,
        ["manaRequired"] = false,
    };

    public static Dictionary<string, string> CreateStrings(string id)
    {
        var result = new Dictionary<string, string>(StringDefaults, StringComparer.Ordinal) { ["name"] = id };
        return result;
    }

    public static Dictionary<string, string?> CreateNullableNames() => new(StringComparer.Ordinal)
    {
        ["defaultInventorySlot"] = null,
        ["zombieUnit"] = null,
        ["spawnUnit"] = null,
        ["spawnItem"] = null,
    };

    public static Dictionary<string, int> CreateIntegers(int listOrder)
    {
        var result = new Dictionary<string, int>(IntegerDefaults, StringComparer.Ordinal) { ["listOrder"] = listOrder };
        return result;
    }

    public static Dictionary<string, double> CreateReals() => new(RealDefaults, StringComparer.Ordinal);
    public static Dictionary<string, bool> CreateBooleans() => new(BooleanDefaults, StringComparer.Ordinal);

    public static Dictionary<string, RuleIndexReference> CreateResourceIndexes() => new(StringComparer.Ordinal)
    {
        ["bigSprite"] = new(-1, ""),
        ["floorSprite"] = new(-1, ""),
        ["handSprite"] = new(120, ""),
        ["bulletSprite"] = new(-1, ""),
        ["specialIconSprite"] = new(-1, ""),
        ["hitAnimation"] = new(0, ""),
        ["hitMissAnimation"] = new(-1, ""),
        ["meleeAnimation"] = new(0, ""),
        ["meleeMissAnimation"] = new(-1, ""),
        ["psiAnimation"] = new(-1, ""),
        ["psiMissAnimation"] = new(-1, ""),
        ["vaporColor"] = new(-1, ""),
        ["vaporColorSurface"] = new(-1, ""),
    };

    public static Dictionary<string, List<RuleIndexReference>> CreateResourceIndexLists() => new(StringComparer.Ordinal)
    {
        ["reloadSound"] = [],
        ["primeSound"] = [],
        ["unprimeSound"] = [],
        ["fireSound"] = [],
        ["hitSound"] = [],
        ["hitMissSound"] = [],
        ["meleeSound"] = [],
        ["meleeHitSound"] = [],
        ["meleeMissSound"] = [],
        ["psiSound"] = [],
        ["psiMissSound"] = [],
        ["explosionHitSound"] = [],
        ["customItemPreviewIndex"] = [new RuleIndexReference(-1, "")],
    };
}
