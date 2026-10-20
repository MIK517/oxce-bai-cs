using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// Items units start a battle with: SavedBattleGame::initUnit (special built-in items, armor and unit built-in weapons,
/// weighted weapon sets, living weapons), createItemForUnit, createItemForTile and BattleUnit::addItem with its inventory
/// fitting. The reference's CreateUnit and CreateItem script hooks are tactical script bindings (Phase 7 branch 8).
/// </summary>
internal sealed class UnitEquipment
{
    // BattleType values.
    private const int Firearm = 1;
    private const int Ammo = 2;
    private const int Melee = 3;
    private const int Medikit = 6;
    private const int PsiAmp = 9;

    /// <summary>RuleItem::AmmoSlotSelfUse.</summary>
    private const int SelfUse = -1;

    /// <summary>BattleUnit::SPEC_WEAPON_MAX.</summary>
    private const int SpecialWeaponMaximum = 4;

    private const string RightHand = "STR_RIGHT_HAND";
    private const string LeftHand = "STR_LEFT_HAND";

    private readonly BattleState _battle;
    private readonly RuntimeRuleCatalog _rules;
    private readonly RuntimeBattleEquipment _equipment;
    private readonly IRandomSource _random;

    public UnitEquipment(BattleState battle, RuntimeRuleCatalog rules, IRandomSource random)
    {
        _battle = battle;
        _rules = rules;
        _equipment = rules.Battle.Equipment;
        _random = random;
    }

    /// <summary>SavedBattleGame::initUnit for a unit just placed: its special built-in items, then its fixed items.</summary>
    public void InitUnit(BattleUnit unit, RuntimeBattleUnitRule? unitRule, string soldierType, int itemLevel = 0)
    {
        SetSpecialWeapon(unit, unitRule, soldierType);
        AddFixedItems(unit, Armor(unit.ArmorId).BuiltInWeapons);
        if (unitRule is null) return;
        if (unitRule.BuiltInWeaponSets.Count != 0)
            AddFixedItems(unit, unitRule.BuiltInWeaponSets[Math.Min(itemLevel, unitRule.BuiltInWeaponSets.Count - 1)]);
        if (unitRule.WeightedBuiltInWeaponSets.Count != 0)
        {
            var weights = unitRule.WeightedBuiltInWeaponSets[Math.Min(itemLevel, unitRule.WeightedBuiltInWeaponSets.Count - 1)];
            var set = WorldAlienMissionGeneration.Choose(weights, _random);
            if (!_equipment.WeaponSets.TryGetValue(set, out var weapons))
                throw new MapGenerationException($"Unit {unit.UnitType} picks weapon set '{set}', which does not exist.");
            AddFixedItems(unit, weapons);
        }
        if (unitRule.LivingWeapon)
        {
            // The terrorist's weapon is its race without the "STR_" prefix and with the suffix _WEAPON.
            if (unitRule.Race.Length < 4) throw new MapGenerationException($"Living weapon unit {unit.UnitType} has a race shorter than four characters.");
            var weapon = unitRule.Race[4..] + "_WEAPON";
            if (_rules.Items.TryGet(weapon, out _) && CreateItemForUnit(weapon, unit) is { } item)
                unit.TurretType = BattleItemRule(item.ItemType).TurretType;
        }
    }

    /// <summary>SavedBattleGame::createItemForUnit: a new item the unit takes, or null (its ID still used) when it fits nowhere.</summary>
    public BattleItem? CreateItemForUnit(string itemType, BattleUnit unit, bool fixedWeapon = false)
    {
        var item = NewItem(itemType);
        if (!AddItem(item, unit, allowSecondClip: false, allowAutoLoadout: fixedWeapon, allowUnloadedWeapons: fixedWeapon, allowInfinite: false, testMode: false))
        {
            // The C3 fallback can leave a deleted item in the unit's inventory, which the reference then reads freed.
            if (item.OwnerUnitId is not null)
                throw new MapGenerationException($"Item {itemType} was placed in an inventory section and then reported as not placed.");
            return null;
        }
        _battle.AddItem(item);
        return item;
    }

    /// <summary>SavedBattleGame::createItemForTile.</summary>
    public BattleItem CreateItemForTile(string itemType, BattlePosition? tile) => _battle.AddItemOnTile(NewItem(itemType), tile);

    /// <summary>The ammunition item RuleItem::getVehicleClipAmmo names for a vehicle weapon, or null.</summary>
    public string? VehicleClipAmmo(string vehicleItem)
    {
        var rule = ItemRule(vehicleItem);
        var slot = rule.VehicleFixedAmmoSlot;
        if (slot < 0) return null;
        if (slot >= rule.CompatibleAmmo.Count)
            throw new MapGenerationException($"Vehicle item {vehicleItem} names ammo slot {slot}, which it does not have.");
        return rule.CompatibleAmmo[slot].Count == 0 ? null : _rules.Items.GetExternalId(rule.CompatibleAmmo[slot][0]);
    }

    public RuntimeBattleItem BattleItemRule(string itemType) => _equipment.Items.TryGetValue(itemType, out var rule)
        ? rule : throw new MapGenerationException($"Item {itemType} does not exist.");

    /// <summary>The BattleItem constructor: clip ammo, medikit quantities, and the ammo slots a weapon fills itself.</summary>
    private BattleItem NewItem(string itemType)
    {
        var rule = ItemRule(itemType);
        var battleRule = BattleItemRule(itemType);
        var item = _battle.NewItem(itemType);
        item.AmmoQuantity = rule.ClipSize;
        if (rule.BattleType == Medikit)
        {
            item.HealQuantity = battleRule.HealQuantity;
            item.PainKillerQuantity = battleRule.PainKillerQuantity;
            item.StimulantQuantity = battleRule.StimulantQuantity;
        }
        else if (rule.BattleType is Firearm or Melee)
        {
            for (var slot = 0; slot < BattleItem.AmmoSlotCount; slot++)
            {
                if (slot >= rule.CompatibleAmmo.Count || rule.CompatibleAmmo[slot].Count == 0) item.AmmoSlots[slot] = item.Id;
                else item.IsWeaponWithAmmo = true;
            }
        }
        return item;
    }

    /// <summary>
    /// BattleUnit::setSpecialWeapon for a new unit: the unit's melee weapon, the armor's special weapon, a hostile unit's
    /// psi weapon, the soldier type's special weapon and the armor's self-destruct item, without duplicates.
    /// </summary>
    private void SetSpecialWeapon(BattleUnit unit, RuntimeBattleUnitRule? unitRule, string soldierType)
    {
        var armor = Armor(unit.ArmorId);
        // Mod::getItem without the required flag: an unknown unit melee or psi weapon is no special weapon.
        if (unitRule is not null && _rules.Items.TryGet(unitRule.MeleeWeapon, out _)) Add(unitRule.MeleeWeapon);
        Add(armor.SpecialWeapon);
        if (unitRule is not null && unit.Faction == BattleFaction.Hostile && _rules.Items.TryGet(unitRule.PsiWeapon, out _)) Add(unitRule.PsiWeapon);
        if (soldierType.Length != 0 && _equipment.SoldierSpecialWeapons.TryGetValue(soldierType, out var soldierWeapon)) Add(soldierWeapon);
        Add(armor.SelfDestructItem);

        void Add(string itemType)
        {
            if (itemType.Length == 0 || unit.SpecialWeaponIds.Count >= SpecialWeaponMaximum) return;
            var rule = ItemRule(itemType);
            if (unit.Stats.PsiSkill <= 0 && BattleItemRule(itemType).PsiRequired) return;
            if (rule.BattleType is Firearm or Melee && rule.ClipSize == 0)
                throw new MapGenerationException($"Weapon {itemType} is used as a special built-in weapon on unit {unit.UnitType} but doesn't have it's own ammo - give it a clipSize!");
            if (unit.SpecialWeaponIds.Exists(id => Find(id).ItemType == itemType)) return;
            // SavedBattleGame::createItemForUnitSpecialBuiltin: owned, in no inventory section.
            var item = NewItem(itemType);
            item.OwnerUnitId = unit.Id;
            item.IsSpecialWeapon = true;
            _battle.AddItem(item);
            unit.SpecialWeaponIds.Add(item.Id);
        }
    }

    /// <summary>SavedBattleGame::addFixedItems: the listed items as fixed weapons, ammunition after the rest.</summary>
    private void AddFixedItems(BattleUnit unit, IReadOnlyList<string> items)
    {
        foreach (var itemType in items)
            if (ItemRule(itemType).BattleType != Ammo) CreateItemForUnit(itemType, unit, fixedWeapon: true);
        foreach (var itemType in items)
            if (ItemRule(itemType).BattleType == Ammo) CreateItemForUnit(itemType, unit, fixedWeapon: true);
    }

    /// <summary>
    /// BattleUnit::addItem. Test mode only keeps the section fitting from moving the item: as in the reference, a fixed
    /// weapon still takes its default slot and ammunition still loads into a hand weapon.
    /// </summary>
    public bool AddItem(BattleItem item, BattleUnit unit, bool allowSecondClip, bool allowAutoLoadout, bool allowUnloadedWeapons,
        bool allowInfinite, bool testMode)
    {
        var rightHand = Inventory(RightHand);
        var leftHand = Inventory(LeftHand);
        var placed = false;
        var rule = ItemRule(item.ItemType);
        var battleRule = BattleItemRule(item.ItemType);
        var armor = Armor(unit.ArmorId);
        var weight = 0;
        var player = unit.Faction == BattleFaction.Player;
        var standardPlayerUnit = player && armor.HasInventory;

        // Tanks and aliens don't care about weight or multiple items.
        if (standardPlayerUnit)
        {
            weight = CarriedWeight(unit, armor) + TotalWeight(item);
            if (rule.BattleType is not (Firearm or Melee) && !allowInfinite)
            {
                var tally = 0;
                foreach (var carried in _battle.InventoryOf(unit))
                {
                    if (carried.ItemType != item.ItemType) continue;
                    if (!allowSecondClip || rule.BattleType != Ammo || ++tally == 2) return false;
                }
            }
        }

        if (battleRule.Fixed)
        {
            // The default slot the rule names, or a hand.
            if (battleRule.DefaultInventorySlot.Length != 0)
            {
                var slot = Inventory(battleRule.DefaultInventorySlot);
                if (ItemAt(unit, slot, 0, 0) is null)
                {
                    MoveToOwner(item, unit);
                    item.Slot = slot.Id;
                    item.SlotX = battleRule.DefaultInventorySlotX;
                    item.SlotY = battleRule.DefaultInventorySlotY;
                    placed = true;
                }
            }
            if (!placed && (Fit(rightHand, item, unit, testMode) || Fit(leftHand, item, unit, testMode))) placed = true;
            if (placed)
            {
                item.XcomProperty = player;
                if (battleRule.TurretType > -1) unit.TurretType = battleRule.TurretType;
            }
            return placed;
        }

        // Only items the unit has the skill to use.
        if (unit.Stats.PsiSkill <= 0 && battleRule.PsiRequired) return false;
        if (battleRule.ManaRequired && player) return false;

        var keep = true;
        var ammoCase = rule.BattleType == Ammo;
        if (rule.BattleType is Firearm or Melee)
        {
            var loaded = HaveAnyAmmo(item) || !player || !armor.HasInventory || allowUnloadedWeapons;
            if (loaded && (unit.SoldierId is null || allowAutoLoadout))
            {
                // C1: the right hand, or the left for a unit that may hold two main weapons.
                if (Fit(rightHand, item, unit, testMode)) placed = true;
                if (!placed && (!player || armor.AllowTwoMainWeapons) && Fit(leftHand, item, unit, testMode)) placed = true;
            }
            // An unplaced weapon falls through to the ammunition case.
            ammoCase = !placed;
        }
        if (ammoCase)
        {
            // Aliens' and tanks' weapons get their ammo afterwards.
            var rightWeapon = HandWeapon(unit, RightHand);
            var leftWeapon = HandWeapon(unit, LeftHand);
            if (rightWeapon is not null && (BattleItemRule(rightWeapon.ItemType).Fixed || !player || allowUnloadedWeapons) &&
                rightWeapon.IsWeaponWithAmmo && SetAmmoPreMission(rightWeapon, item))
                placed = true;
            else if (leftWeapon is not null && (BattleItemRule(leftWeapon.ItemType).Fixed || !player || allowUnloadedWeapons) &&
                leftWeapon.IsWeaponWithAmmo && SetAmmoPreMission(leftWeapon, item))
                placed = true;
            else
            {
                // Don't take ammo for weapons we don't have.
                keep = !player;
                if (rightWeapon is not null && SlotForAmmo(rightWeapon, item.ItemType) != -1) keep = true;
                if (leftWeapon is not null && SlotForAmmo(leftWeapon, item.ItemType) != -1) keep = true;
            }
        }

        if (!placed && (rule.BattleType != Ammo || keep))
        {
            if (rule.BattleType == PsiAmp && unit.Faction == BattleFaction.Hostile)
            {
                // C2: hostiles hold a psi-amp.
                if (Fit(rightHand, item, unit, testMode) || Fit(leftHand, item, unit, testMode)) placed = true;
            }
            else if ((unit.SoldierId is null || allowAutoLoadout) && unit.Stats.Strength >= weight)
            {
                // D1: the item's default slot.
                if (standardPlayerUnit && battleRule.DefaultInventorySlot.Length != 0)
                {
                    var slot = Inventory(battleRule.DefaultInventorySlot);
                    if (slot.Type != RuntimeBattleInventory.Ground) placed = Fit(slot, item, unit, testMode);
                }
                // D2: the slot order of the item's first category that has one.
                if (!placed && standardPlayerUnit &&
                    battleRule.Categories.FirstOrDefault(_equipment.CategoryInventoryOrders.ContainsKey) is { } category)
                {
                    foreach (var name in _equipment.CategoryInventoryOrders[category])
                    {
                        var slot = Inventory(name);
                        if (slot.Type == RuntimeBattleInventory.Ground) continue;
                        placed = Fit(slot, item, unit, testMode);
                        if (placed) break;
                    }
                }
                if (!placed) placed = FitCheapest(item, unit, rightHand, leftHand, testMode);
                // C3: every slot section in list order; the item ends in the last that fits.
                if (!placed)
                    foreach (var slot in _equipment.Inventories)
                        if (slot.Type == RuntimeBattleInventory.Slot) placed = Fit(slot, item, unit, testMode);
            }
        }

        item.XcomProperty = player;
        return placed;
    }

    /// <summary>
    /// addItem's storage search: the non-hand section the item fits with the lowest move cost to the hand to optimize for
    /// (the left only when it alone is empty), ties broken by the cost to the other hand.
    /// </summary>
    private bool FitCheapest(BattleItem item, BattleUnit unit, RuntimeBattleInventory rightHand, RuntimeBattleInventory leftHand, bool testMode)
    {
        var bestPrimary = int.MaxValue;
        var bestSecondary = int.MaxValue;
        RuntimeBattleInventory? best = null;
        var optimizeForLeft = HandWeapon(unit, LeftHand) is null && HandWeapon(unit, RightHand) is not null;
        foreach (var slot in _equipment.Inventories)
        {
            if (slot.Type == RuntimeBattleInventory.Ground || slot.IsLeftHand || slot.IsRightHand) continue;
            if (!Fit(slot, item, unit, test: true)) continue;
            var costRight = Cost(slot, rightHand);
            var costLeft = Cost(slot, leftHand);
            var primary = optimizeForLeft ? costLeft : costRight;
            var secondary = optimizeForLeft ? costRight : costLeft;
            if (primary < bestPrimary || primary == bestPrimary && secondary < bestSecondary)
            {
                bestPrimary = primary;
                bestSecondary = secondary;
                best = slot;
            }
        }
        return best is { Type: RuntimeBattleInventory.Slot } && Fit(best, item, unit, testMode);
    }

    /// <summary>RuleInventory::getCost.</summary>
    private static int Cost(RuntimeBattleInventory from, RuntimeBattleInventory to)
    {
        if (ReferenceEquals(from, to)) return 0;
        return from.Costs.TryGetValue(to.Id, out var cost)
            ? cost : throw new MapGenerationException($"Inventory {from.Id} has no move cost to {to.Id}.");
    }

    /// <summary>BattleUnit::fitItemToInventory.</summary>
    private bool Fit(RuntimeBattleInventory slot, BattleItem item, BattleUnit unit, bool test = false)
    {
        var battleRule = BattleItemRule(item.ItemType);
        // RuleItem::canBePlacedIntoInventorySection.
        if (battleRule.SupportedInventorySections.Count != 0 && slot.Type != RuntimeBattleInventory.Ground &&
            !battleRule.SupportedInventorySections.Contains(slot.Id))
            return false;
        if (slot.Type == RuntimeBattleInventory.Hand)
        {
            if (Overlaps(unit, item, slot, 0, 0)) return false;
            if (!test)
            {
                MoveToOwner(item, unit);
                item.Slot = slot.Id;
            }
            return true;
        }
        if (slot.Type != RuntimeBattleInventory.Slot) return false;
        foreach (var position in slot.Slots)
        {
            if (Overlaps(unit, item, slot, position.X, position.Y) || !FitsInSlot(slot, battleRule, position.X, position.Y)) continue;
            if (!test)
            {
                MoveToOwner(item, unit);
                item.Slot = slot.Id;
                item.SlotX = position.X;
                item.SlotY = position.Y;
            }
            return true;
        }
        return false;
    }

    /// <summary>RuleInventory::fitItemInSlot for a slot section: every cell the item covers is one of the section's.</summary>
    private static bool FitsInSlot(RuntimeBattleInventory slot, RuntimeBattleItem rule, int x, int y)
    {
        var total = rule.InventoryWidth * rule.InventoryHeight;
        var found = 0;
        foreach (var cell in slot.Slots)
        {
            if (found >= total) break;
            if (cell.X >= x && cell.X < x + rule.InventoryWidth && cell.Y >= y && cell.Y < y + rule.InventoryHeight) found++;
        }
        return found == total;
    }

    /// <summary>Inventory::overlapItems for a section that is not the ground.</summary>
    private bool Overlaps(BattleUnit unit, BattleItem item, RuntimeBattleInventory slot, int x, int y)
    {
        var rule = BattleItemRule(item.ItemType);
        foreach (var carried in _battle.InventoryOf(unit))
        {
            if (carried.Id == item.Id || carried.Slot != slot.Id) continue;
            if (slot.Type == RuntimeBattleInventory.Hand) return true;
            var carriedRule = BattleItemRule(carried.ItemType);
            if (!(x >= carried.SlotX + carriedRule.InventoryWidth || x + rule.InventoryWidth <= carried.SlotX ||
                y >= carried.SlotY + carriedRule.InventoryHeight || y + rule.InventoryHeight <= carried.SlotY))
                return true;
        }
        return false;
    }

    /// <summary>BattleItem::moveToOwner: the item leaves its tile and goes to the unit (or to no owner).</summary>
    public static void MoveToOwner(BattleItem item, BattleUnit? unit)
    {
        item.Tile = null;
        item.OwnerUnitId = unit?.Id;
    }

    /// <summary>BattleUnit::hasInventory.</summary>
    public bool HasInventory(BattleUnit unit) => Armor(unit.ArmorId).HasInventory;

    public BattleItem? ItemAt(BattleUnit unit, string slot, int x, int y) => ItemAt(unit, Inventory(slot), x, y);

    /// <summary>BattleUnit::getItem(slot, x, y): the carried item covering a cell of a section.</summary>
    private BattleItem? ItemAt(BattleUnit unit, RuntimeBattleInventory slot, int x, int y)
    {
        foreach (var carried in _battle.InventoryOf(unit))
        {
            if (carried.Slot != slot.Id) continue;
            if (slot.Type == RuntimeBattleInventory.Hand) return carried;
            var rule = BattleItemRule(carried.ItemType);
            if (x >= carried.SlotX && x < carried.SlotX + rule.InventoryWidth && y >= carried.SlotY && y < carried.SlotY + rule.InventoryHeight)
                return carried;
        }
        return null;
    }

    /// <summary>BattleUnit::getRightHandWeapon and getLeftHandWeapon.</summary>
    private BattleItem? HandWeapon(BattleUnit unit, string hand) => _battle.InventoryOf(unit).FirstOrDefault(item => item.Slot == hand);

    /// <summary>BattleItem::setAmmoPreMission: loads the ammo into its slot when the slot is empty.</summary>
    public bool SetAmmoPreMission(BattleItem weapon, BattleItem ammo)
    {
        var slot = SlotForAmmo(weapon, ammo.ItemType);
        if (slot < 0 || weapon.AmmoSlots[slot] is not null) return false;
        // BattleItem::setAmmoForSlot: the ammo leaves its owner and section.
        if (weapon.IsWeaponWithAmmo && weapon.AmmoSlots[slot] != weapon.Id)
        {
            weapon.AmmoSlots[slot] = ammo.Id;
            MoveToOwner(ammo, null);
            ammo.Slot = "";
            ammo.IsAmmo = true;
        }
        return true;
    }

    /// <summary>RuleItem::getSlotForAmmo: the first slot whose compatible ammo lists the item, or -1.</summary>
    public int SlotForAmmo(BattleItem weapon, string ammoType)
    {
        var compatible = ItemRule(weapon.ItemType).CompatibleAmmo;
        for (var slot = 0; slot < Math.Min(compatible.Count, BattleItem.AmmoSlotCount); slot++)
            foreach (var handle in compatible[slot])
                if (_rules.Items.GetExternalId(handle) == ammoType) return slot;
        return -1;
    }

    /// <summary>BattleItem::haveAnyAmmo: loaded (an empty clip counts) for its aimed, auto or snap shot, or its hit for melee.</summary>
    public bool HaveAnyAmmo(BattleItem item)
    {
        if (!item.IsWeaponWithAmmo) return true;
        return ItemRule(item.ItemType).BattleType == Melee
            ? AmmoForAction(item, 3) is not null
            : AmmoForAction(item, 0) is not null || AmmoForAction(item, 1) is not null || AmmoForAction(item, 2) is not null;
    }

    /// <summary>BattleItem::haveAllAmmo: every ammo slot holds something (the weapon itself for a slot that takes none).</summary>
    public static bool HaveAllAmmo(BattleItem item) => item.AmmoSlots.All(static slot => slot is not null);

    /// <summary>BattleItem::getAmmoForAction (const): the item feeding an action, null when unloaded or spent.</summary>
    private BattleItem? AmmoForAction(BattleItem item, int action)
    {
        var slot = BattleItemRule(item.ItemType).ActionAmmoSlots[action];
        if (slot == SelfUse) return item;
        if (slot < 0 || slot >= BattleItem.AmmoSlotCount)
            throw new MapGenerationException($"Item {item.ItemType} names ammo slot {slot}.");
        if (item.AmmoSlots[slot] is not { } id) return null;
        var ammo = id == item.Id ? item : Find(id);
        return ammo.AmmoQuantity == 0 && !BattleItemRule(ammo.ItemType).AmmoRechargeable ? null : ammo;
    }

    /// <summary>BattleItem::getTotalWeight: the item and the ammo loaded in it.</summary>
    public int TotalWeight(BattleItem item)
    {
        var weight = ItemRule(item.ItemType).Weight;
        foreach (var slot in item.AmmoSlots)
            if (slot is { } id && id != item.Id) weight += ItemRule(Find(id).ItemType).Weight;
        return weight;
    }

    /// <summary>BattleUnit::getCarriedWeight.</summary>
    private int CarriedWeight(BattleUnit unit, RuntimeBattleArmor armor) =>
        Math.Max(0, armor.Weight + _battle.InventoryOf(unit).Sum(TotalWeight));

    private BattleItem Find(int id) => _battle.Items.First(item => item.Id == id);

    public RuntimeItemRule ItemRule(string itemType) => _rules.Items.TryGet(itemType, out var handle)
        ? _rules.Items[handle].Value : throw new MapGenerationException($"Item {itemType} does not exist.");

    private RuntimeBattleArmor Armor(string armorId) => _equipment.Armors.TryGetValue(armorId, out var armor)
        ? armor : throw new MapGenerationException($"Armor {armorId} does not exist.");

    /// <summary>Mod::getInventory with the required flag.</summary>
    private RuntimeBattleInventory Inventory(string id) => _equipment.Inventories.FirstOrDefault(inventory => inventory.Id == id)
        ?? throw new MapGenerationException($"Inventory {id} does not exist.");
}
