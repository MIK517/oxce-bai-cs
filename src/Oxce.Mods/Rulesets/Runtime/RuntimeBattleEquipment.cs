using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.Phase3;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// What the battle reads when it equips units (SavedBattleGame::initUnit, createItemForUnit, BattleUnit::addItem,
/// setSpecialWeapon): the inventory sections in Mod::getInvsList order, the item, armor, unit and soldier fields the
/// strategic families do not carry, item category inventory orders and weapon sets. Item references stay names; the
/// battle refuses a name that resolves to no item, where the reference fails at load.
/// </summary>
public sealed class RuntimeBattleEquipment
{
    private static readonly string[] AmmoActions = ["Aimed", "Auto", "Snap", "Melee"];

    private RuntimeBattleEquipment(IReadOnlyList<RuntimeBattleInventory> inventories, IReadOnlyDictionary<string, RuntimeBattleItem> items,
        IReadOnlyDictionary<string, RuntimeBattleArmor> armors, IReadOnlyDictionary<string, RuntimeBattleUnitRule> units,
        IReadOnlyDictionary<string, string> soldierSpecialWeapons, IReadOnlyDictionary<string, IReadOnlyList<string>> categoryInventoryOrders,
        IReadOnlyDictionary<string, IReadOnlyList<string>> weaponSets)
    {
        Inventories = inventories;
        Items = items;
        Armors = armors;
        Units = units;
        SoldierSpecialWeapons = soldierSpecialWeapons;
        CategoryInventoryOrders = categoryInventoryOrders;
        WeaponSets = weaponSets;
    }

    public static RuntimeBattleEquipment Empty { get; } = new(Array.AsReadOnly(Array.Empty<RuntimeBattleInventory>()),
        Map(new Dictionary<string, RuntimeBattleItem>()), Map(new Dictionary<string, RuntimeBattleArmor>()),
        Map(new Dictionary<string, RuntimeBattleUnitRule>()), Map(new Dictionary<string, string>()),
        Map(new Dictionary<string, IReadOnlyList<string>>()), Map(new Dictionary<string, IReadOnlyList<string>>()));

    /// <summary>Mod::getInvsList: the inventory sections by list order (ties by ID, where std::sort leaves them unspecified).</summary>
    public IReadOnlyList<RuntimeBattleInventory> Inventories { get; }

    public IReadOnlyDictionary<string, RuntimeBattleItem> Items { get; }
    public IReadOnlyDictionary<string, RuntimeBattleArmor> Armors { get; }
    public IReadOnlyDictionary<string, RuntimeBattleUnitRule> Units { get; }

    /// <summary>RuleSoldier::getSpecialWeapon, by soldier type (only soldiers that have one).</summary>
    public IReadOnlyDictionary<string, string> SoldierSpecialWeapons { get; }

    /// <summary>RuleSoldier::getStandHeight, getKneelHeight and getFloatHeight, by soldier type.</summary>
    public IReadOnlyDictionary<string, RuntimeUnitHeights> SoldierHeights { get; private init; } = Map(new Dictionary<string, RuntimeUnitHeights>());

    /// <summary>RuleItemCategory::getInvOrder, by category (only categories that have one).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CategoryInventoryOrders { get; }

    /// <summary>RuleWeaponSet::getWeapons, by weapon set.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> WeaponSets { get; }

    internal static RuntimeBattleEquipment Link(Phase3ContentCatalog content, IReadOnlyDictionary<string, string> categoryReplacements)
    {
        ArgumentNullException.ThrowIfNull(content);
        var personnel = content.PersonnelTactical;
        var inventories = personnel.Inventories.Rules
            .OrderBy(static rule => rule.Value.ListOrder).ThenBy(static rule => rule.Id, StringComparer.Ordinal)
            .Select(static rule => new RuntimeBattleInventory(rule.Id, rule.Value.Type,
                List(rule.Value.Slots.Select(static slot => new RuntimeInventorySlot(slot.X, slot.Y))),
                Map(rule.Value.Costs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal))));
        var items = content.Items.Items.Rules.ToDictionary(static rule => rule.Id, rule =>
        {
            var values = rule.Value.Values;
            return new RuntimeBattleItem(
                values.Boolean("fixedWeapon"),
                values.NullableName("defaultInventorySlot") ?? "",
                values.GetInteger("defaultInvSlotX"),
                values.GetInteger("defaultInvSlotY"),
                values.GetInteger("invWidth"),
                values.GetInteger("invHeight"),
                List(rule.Value.SupportedInventorySections),
                values.GetInteger("turretType"),
                values.Boolean("psiRequired"),
                values.Boolean("manaRequired"),
                values.Boolean("isAmmoRechargeable"),
                values.GetInteger("heal"),
                values.GetInteger("painKiller"),
                values.GetInteger("stimulant"),
                List(rule.Value.Categories.Select(category => categoryReplacements.GetValueOrDefault(category, category))),
                List(AmmoActions.Select(action => rule.Value.Actions[action].AmmoSlot)),
                values.GetInteger("fuseType"),
                rule.Value.EffectiveLoadOrder,
                !string.IsNullOrEmpty(values.NullableName("spawnUnit")) || !string.IsNullOrEmpty(values.NullableName("spawnItem")))
            {
                BigSprite = rule.Value.ResourceIndexes["bigSprite"].Index,
                IgnoreInBaseDefense = values.Boolean("ignoreInBaseDefense"),
                StrengthApplied = values.Boolean("strengthApplied"),
                IgnoreInCraftEquip = values.Boolean("ignoreInCraftEquip"),
                InventoryMoveCostPercent = values.GetInteger("inventoryMoveCostPercent"),
                SpawnUnit = values.NullableName("spawnUnit") ?? "",
                SpawnItem = values.NullableName("spawnItem") ?? "",
                SpawnUnitFaction = values.GetInteger("spawnUnitFaction"),
                SpawnUnitChance = values.GetInteger("spawnUnitChance") == -1 ? values.GetInteger("specialChance") : values.GetInteger("spawnUnitChance"),
                SpawnItemChance = values.GetInteger("spawnItemChance") == -1 ? values.GetInteger("specialChance") : values.GetInteger("spawnItemChance"),
                ActionCosts = ActionCostsOf(rule.Value),
                FuseTrigger = new RuntimeFuseTrigger(rule.Value.FuseTriggers.DefaultBehavior, rule.Value.FuseTriggers.ThrowTrigger,
                    rule.Value.FuseTriggers.ProximityTrigger),
            };
        }, StringComparer.Ordinal);
        var armors = personnel.Armors.Rules.ToDictionary(static rule => rule.Id, static rule => new RuntimeBattleArmor(
            List(rule.Value.BuiltInWeapons), rule.Value.Strings["specialWeapon"], rule.Value.Strings["selfDestructItem"],
            rule.Value.Booleans["allowTwoMainWeapons"], rule.Value.Booleans["allowInv"], rule.Value.Integers["weight"])
        {
            Sides = List([rule.Value.Integers["frontArmor"], unchecked(rule.Value.Integers["sideArmor"] + rule.Value.Integers["leftArmorDiff"]),
                rule.Value.Integers["sideArmor"], rule.Value.Integers["rearArmor"], rule.Value.Integers["underArmor"]]),
            MoveCosts = new RuntimeArmorMoveCosts(Cost(rule.Value, "basePercent"), Cost(rule.Value, "baseFlyPercent"),
                Cost(rule.Value, "baseClimbPercent"), Cost(rule.Value, "baseNormalPercent")),
            StepCosts = new RuntimeArmorStepCosts(Cost(rule.Value, "walkPercent"), Cost(rule.Value, "runPercent"), Cost(rule.Value, "strafePercent"),
                Cost(rule.Value, "sneakPercent"), Cost(rule.Value, "flyWalkPercent"), Cost(rule.Value, "flyRunPercent"),
                Cost(rule.Value, "flyStrafePercent"), Cost(rule.Value, "flyUpPercent"), Cost(rule.Value, "flyDownPercent"),
                Cost(rule.Value, "climbUpPercent"), Cost(rule.Value, "climbDownPercent"), Cost(rule.Value, "gravLiftPercent")),
            StandHeight = rule.Value.Integers["standHeight"],
            KneelHeight = rule.Value.Integers["kneelHeight"],
            FloatHeight = rule.Value.Integers["floatHeight"],
            TurnCost = rule.Value.Integers["turnCost"],
            SpecialAbility = rule.Value.Integers["specab"],
            AlwaysVisible = rule.Value.Booleans["alwaysVisible"],
            TurnBeforeFirstStep = rule.Value.Booleans["turnBeforeFirstStep"],
            AllowsMoving = rule.Value.NullableBooleans["allowsMoving"] ?? true,
            AllowsRunning = rule.Value.NullableBooleans["allowsRunning"],
            AllowsStrafing = rule.Value.NullableBooleans["allowsStrafing"],
            AllowsSneaking = rule.Value.NullableBooleans["allowsSneaking"],
            AllowsKneeling = rule.Value.NullableBooleans["allowsKneeling"],
            FaceColorGroup = rule.Value.Integers["spriteFaceGroup"],
            HairColorGroup = rule.Value.Integers["spriteHairGroup"],
            UtileColorGroup = rule.Value.Integers["spriteUtileGroup"],
            RankColorGroup = rule.Value.Integers["spriteRankGroup"],
            FaceColors = List(rule.Value.SpriteColors["spriteFaceColor"]),
            HairColors = List(rule.Value.SpriteColors["spriteHairColor"]),
            UtileColors = List(rule.Value.SpriteColors["spriteUtileColor"]),
            RankColors = List(rule.Value.SpriteColors["spriteRankColor"]),
        }, StringComparer.Ordinal);
        var units = personnel.Units.Rules.ToDictionary(static rule => rule.Id, static rule => new RuntimeBattleUnitRule(
            Map(rule.Value.Stats.Values.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
            List(rule.Value.BuiltInWeaponSets.Select(static set => (IReadOnlyList<string>)List(set))),
            List(rule.Value.WeightedBuiltInWeaponSets.Select(static set =>
                (IReadOnlyDictionary<string, ulong>)Map(set.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)))),
            rule.Value.Strings["meleeWeapon"], rule.Value.Strings["psiWeapon"], rule.Value.Booleans["livingWeapon"], rule.Value.Strings["race"])
        {
            Rank = rule.Value.Strings["rank"],
            Aggression = rule.Value.Integers["aggression"],
            SpawnUnit = rule.Value.Strings["spawnUnit"],
            IsVip = rule.Value.Booleans["vip"],
            PickUpWeaponsMoreActively = rule.Value.Integers["pickUpWeaponsMoreActively"],
            EnergyRecovery = rule.Value.Integers["energyRecovery"],
            SpecialAbility = rule.Value.Integers["specab"],
            AvoidsFire = rule.Value.AvoidsFire,
            IsBrutal = rule.Value.Booleans["isBrutal"],
            IsNotBrutal = rule.Value.Booleans["isNotBrutal"],
            Heights = new RuntimeUnitHeights(rule.Value.Integers["standHeight"], rule.Value.Integers["kneelHeight"], rule.Value.Integers["floatHeight"]),
        },
            StringComparer.Ordinal);
        var soldierWeapons = personnel.Soldiers.Rules.Where(static rule => rule.Value.Strings["specialWeapon"].Length != 0)
            .ToDictionary(static rule => rule.Id, static rule => rule.Value.Strings["specialWeapon"], StringComparer.Ordinal);
        var categories = content.EquipmentProduction.ItemCategories.Rules.Where(static rule => rule.Value.InventoryOrder.Count != 0)
            .ToDictionary(static rule => rule.Id, static rule => (IReadOnlyList<string>)List(rule.Value.InventoryOrder), StringComparer.Ordinal);
        var weaponSets = content.EquipmentProduction.WeaponSets.Rules
            .ToDictionary(static rule => rule.Id, static rule => (IReadOnlyList<string>)List(rule.Value.Weapons), StringComparer.Ordinal);
        var soldierHeights = personnel.Soldiers.Rules.ToDictionary(static rule => rule.Id, static rule => new RuntimeUnitHeights(
            rule.Value.Integers["standHeight"], rule.Value.Integers["kneelHeight"], rule.Value.Integers["floatHeight"]), StringComparer.Ordinal);
        return new(List(inventories), Map(items), Map(armors), Map(units), Map(soldierWeapons), Map(categories), Map(weaponSets))
        {
            SoldierHeights = Map(soldierHeights),
        };
    }

    /// <summary>
    /// RuleItem::getCostAimed and the other getCost* with their getFlat* (getDefault: the first value the chain sets), per
    /// <see cref="RuntimeItemActionCosts"/> key. Use is zero for a psi amp without <c>psiAttackName</c> and Prime for an item without
    /// <c>primeActionName</c>; mind control and panic fall back to the use cost, with the use flat.
    /// </summary>
    private static RuntimeItemActionCosts ActionCostsOf(ItemRule rule)
    {
        const int psiAmp = 9;
        var aimed = rule.Actions["Aimed"];
        var auto = rule.Actions["Auto"];
        var snap = rule.Actions["Snap"];
        var melee = rule.Actions["Melee"];
        var use = rule.UseCosts["Use"];
        var useFlat = rule.UseFlats["Use"];
        var zero = new RuntimeUseCost(0, 0, 0, 0, 0, 0);
        var costs = new Dictionary<string, (RuntimeUseCost, RuntimeUseFlat)>(StringComparer.Ordinal)
        {
            [RuntimeItemActionCosts.Aimed] = (Coalesce(aimed.Cost), Coalesce(aimed.Flat, useFlat)),
            [RuntimeItemActionCosts.Auto] = (Coalesce(auto.Cost, aimed.Cost), Coalesce(auto.Flat, aimed.Flat, useFlat)),
            [RuntimeItemActionCosts.Snap] = (Coalesce(snap.Cost, aimed.Cost), Coalesce(snap.Flat, aimed.Flat, useFlat)),
            [RuntimeItemActionCosts.Melee] = (Coalesce(melee.Cost), Coalesce(melee.Flat, useFlat)),
            [RuntimeItemActionCosts.Use] = (rule.Values.GetInteger("battleType") != psiAmp || rule.Values.GetString("psiAttackName").Length != 0
                ? Coalesce(use) : zero, Coalesce(useFlat)),
            [RuntimeItemActionCosts.MindControl] = (Coalesce(rule.UseCosts["MindControl"], use), Coalesce(useFlat)),
            [RuntimeItemActionCosts.Panic] = (Coalesce(rule.UseCosts["Panic"], use), Coalesce(useFlat)),
            [RuntimeItemActionCosts.Throw] = (Coalesce(rule.UseCosts["Throw"]), Coalesce(rule.UseFlats["Throw"])),
            [RuntimeItemActionCosts.Prime] = (rule.Values.GetString("primeActionName").Length != 0 ? Coalesce(rule.UseCosts["Prime"]) : zero,
                Coalesce(rule.UseFlats["Prime"])),
            [RuntimeItemActionCosts.Unprime] = (Coalesce(rule.UseCosts["Unprime"]), Coalesce(rule.UseFlats["Unprime"])),
        };
        return new RuntimeItemActionCosts(Map(costs));
    }

    private static RuntimeUseCost Coalesce(params ItemUseValues<int?>[] chain)
    {
        int Pick(Func<ItemUseValues<int?>, int?> field) => chain.Select(field).FirstOrDefault(static value => value is not null) ?? 0;
        return new RuntimeUseCost(Pick(static v => v.Time), Pick(static v => v.Energy), Pick(static v => v.Morale), Pick(static v => v.Health),
            Pick(static v => v.Stun), Pick(static v => v.Mana));
    }

    private static RuntimeUseFlat Coalesce(params ItemUseValues<bool?>[] chain)
    {
        bool Pick(Func<ItemUseValues<bool?>, bool?> field) => chain.Select(field).FirstOrDefault(static value => value is not null) ?? false;
        return new RuntimeUseFlat(Pick(static v => v.Time), Pick(static v => v.Energy), Pick(static v => v.Morale), Pick(static v => v.Health),
            Pick(static v => v.Stun), Pick(static v => v.Mana));
    }

    private static ReadOnlyCollection<T> List<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());

    private static (int Time, int Energy) Cost(PersonnelTactical.ArmorRule armor, string key) =>
        (armor.MoveCosts[key].TimePercent, armor.MoveCosts[key].EnergyPercent);

    private static ReadOnlyDictionary<string, T> Map<T>(Dictionary<string, T> source) => new(source);
}

/// <summary>RuleInventory as the battle reads it. Type: 0 slot, 1 hand, 2 ground.</summary>
public sealed record RuntimeBattleInventory(string Id, int Type, IReadOnlyList<RuntimeInventorySlot> Slots, IReadOnlyDictionary<string, int> Costs)
{
    public const int Slot = 0;
    public const int Hand = 1;
    public const int Ground = 2;

    public bool IsRightHand => Id == "STR_RIGHT_HAND";
    public bool IsLeftHand => Id == "STR_LEFT_HAND";
}

public sealed record RuntimeInventorySlot(int X, int Y);

/// <summary>
/// RuleItem's equipment fields: fixed weapon, default inventory slot and position, inventory size, supported sections,
/// turret type, psi and mana requirements, rechargeable ammo, medikit quantities, categories (after replacement), the
/// aimed, auto, snap and melee actions' ammo slots, the fuse type (-3 none) and the load order.
/// </summary>
public sealed record RuntimeBattleItem(
    bool Fixed,
    string DefaultInventorySlot,
    int DefaultInventorySlotX,
    int DefaultInventorySlotY,
    int InventoryWidth,
    int InventoryHeight,
    IReadOnlyList<string> SupportedInventorySections,
    int TurretType,
    bool PsiRequired,
    bool ManaRequired,
    bool AmmoRechargeable,
    int HealQuantity,
    int PainKillerQuantity,
    int StimulantQuantity,
    IReadOnlyList<string> Categories,
    IReadOnlyList<int> ActionAmmoSlots,
    int FuseType,
    int LoadOrder,
    bool Spawns)
{
    /// <summary>RuleItem::getBigSprite (-1 for none): an item with one that is not fixed is an inventory item.</summary>
    public int BigSprite { get; init; } = -1;

    /// <summary>RuleItem's <c>ignoreInBaseDefense</c> (canBeEquippedBeforeBaseDefense) and <c>ignoreInCraftEquip</c>.</summary>
    public bool IgnoreInBaseDefense { get; init; }

    /// <summary>RuleItem's <c>strengthApplied</c>: the power gets the owner's strength bonus (<c>getPowerBonus</c>).</summary>
    public bool StrengthApplied { get; init; }
    public bool IgnoreInCraftEquip { get; init; } = true;

    /// <summary>RuleItem::isInventoryItem.</summary>
    public bool IsInventoryItem => BigSprite > -1 && !Fixed;

    /// <summary>RuleItem::getInventoryMoveCostPercent (<c>inventoryMoveCost: {basePercent}</c>).</summary>
    public int InventoryMoveCostPercent { get; init; } = 100;

    /// <summary>RuleItem::getCost* and getFlat*, resolved (see <see cref="RuntimeItemActionCosts"/>).</summary>
    public RuntimeItemActionCosts ActionCosts { get; init; } = RuntimeItemActionCosts.Default;

    /// <summary>RuleItem::getFuseTriggerEvent's fields BattleItem::setFuseTimer reads.</summary>
    public RuntimeFuseTrigger FuseTrigger { get; init; } = RuntimeFuseTrigger.Default;

    /// <summary>RuleItem::getSpawnUnit and getSpawnItem ("" for none), getSpawnUnitFaction (-1 for the owner's) and the chances (getSpawnUnitChance, getSpawnItemChance: <c>specialChance</c> unless set).</summary>
    public string SpawnUnit { get; init; } = "";
    public string SpawnItem { get; init; } = "";
    public int SpawnUnitFaction { get; init; } = -1;
    public int SpawnUnitChance { get; init; } = 100;
    public int SpawnItemChance { get; init; } = 100;
}

/// <summary>RuleItemUseCost: the time units, energy, morale, health, stun and mana an action costs (percent unless flat).</summary>
public sealed record RuntimeUseCost(int Time, int Energy, int Morale, int Health, int Stun, int Mana);

/// <summary>RuleItemUseFlat: for each resource of a <see cref="RuntimeUseCost"/>, whether it is a flat value instead of a percentage.</summary>
public sealed record RuntimeUseFlat(bool Time, bool Energy, bool Morale, bool Health, bool Stun, bool Mana);

/// <summary>
/// An item's action costs as <c>BattleUnit::getActionTUs(RuleItem)</c> reads them, with the reference's fallbacks applied
/// (an unset auto or snap cost takes the aimed one, an unset flag the use one), by the action's key.
/// </summary>
public sealed record RuntimeItemActionCosts(IReadOnlyDictionary<string, (RuntimeUseCost Cost, RuntimeUseFlat Flat)> Actions)
{
    public const string Aimed = "Aimed";
    public const string Auto = "Auto";
    public const string Snap = "Snap";
    public const string Melee = "Melee";
    public const string Use = "Use";
    public const string MindControl = "MindControl";
    public const string Panic = "Panic";
    public const string Throw = "Throw";
    public const string Prime = "Prime";
    public const string Unprime = "Unprime";

    private static readonly (RuntimeUseCost, RuntimeUseFlat) None = (new RuntimeUseCost(0, 0, 0, 0, 0, 0),
        new RuntimeUseFlat(false, true, true, true, true, true));

    public static RuntimeItemActionCosts Default { get; } = new(new ReadOnlyDictionary<string, (RuntimeUseCost, RuntimeUseFlat)>(
        new Dictionary<string, (RuntimeUseCost, RuntimeUseFlat)>()));

    /// <summary>The cost and flags of an action key; zero cost for a key the item does not define.</summary>
    public (RuntimeUseCost Cost, RuntimeUseFlat Flat) Of(string action) => Actions.TryGetValue(action, out var value) ? value : None;
}

/// <summary>RuleItemFuseTrigger's <c>defaultBehavior</c>, <c>throwTrigger</c> and <c>proximityTrigger</c>.</summary>
public sealed record RuntimeFuseTrigger(bool DefaultBehavior, bool ThrowTrigger, bool ProximityTrigger)
{
    public static RuntimeFuseTrigger Default { get; } = new(true, false, false);
}

/// <summary>Armor's equipment fields: built-in weapons, special and self-destruct items, two main weapons, inventory, weight.</summary>
public sealed record RuntimeBattleArmor(
    IReadOnlyList<string> BuiltInWeapons,
    string SpecialWeapon,
    string SelfDestructItem,
    bool AllowTwoMainWeapons,
    bool HasInventory,
    int Weight)
{
    /// <summary>The five side armor values in UnitSide order (front, left = side + leftArmorDiff, right, rear, under).</summary>
    public IReadOnlyList<int> Sides { get; init; } = [0, 0, 0, 0, 0];

    /// <summary>Armor's <c>moveCost</c> base percentages (basePercent, baseFlyPercent, baseClimbPercent, baseNormalPercent).</summary>
    public RuntimeArmorMoveCosts MoveCosts { get; init; } = RuntimeArmorMoveCosts.Default;

    /// <summary>Armor's <c>moveCost</c> percentages per kind of step (Armor::getMoveCostWalk and the others).</summary>
    public RuntimeArmorStepCosts StepCosts { get; init; } = RuntimeArmorStepCosts.Default;

    /// <summary>Armor::getStandHeight, getKneelHeight and getFloatHeight (-1 for the unit's or soldier's own).</summary>
    public int StandHeight { get; init; } = -1;
    public int KneelHeight { get; init; } = -1;
    public int FloatHeight { get; init; } = -1;

    /// <summary>Armor::getTurnCost (<c>turnCost</c>, the time units of a turn of one eighth) and getTurnBeforeFirstStep.</summary>
    public int TurnCost { get; init; } = 1;
    public bool TurnBeforeFirstStep { get; init; }

    /// <summary>Armor::isAlwaysVisible (<c>alwaysVisible</c>): units in it are visible to the player whether spotted or not.</summary>
    public bool AlwaysVisible { get; init; }

    /// <summary>Armor::getSpecialAbility (<c>specab</c>), which a soldier's unit takes as its own (SPECAB_BURNFLOOR is 2).</summary>
    public int SpecialAbility { get; init; }

    /// <summary>Armor::allowsMoving (<c>allowsMoving</c>, default true).</summary>
    public bool AllowsMoving { get; init; } = true;

    /// <summary>
    /// The nullable <c>allowsRunning</c>, <c>allowsStrafing</c>, <c>allowsSneaking</c> and <c>allowsKneeling</c> (useBoolNullable):
    /// null takes the caller's default (<see cref="Allows"/>).
    /// </summary>
    public bool? AllowsRunning { get; init; }
    public bool? AllowsStrafing { get; init; }
    public bool? AllowsSneaking { get; init; }
    public bool? AllowsKneeling { get; init; }

    /// <summary>useBoolNullable: the armor's value, else the default.</summary>
    public static bool Allows(bool? value, bool @default) => value ?? @default;

    /// <summary>Armor's recolor groups (<c>spriteFaceGroup</c> and the others, 0 for none) and colors (<c>spriteFaceColor</c> and the others).</summary>
    public int FaceColorGroup { get; init; }
    public int HairColorGroup { get; init; }
    public int UtileColorGroup { get; init; }
    public int RankColorGroup { get; init; }
    public IReadOnlyList<int> FaceColors { get; init; } = [];
    public IReadOnlyList<int> HairColors { get; init; } = [];
    public IReadOnlyList<int> UtileColors { get; init; } = [];
    public IReadOnlyList<int> RankColors { get; init; } = [];
}

/// <summary>ArmorMoveCost of each base kind: (time percent, energy percent).</summary>
public sealed record RuntimeArmorMoveCosts((int Time, int Energy) Base, (int Time, int Energy) Fly, (int Time, int Energy) Climb,
    (int Time, int Energy) Normal)
{
    public static RuntimeArmorMoveCosts Default { get; } = new((100, 100), (100, 100), (100, 100), (100, 100));
}

/// <summary>Armor's per-step <c>moveCost</c> percentages: (time percent, energy percent) of each kind of step.</summary>
public sealed record RuntimeArmorStepCosts((int Time, int Energy) Walk, (int Time, int Energy) Run, (int Time, int Energy) Strafe,
    (int Time, int Energy) Sneak, (int Time, int Energy) FlyWalk, (int Time, int Energy) FlyRun, (int Time, int Energy) FlyStrafe,
    (int Time, int Energy) FlyUp, (int Time, int Energy) FlyDown, (int Time, int Energy) ClimbUp, (int Time, int Energy) ClimbDown,
    (int Time, int Energy) GravLift)
{
    public static RuntimeArmorStepCosts Default { get; } = new((100, 50), (75, 75), (100, 50), (100, 50), (100, 50), (75, 75), (100, 50),
        (100, 0), (100, 0), (100, 50), (100, 50), (100, 0));
}

/// <summary>Unit's battle fields: statistics, built-in weapon sets (plain and weighted), melee and psi weapons, living weapon, race.</summary>
public sealed record RuntimeBattleUnitRule(
    IReadOnlyDictionary<string, short> Stats,
    IReadOnlyList<IReadOnlyList<string>> BuiltInWeaponSets,
    IReadOnlyList<IReadOnlyDictionary<string, ulong>> WeightedBuiltInWeaponSets,
    string MeleeWeapon,
    string PsiWeapon,
    bool LivingWeapon,
    string Race)
{
    /// <summary>Unit's <c>rank</c>, <c>aggression</c>, <c>spawnUnit</c> ("" for none), <c>vip</c> and <c>pickUpWeaponsMoreActively</c> (-1 for the mod's default).</summary>
    public string Rank { get; init; } = "";
    public int Aggression { get; init; }
    public string SpawnUnit { get; init; } = "";
    public bool IsVip { get; init; }
    public int PickUpWeaponsMoreActively { get; init; } = -1;

    /// <summary>Unit::getStandHeight, getKneelHeight and getFloatHeight.</summary>
    public RuntimeUnitHeights Heights { get; init; } = new(0, 0, 0);

    /// <summary>Unit's <c>energyRecovery</c> (basicEnergyRegeneration for a unit that is no soldier).</summary>
    public int EnergyRecovery { get; init; }

    /// <summary>Unit's <c>specab</c> (SPECAB_NONE 0, explode on death 1, burn floor 2, burn and explode 3).</summary>
    public int SpecialAbility { get; init; }

    /// <summary>Unit's nullable <c>avoidsFire</c> (<c>Unit::avoidsFire</c>: null takes <c>specab &lt; SPECAB_BURNFLOOR</c>).</summary>
    public bool? AvoidsFire { get; init; }

    /// <summary>Unit's <c>isBrutal</c> and <c>isNotBrutal</c>, which override the options' choice of brutal AI.</summary>
    public bool IsBrutal { get; init; }
    public bool IsNotBrutal { get; init; }

    /// <summary>Unit::avoidsFire.</summary>
    public bool AvoidsFireOrDefault => AvoidsFire ?? SpecialAbility < 2;
}

/// <summary>The voxel heights of a unit standing, kneeling and floating (the unit rule's or soldier's, unless the armor sets its own).</summary>
public sealed record RuntimeUnitHeights(int Stand, int Kneel, int Floating);
