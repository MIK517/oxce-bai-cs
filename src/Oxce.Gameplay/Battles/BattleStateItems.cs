using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The BattleItem constructor and the items' capture and restore (Phase 7 branch 2 S4: <c>SavedBattleGame::load</c>'s item
/// passes and the units' second pass, <c>BattleItem::load</c> and <c>save</c>, <c>setAmmoForSlot</c>, <c>moveToOwner</c>).
/// </summary>
internal sealed partial class BattleState
{
    // RuleItem's BattleType values the constructor and save read.
    private const int FirearmType = 1;
    private const int MeleeType = 3;
    private const int MedikitType = 6;

    /// <summary>The battle's rules (Mod), which the save's conditions read; null for a battle built without content.</summary>
    internal RuntimeRuleCatalog? Rules { get; init; }

    private readonly List<BattleItem> _conditionalRecovered = [];

    /// <summary>SavedBattleGame::getConditionalRecoveredItems: items off the map that go home if the battle is won, by ID.</summary>
    public IReadOnlyList<BattleItem> ConditionalRecoveredItems => _conditionalRecovered;

    /// <summary>
    /// The BattleItem constructor: the rule's inventory move cost and clip size as the ammo, a medikit's quantities, and for
    /// a firearm or melee weapon the ammo slots without compatible ammo filled by the weapon itself (the others make it a
    /// weapon with ammo).
    /// </summary>
    internal static BattleItem ConstructItem(RuntimeRuleCatalog rules, int id, string itemType)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var rule = rules.Items.TryGet(itemType, out var handle) ? rules.Items[handle].Value
            : throw new Generation.MapGenerationException($"Item {itemType} does not exist.");
        var battleRule = rules.Battle.Equipment.Items.TryGetValue(itemType, out var equipment) ? equipment
            : throw new Generation.MapGenerationException($"Item {itemType} does not exist.");
        var item = new BattleItem(id, itemType)
        {
            InventoryMoveCostPercent = battleRule.InventoryMoveCostPercent,
            AmmoQuantity = rule.ClipSize,
        };
        if (rule.BattleType == MedikitType)
        {
            item.HealQuantity = battleRule.HealQuantity;
            item.PainKillerQuantity = battleRule.PainKillerQuantity;
            item.StimulantQuantity = battleRule.StimulantQuantity;
        }
        else if (rule.BattleType is FirearmType or MeleeType)
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
    /// BattleItem::moveToOwner: off its tile, and when the owner changes, setOwner (the old owner, none included, becomes
    /// the previous owner).
    /// </summary>
    internal static void MoveToOwner(BattleItem item, int? owner)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Tile = null;
        if (owner == item.OwnerUnitId) return;
        item.PreviousOwnerUnitId = item.OwnerUnitId;
        item.OwnerUnitId = owner;
    }

    /// <summary>An item of the battle or of either recovery list by ID, or null.</summary>
    internal BattleItem? FindItem(int id) =>
        _items.Find(item => item.Id == id) ?? _guaranteedRecovered.Find(item => item.Id == id) ?? _conditionalRecovered.Find(item => item.Id == id);

    /// <summary>
    /// SavedBattleGame::save's items: <c>items</c> and <c>itemsSpecial</c> split the battle's items (isSpecialWeapon: owned
    /// and in no inventory section) in their order, the recovery lists in theirs.
    /// </summary>
    private (ReadOnlyCollection<BattleItemSnapshot> Items, ReadOnlyCollection<BattleItemSnapshot> Special,
        ReadOnlyCollection<BattleItemSnapshot> Conditional, ReadOnlyCollection<BattleItemSnapshot> Guaranteed) CaptureItems()
    {
        static bool Special(BattleItem item) => item.OwnerUnitId is not null && item.Slot.Length == 0;
        return (Array.AsReadOnly(_items.Where(static item => !Special(item)).Select(CaptureItem).ToArray()),
            Array.AsReadOnly(_items.Where(Special).Select(CaptureItem).ToArray()),
            Array.AsReadOnly(_conditionalRecovered.Select(CaptureItem).ToArray()),
            Array.AsReadOnly(_guaranteedRecovered.Select(CaptureItem).ToArray()));
    }

    /// <summary>BattleItem::save's values for one item, with the rule values its conditions read.</summary>
    private BattleItemSnapshot CaptureItem(BattleItem item)
    {
        var battleRule = Rules?.Battle.Equipment.Items.GetValueOrDefault(item.ItemType);
        var battleType = Rules is not null && Rules.Items.TryGet(item.ItemType, out var handle) ? Rules.Items[handle].Value.BattleType : -1;
        // untilLastIf: the slots up to the last filled one, an empty slot as -1.
        var last = Array.FindLastIndex(item.AmmoSlots, static slot => slot is not null);
        return new BattleItemSnapshot(item.Id, item.ItemType)
        {
            OwnerId = item.OwnerUnitId ?? -1,
            PreviousOwnerId = item.PreviousOwnerUnitId ?? -1,
            UnitId = item.CorpseUnitId ?? -1,
            InventoryMoveCostPercent = item.InventoryMoveCostPercent,
            // RuleItem's default (100) for a battle built without content.
            RuleInventoryMoveCostPercent = battleRule?.InventoryMoveCostPercent ?? 100,
            Slot = item.Slot,
            SlotIsGrid = InventoryType(item.Slot) == RuntimeBattleInventory.Slot,
            SlotX = item.SlotX,
            SlotY = item.SlotY,
            Position = item.Tile,
            AmmoQuantity = item.AmmoQuantity,
            AmmoRechargeable = battleRule?.AmmoRechargeable ?? false,
            AmmoItem = item.AmmoSlots[0],
            AmmoItemSlots = Array.AsReadOnly(item.AmmoSlots.Take(last + 1).Select(static slot => slot ?? -1).ToArray()),
            Medikit = battleType == MedikitType,
            PainKiller = item.PainKillerQuantity,
            Heal = item.HealQuantity,
            Stimulant = item.StimulantQuantity,
            FuseTimer = item.FuseTimer,
            FuseEnabled = item.FuseEnabled,
            DroppedOnAlienTurn = item.DroppedOnAlienTurn,
            XcomProperty = item.XcomProperty,
            ScriptValues = Array.AsReadOnly((item.ScriptValues?.Capture() ?? []).ToArray()),
        };
    }

    // RuleInventory::getType of a section, or null for none or an unknown one.
    private int? InventoryType(string slot) =>
        slot.Length == 0 ? null : Rules?.Battle.Equipment.Inventories.FirstOrDefault(inventory => inventory.Id == slot)?.Type;

    /// <summary>
    /// SavedBattleGame::load's item passes, with the units' second pass between them. First pass, over <c>items</c>,
    /// <c>recoverConditional</c>, <c>recoverGuaranteed</c> and <c>itemsSpecial</c>: each item made by its constructor and
    /// BattleItem::load; its owner, previous owner and corpse unit found among the loaded units (findUnitById: none for a
    /// unit left out); an owned item in no inventory section joins its owner's special built-in items (addLoadedSpecialWeapon,
    /// at most four); a ground item with a position lies on that tile (outside the map, getTile's null is dereferenced:
    /// a corrupted save). The item counter is the highest ID plus one. Units' second pass: every unit that is neither dead
    /// nor ignored gets the special built-in items setSpecialWeapon gives on load. Second pass: <c>ammoItemSlots</c> loads
    /// each slot's item (an ID that names no item is ignored), else the older <c>ammoItem</c> loads slot 0 (an ID that names
    /// no item is a corrupted save: <c>itemIndex.at</c> throws). Each list is then sorted by ID.
    /// </summary>
    private void RestoreItems(BattleSnapshot snapshot, RuntimeContent content)
    {
        var rules = content.RuntimeRules;
        var units = _units.ToDictionary(static unit => unit.Id);
        int? Find(int id) => id != -1 && units.ContainsKey(id) ? id : null;
        (IReadOnlyList<BattleItemSnapshot> Saved, List<BattleItem> Target)[] lists =
        [
            (snapshot.Items, _items), (snapshot.RecoverConditional, _conditionalRecovered), (snapshot.RecoverGuaranteed, _guaranteedRecovered),
            (snapshot.ItemsSpecial, _items),
        ];
        var index = new Dictionary<int, BattleItem>();
        var highest = 0;
        foreach (var (saved, target) in lists)
            foreach (var entry in saved)
            {
                // OxceBattleSave.Read leaves out an item whose rule is gone (the load's continue).
                if (!rules.Items.TryGet(entry.ItemType, out _) || !rules.Battle.Equipment.Items.TryGetValue(entry.ItemType, out var battleRule))
                    throw new InvalidDataException($"Battle save item {entry.Id} names unknown item '{entry.ItemType}'.");
                var item = ConstructItem(rules, entry.Id, entry.ItemType);
                LoadItem(item, entry, battleRule, content);
                if (Find(entry.OwnerId) is { } owner)
                {
                    // setOwner (the previous owner, none, is set below), then a special built-in item or the inventory.
                    item.OwnerUnitId = owner;
                    if (item.Slot.Length == 0)
                    {
                        item.IsSpecialWeapon = true;
                        // addLoadedSpecialWeapon: the first free of the four slots; a fifth stays owned, in no slot (the reference logs it; the port does not).
                        var weapons = units[owner].SpecialWeaponIds;
                        if (weapons.Count < SpecialWeaponSlots) weapons.Add(item.Id);
                    }
                }
                item.PreviousOwnerUnitId = Find(entry.PreviousOwnerId);
                item.CorpseUnitId = Find(entry.UnitId);
                // getSlot()->getType() == INV_GROUND: the item lies on its tile; x -1 is none.
                if (InventoryType(item.Slot) == RuntimeBattleInventory.Ground && entry.Position is { } position)
                {
                    if (!Contains(position)) throw new InvalidOperationException($"Battle item {item.Id} lies at {position}, outside the map.");
                    item.Tile = position;
                }
                if (!index.TryAdd(item.Id, item)) throw new InvalidOperationException($"Battle item {item.Id} is listed twice.");
                highest = Math.Max(highest, item.Id);
                target.Add(item);
            }
        // _itemId++ after the highest ID (0 to start with).
        NextItemId = highest < int.MaxValue ? highest + 1
            : throw new InvalidOperationException($"Battle item ID {highest} leaves no ID for the next item.");
        var equipment = new Generation.UnitEquipment(this, rules, Seedless);
        foreach (var unit in _units)
            if (unit.Status is not (BattleUnitStatus.Dead or BattleUnitStatus.IgnoreMe))
                equipment.RestoreSpecialWeapons(unit, unit.SoldierId is null ? rules.Battle.Equipment.Units.GetValueOrDefault(unit.UnitType) : null,
                    unit.SoldierId is null ? "" : unit.UnitType);
        foreach (var (saved, _) in lists)
            foreach (var entry in saved)
            {
                var weapon = index[entry.Id];
                if (entry.AmmoItemSlots is { } slots)
                {
                    for (var slot = 0; slot < BattleItem.AmmoSlotCount; slot++)
                        if (slot < slots.Count && slots[slot] > -1 && index.TryGetValue(slots[slot], out var ammo)) SetAmmoForSlot(weapon, slot, ammo, index);
                }
                else if (entry.AmmoItem is { } legacy)
                {
                    SetAmmoForSlot(weapon, 0, index.TryGetValue(legacy, out var ammo) ? ammo
                        : throw new InvalidOperationException($"Battle item {entry.Id} 'ammoItem' names item {legacy}, which the battle does not have."), index);
                }
            }
        // Restore the order before the save (std::sort by ID).
        foreach (var list in new[] { _items, _conditionalRecovered, _guaranteedRecovered })
            list.Sort(static (left, right) => left.Id.CompareTo(right.Id));
    }

    /// <summary>BattleUnit::SPEC_WEAPON_MAX.</summary>
    private const int SpecialWeaponSlots = 4;

    /// <summary>BattleItem::load over the constructor's item: every saved value, the constructor's for a key the save leaves out.</summary>
    private void LoadItem(BattleItem item, BattleItemSnapshot saved, RuntimeBattleItem rule, RuntimeContent content)
    {
        item.InventoryMoveCostPercent = saved.InventoryMoveCostPercent ?? item.InventoryMoveCostPercent;
        if (saved.Slot.Length != 0 && InventoryType(saved.Slot) is null)
            throw new InvalidDataException($"Battle save item {saved.Id} is in unknown inventory section '{saved.Slot}'.");
        item.Slot = saved.Slot;
        item.SlotX = saved.SlotX;
        item.SlotY = saved.SlotY;
        item.AmmoQuantity = saved.AmmoQuantity ?? item.AmmoQuantity;
        item.PainKillerQuantity = saved.PainKiller ?? item.PainKillerQuantity;
        item.HealQuantity = saved.Heal ?? item.HealQuantity;
        item.StimulantQuantity = saved.Stimulant ?? item.StimulantQuantity;
        // fuseTimer through setFuseTimer (OXC's saves), then fuseEnabed.
        if (saved.FuseTimer is { } fuse) item.SetFuseTimer(fuse, rule);
        item.FuseEnabled = saved.FuseEnabled ?? item.FuseEnabled;
        item.DroppedOnAlienTurn = saved.DroppedOnAlienTurn;
        item.XcomProperty = saved.XcomProperty;
        // The script API always declares BattleItem.Tag, so every content has the owner type.
        var owner = content.Tags.Types.First(static candidate => candidate.Name == BattleItemSnapshot.ScriptOwner).Id;
        if (!ScriptValueState.TryRestore(content.Tags, owner, saved.ScriptValues, out var state, out var diagnostics))
            throw new InvalidDataException($"Battle save item {saved.Id}: " +
                string.Join(Environment.NewLine, diagnostics.Select(static entry => entry.Message)));
        item.ScriptValues = state;
    }

    /// <summary>
    /// BattleItem::setAmmoForSlot: only a weapon with ammo whose slot it does not fill itself takes ammo (needsAmmoForSlot);
    /// the old ammo stops being ammo, and the new one leaves its tile and owner (moveToOwner(nullptr)) and its section.
    /// </summary>
    private static void SetAmmoForSlot(BattleItem weapon, int slot, BattleItem ammo, Dictionary<int, BattleItem> items)
    {
        if (!weapon.IsWeaponWithAmmo || weapon.AmmoSlots[slot] == weapon.Id) return;
        if (weapon.AmmoSlots[slot] is { } old && items.TryGetValue(old, out var oldAmmo)) oldAmmo.IsAmmo = false;
        weapon.AmmoSlots[slot] = ammo.Id;
        MoveToOwner(ammo, null);
        ammo.Slot = "";
        ammo.IsSpecialWeapon = false;
        ammo.IsAmmo = true;
    }
}
