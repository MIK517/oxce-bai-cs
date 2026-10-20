using System.Collections.ObjectModel;
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
                rule.Value.EffectiveLoadOrder);
        }, StringComparer.Ordinal);
        var armors = personnel.Armors.Rules.ToDictionary(static rule => rule.Id, static rule => new RuntimeBattleArmor(
            List(rule.Value.BuiltInWeapons), rule.Value.Strings["specialWeapon"], rule.Value.Strings["selfDestructItem"],
            rule.Value.Booleans["allowTwoMainWeapons"], rule.Value.Booleans["allowInv"], rule.Value.Integers["weight"]), StringComparer.Ordinal);
        var units = personnel.Units.Rules.ToDictionary(static rule => rule.Id, static rule => new RuntimeBattleUnitRule(
            Map(rule.Value.Stats.Values.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
            List(rule.Value.BuiltInWeaponSets.Select(static set => (IReadOnlyList<string>)List(set))),
            List(rule.Value.WeightedBuiltInWeaponSets.Select(static set =>
                (IReadOnlyDictionary<string, ulong>)Map(set.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)))),
            rule.Value.Strings["meleeWeapon"], rule.Value.Strings["psiWeapon"], rule.Value.Booleans["livingWeapon"], rule.Value.Strings["race"]),
            StringComparer.Ordinal);
        var soldierWeapons = personnel.Soldiers.Rules.Where(static rule => rule.Value.Strings["specialWeapon"].Length != 0)
            .ToDictionary(static rule => rule.Id, static rule => rule.Value.Strings["specialWeapon"], StringComparer.Ordinal);
        var categories = content.EquipmentProduction.ItemCategories.Rules.Where(static rule => rule.Value.InventoryOrder.Count != 0)
            .ToDictionary(static rule => rule.Id, static rule => (IReadOnlyList<string>)List(rule.Value.InventoryOrder), StringComparer.Ordinal);
        var weaponSets = content.EquipmentProduction.WeaponSets.Rules
            .ToDictionary(static rule => rule.Id, static rule => (IReadOnlyList<string>)List(rule.Value.Weapons), StringComparer.Ordinal);
        return new(List(inventories), Map(items), Map(armors), Map(units), Map(soldierWeapons), Map(categories), Map(weaponSets));
    }

    private static ReadOnlyCollection<T> List<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());

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
    int LoadOrder);

/// <summary>Armor's equipment fields: built-in weapons, special and self-destruct items, two main weapons, inventory, weight.</summary>
public sealed record RuntimeBattleArmor(
    IReadOnlyList<string> BuiltInWeapons,
    string SpecialWeapon,
    string SelfDestructItem,
    bool AllowTwoMainWeapons,
    bool HasInventory,
    int Weight);

/// <summary>Unit's battle fields: statistics, built-in weapon sets (plain and weighted), melee and psi weapons, living weapon, race.</summary>
public sealed record RuntimeBattleUnitRule(
    IReadOnlyDictionary<string, short> Stats,
    IReadOnlyList<IReadOnlyList<string>> BuiltInWeaponSets,
    IReadOnlyList<IReadOnlyDictionary<string, ulong>> WeightedBuiltInWeaponSets,
    string MeleeWeapon,
    string PsiWeapon,
    bool LivingWeapon,
    string Race);
