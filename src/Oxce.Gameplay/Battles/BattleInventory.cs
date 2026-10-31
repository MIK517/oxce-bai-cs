using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>A checked inventory move: nothing has changed yet.</summary>
internal sealed record InventoryMove(BattleUnit Unit, BattleItem Item, RuntimeBattleInventory From, RuntimeBattleInventory To, int X, int Y, int Cost);

/// <summary>
/// The inventory (Phase 7 branch 3 S4): <c>Inventory::mouseClick</c>'s pick up and drop, <c>fitItem</c>, <c>overlapItems</c>,
/// <c>RuleInventory::fitItemInSlot</c>, <c>BattleItem::getMoveToCost</c>, <c>BattleItem::occupiesSlot</c>,
/// <c>TileEngine::itemMoveInventory</c> and <c>InventoryState::saveEquipmentLayout</c>.
/// </summary>
internal sealed partial class BattleState
{
    private long _arrivalSequence;

    /// <summary>The next arrival number: Tile::addItem and BattleUnit::getInventory().push_back append.</summary>
    internal long NextArrival() => ++_arrivalSequence;

    /// <summary>
    /// Tile::addItem: the item lies on the tile after the others; one that carries a unit swaps places with the first when the tile
    /// holds others (the floor drawing optimisation). The tile's items are renumbered in their new order.
    /// </summary>
    internal void AddToTile(BattleItem item, BattlePosition tile)
    {
        var list = ItemsOnTile(tile).Where(other => other.Id != item.Id).ToList();
        item.Tile = tile;
        list.Add(item);
        if (item.CorpseUnitId is not null && list.Count > 1) (list[0], list[^1]) = (list[^1], list[0]);
        foreach (var member in list) member.ArrivalOrder = NextArrival();
    }

    /// <summary>BattleUnit::getInventory in the reference's order: the items in sections, by arrival (creation order until moved).</summary>
    internal IEnumerable<BattleItem> OrderedInventoryOf(BattleUnit unit) =>
        InventoryOf(unit).OrderBy(static item => item.ArrivalOrder);

    /// <summary>Tile::getInventory in the reference's order: the items lying on the tile.</summary>
    internal IEnumerable<BattleItem> ItemsOnTile(BattlePosition tile) =>
        _items.Where(item => item.Tile == tile && item.Slot == GroundSlot).OrderBy(static item => item.ArrivalOrder);

    private static RuntimeBattleInventory? Section(RuntimeRuleCatalog rules, string id) =>
        rules.Battle.Equipment.Inventories.FirstOrDefault(inventory => inventory.Id == id);

    private static RuntimeBattleInventory? Ground(RuntimeRuleCatalog rules) =>
        Section(rules, GroundSlot);

    /// <summary>The inventory screen is open to a unit: after the briefing, in the battle on X-COM's turn, for a live X-COM unit on the map with an inventory.</summary>
    internal CampaignReason? InventoryAvailable(RuntimeRuleCatalog rules, int unitId, out BattleUnit? unit)
    {
        unit = null;
        if (Phase == BattlePhase.Briefing || Phase == BattlePhase.InProgress && Side != BattleFaction.Player)
            return BattleInventoryReasons.Unavailable;
        unit = _units.Find(candidate => candidate.Id == unitId);
        if (unit is null || unit.Faction != BattleFaction.Player || unit.IsOut || unit.Position is null ||
            !rules.Battle.Equipment.Armors.TryGetValue(unit.ArmorId, out var armor) || !armor.HasInventory)
        {
            unit = null;
            return BattleInventoryReasons.UnitUnavailable;
        }
        return null;
    }

    /// <summary>Whether the moves cost time units: the battle's inventory does (<c>_tu</c>), the pre-battle one does not.</summary>
    internal bool InventoryChargesTimeUnits => Phase == BattlePhase.InProgress;

    /// <summary>BattleItem::occupiesSlot(x, y) and occupiesSlot(x, y, item): a held item fills its hand; a grid item its rectangle.</summary>
    private static bool Occupies(RuntimeRuleCatalog rules, BattleItem holder, int x, int y, BattleItem? other)
    {
        if (other is not null && other.Id == holder.Id || holder.Slot.Length == 0) return false;
        if (Section(rules, holder.Slot) is { Type: RuntimeBattleInventory.Hand }) return true;
        var rule = rules.Battle.Equipment.Items[holder.ItemType];
        if (other is null)
            return x >= holder.SlotX && x < holder.SlotX + rule.InventoryWidth && y >= holder.SlotY && y < holder.SlotY + rule.InventoryHeight;
        var otherRule = rules.Battle.Equipment.Items[other.ItemType];
        return !(x >= holder.SlotX + rule.InventoryWidth || x + otherRule.InventoryWidth <= holder.SlotX ||
            y >= holder.SlotY + rule.InventoryHeight || y + otherRule.InventoryHeight <= holder.SlotY);
    }

    /// <summary>BattleUnit::getItem(slot, x, y) for a section that is not the ground.</summary>
    private BattleItem? ItemAt(RuntimeRuleCatalog rules, BattleUnit unit, RuntimeBattleInventory slot, int x, int y) =>
        OrderedInventoryOf(unit).FirstOrDefault(candidate => candidate.Slot == slot.Id && Occupies(rules, candidate, x, y, null));

    /// <summary>Inventory::overlapItems for a section that is not the ground.</summary>
    private bool Overlaps(RuntimeRuleCatalog rules, BattleUnit unit, BattleItem item, RuntimeBattleInventory slot, int x, int y) =>
        OrderedInventoryOf(unit).Any(candidate => candidate.Slot == slot.Id && Occupies(rules, candidate, x, y, item));

    /// <summary>RuleInventory::fitItemInSlot for a hand or a grid section: every cell of the item's rectangle is a slot.</summary>
    private static bool FitsInSlot(RuntimeBattleInventory slot, RuntimeBattleItem item, int x, int y)
    {
        if (slot.Type != RuntimeBattleInventory.Slot) return true;
        var total = item.InventoryWidth * item.InventoryHeight;
        var found = 0;
        foreach (var cell in slot.Slots)
        {
            if (found >= total) break;
            if (cell.X >= x && cell.X < x + item.InventoryWidth && cell.Y >= y && cell.Y < y + item.InventoryHeight) found++;
        }
        return found == total;
    }

    /// <summary>RuleItem::canBePlacedIntoInventorySection: no list allows every section; the ground is always allowed.</summary>
    private static bool CanBePlaced(RuntimeBattleItem item, RuntimeBattleInventory slot) =>
        item.SupportedInventorySections.Count == 0 || slot.Type == RuntimeBattleInventory.Ground || item.SupportedInventorySections.Contains(slot.Id);

    /// <summary>
    /// BattleItem::getMoveToCost: the cost between two sections (none within one), free stays free, a drop from a hand costs the
    /// section's cost, else the item's move cost percentage (at least 1). Null when the table has no entry (the reference reads past it).
    /// </summary>
    private static int? MoveToCost(BattleItem item, RuntimeBattleInventory from, RuntimeBattleInventory to)
    {
        int cost;
        if (from.Id == to.Id) cost = 0;
        else if (!from.Costs.TryGetValue(to.Id, out cost)) return null;
        if (cost == 0) return 0;
        if (from.Type == RuntimeBattleInventory.Hand && to.Type == RuntimeBattleInventory.Ground) return cost;
        return Math.Max(1, cost * item.InventoryMoveCostPercent / 100);
    }

    private static bool IsHeld(BattleUnit unit, BattleItem item) =>
        item.OwnerUnitId == unit.Id && item.Slot.Length != 0 && !item.IsSpecialWeapon;

    private static bool IsOnTileOf(BattleUnit unit, BattleItem item) =>
        item.OwnerUnitId is null && item.Tile is { } tile && tile == unit.Position && item.Slot == GroundSlot;

    /// <summary>
    /// The pick-up and drop of <c>Inventory::mouseClick</c>, checked: the item must be the unit's or lie on its tile and not be
    /// fixed; the section must exist and take the item; a grid or hand position must be free and fit (an occupied position with a weapon
    /// that takes ammo would load it: refused as unported); the move must be affordable in the battle; an enabled grenade's drop
    /// is refused for the danger zone it sets. The ground takes any item at once.
    /// </summary>
    internal CampaignReason? PlanMove(RuntimeRuleCatalog rules, int unitId, int itemId, string slotId, int x, int y, out InventoryMove? move)
    {
        move = null;
        if (InventoryAvailable(rules, unitId, out var unit) is { } closed) return closed;
        var item = _items.Find(candidate => candidate.Id == itemId);
        if (item is null || !IsHeld(unit!, item) && !IsOnTileOf(unit!, item)) return BattleInventoryReasons.ItemUnknown;
        var itemRule = rules.Battle.Equipment.Items[item.ItemType];
        if (itemRule.Fixed) return BattleInventoryReasons.ItemFixed;
        if (Section(rules, slotId) is not { } to) return BattleInventoryReasons.SlotUnknown;
        var from = Section(rules, item.Slot) ?? Ground(rules)!;
        if (to.Type == RuntimeBattleInventory.Ground)
        {
            x = 0;
            y = 0;
        }
        else
        {
            if (!CanBePlaced(itemRule, to)) return BattleInventoryReasons.SectionUnsupported;
            if (ItemAt(rules, unit!, to, x, y) is { } target && target.Id != item.Id)
                return target.IsWeaponWithAmmo ? BattleInventoryReasons.AmmoUnsupported : BattleInventoryReasons.NoRoom;
            if (Overlaps(rules, unit!, item, to, x, y) || !FitsInSlot(to, itemRule, x, y)) return BattleInventoryReasons.NoRoom;
        }
        // The cost is read only where it is charged (the reference's `!_tu || spendTimeUnits(...)`).
        var cost = 0;
        if (InventoryChargesTimeUnits)
        {
            if (MoveToCost(item, from, to) is not { } charged) return BattleInventoryReasons.CostUnknown;
            if (charged > unit!.TimeUnits) return BattleInventoryReasons.NotEnoughTimeUnits;
            cost = charged;
        }
        if (to.Type == RuntimeBattleInventory.Ground && from.Type != RuntimeBattleInventory.Ground &&
            RuleBattleType(rules, item) == GrenadeBattleType && item.FuseEnabled)
            return BattleInventoryReasons.DangerZoneUnsupported;
        move = new InventoryMove(unit!, item, from, to, x, y, cost);
        return null;
    }

    private static int RuleBattleType(RuntimeRuleCatalog rules, BattleItem item) =>
        rules.Items.TryGet(item.ItemType, out var handle) ? rules.Items[handle].Value.BattleType : 0;

    /// <summary>
    /// TileEngine::itemMoveInventory: to the ground the item leaves its owner and lies on the unit's tile (an unconscious unit it
    /// is lands there); from the ground (or from nowhere) it joins the unit (and an unconscious unit it is leaves the map); between
    /// sections only the section and position change. The time units are spent first, as the reference does.
    /// </summary>
    internal void ApplyMove(InventoryMove move)
    {
        var (unit, item, _, to, x, y, cost) = move;
        unit.TimeUnits -= cost;
        if (to.Id != item.Slot)
        {
            if (to.Type == RuntimeBattleInventory.Ground)
            {
                MoveToOwner(item, null);
                AddToTile(item, unit.Position!.Value);
                // TileEngine::itemMoveInventory: the dropper (the unit on the tile) learns the tile as a clue (updateEnemyKnowledge).
                var dropper = Map.UnitAt(Map.TileIndex(unit.Position.Value)) is { } standing ? _units.Find(candidate => candidate.Id == standing) : null;
                dropper?.UpdateEnemyKnowledge(Map.TileIndex(unit.Position.Value));
                if (item.CorpseUnitId is { } corpse && _units.Find(candidate => candidate.Id == corpse) is { Status: BattleUnitStatus.Unconscious } lying)
                    lying.Position = unit.Position;
            }
            else if (item.Slot.Length == 0 || item.Slot == GroundSlot)
            {
                MoveToOwner(item, unit.Id);
                item.ArrivalOrder = NextArrival();
                item.DroppedOnAlienTurn = false;
                if (item.CorpseUnitId is { } corpse && _units.Find(candidate => candidate.Id == corpse) is { Status: BattleUnitStatus.Unconscious } carried)
                    carried.Position = null;
            }
        }
        item.Slot = to.Id;
        item.SlotX = x;
        item.SlotY = y;
        Revision++;
    }

    private BattleItem? HandItem(RuntimeRuleCatalog rules, BattleUnit unit, bool right) =>
        OrderedInventoryOf(unit).FirstOrDefault(item => Section(rules, item.Slot) is { } section && (right ? section.IsRightHand : section.IsLeftHand));

    private static RuntimeItemRule? ItemRule(RuntimeRuleCatalog rules, BattleItem item) =>
        rules.Items.TryGet(item.ItemType, out var handle) ? rules.Items[handle].Value : null;

    /// <summary>
    /// The Ctrl click of <c>Inventory::mouseClick</c>. A carried item drops to the ground. A ground item is tried in the smart
    /// choice's cheapest section (when asked), else its default section, the first of its category's inventory order, and the
    /// vanilla section for its battle type; then the alphabetical order of all sections. With the mod's extended slot sorting the last fallback follows the list order.
    /// </summary>
    internal CampaignReason? PlanAutoPlace(RuntimeRuleCatalog rules, int unitId, int itemId, bool smart, out InventoryMove? result)
    {
        result = null;
        if (InventoryAvailable(rules, unitId, out var unit) is { } closed) return closed;
        var item = _items.Find(candidate => candidate.Id == itemId);
        if (item is null || !IsHeld(unit!, item) && !IsOnTileOf(unit!, item)) return BattleInventoryReasons.ItemUnknown;
        var itemRule = rules.Battle.Equipment.Items[item.ItemType];
        if (itemRule.Fixed) return BattleInventoryReasons.ItemFixed;
        var ground = Ground(rules);
        if (ground is null) return BattleInventoryReasons.SlotUnknown;
        if (item.Slot != GroundSlot) return PlanMove(rules, unitId, itemId, ground.Id, 0, 0, out result);

        // The plan is made on a copy of the choice only: the reference moves the item as soon as a section takes it, so the move itself
        // is applied by the caller through the returned plan.
        var warning = BattleInventoryReasons.NoRoom;
        var sections = rules.Battle.Equipment.Inventories.Where(static section => section.Type != RuntimeBattleInventory.Ground)
            .OrderBy(static section => section.Id, StringComparer.Ordinal).ToArray();
        var from = Section(rules, item.Slot) ?? ground;
        RuntimeBattleInventory? chosen = null;
        InventoryMove? Try(RuntimeBattleInventory slot)
        {
            if (!CanBePlaced(itemRule, slot))
            {
                warning = BattleInventoryReasons.SectionUnsupported;
                return null;
            }
            var maxX = slot.Slots.Count == 0 ? 0 : Math.Max(0, slot.Slots.Max(static cell => cell.X));
            var maxY = slot.Slots.Count == 0 ? 0 : Math.Max(0, slot.Slots.Max(static cell => cell.Y));
            for (var y = 0; y <= maxY; y++)
                for (var x = 0; x <= maxX; x++)
                {
                    if (Overlaps(rules, unit!, item, slot, x, y) || !FitsInSlot(slot, itemRule, x, y)) continue;
                    var cost = 0;
                    if (InventoryChargesTimeUnits)
                    {
                        if (MoveToCost(item, from, slot) is not { } charged)
                        {
                            warning = BattleInventoryReasons.CostUnknown;
                            continue;
                        }
                        if (charged > unit!.TimeUnits)
                        {
                            warning = BattleInventoryReasons.NotEnoughTimeUnits;
                            continue;
                        }
                        cost = charged;
                    }
                    return new InventoryMove(unit!, item, from, slot, x, y, cost);
                }
            return null;
        }

        if (smart)
        {
            var right = Section(rules, "STR_RIGHT_HAND");
            var left = Section(rules, "STR_LEFT_HAND");
            var rightItem = HandItem(rules, unit!, right: true);
            var leftItem = HandItem(rules, unit!, right: false);
            var cheapest = int.MaxValue;
            foreach (var section in sections)
            {
                if (Try(section) is null) continue;
                if (right is null || left is null) return BattleInventoryReasons.SlotUnknown;
                if (!HandCost(section, right, out var toRight) || !HandCost(section, left, out var toLeft)) return BattleInventoryReasons.CostUnknown;
                var cost = Math.Min(toRight, toLeft);
                if (section.IsLeftHand && rightItem is not null && ItemRule(rules, rightItem) is { BlockingBothHands: true } ||
                    section.IsRightHand && leftItem is not null && ItemRule(rules, leftItem) is { BlockingBothHands: true })
                    continue;
                if (section.IsLeftHand && rightItem is not null && ItemRule(rules, rightItem) is { TwoHanded: true } ||
                    section.IsRightHand && leftItem is not null && ItemRule(rules, leftItem) is { TwoHanded: true })
                    cost += 10;
                if (cost <= cheapest)
                {
                    cheapest = cost;
                    chosen = section;
                }
            }
            if (chosen is null)
            {
                // No section: the item "moves" to the ground it lies on, which costs nothing and changes nothing.
                return null;
            }
            if (Try(chosen) is { } direct)
            {
                result = direct;
                return null;
            }
        }
        else
        {
            InventoryMove? placed = null;
            if (itemRule.DefaultInventorySlot.Length != 0 && Section(rules, itemRule.DefaultInventorySlot) is { Type: not RuntimeBattleInventory.Ground } preferred)
                placed = Try(preferred);
            if (placed is null)
                foreach (var name in CategoryInvOrder(rules, itemRule))
                {
                    if (Section(rules, name) is not { } section || section.Type == RuntimeBattleInventory.Ground) continue;
                    if ((placed = Try(section)) is not null) break;
                }
            if (placed is null)
            {
                var vanilla = (RuleBattleType(rules, item)) switch
                {
                    1 => "STR_RIGHT_HAND",
                    8 or 9 or 3 or 11 => "STR_LEFT_HAND",
                    _ => itemRule.InventoryHeight > 2 ? "STR_BACK_PACK" : "STR_BELT",
                };
                if (Section(rules, vanilla) is { } section && section.Type != RuntimeBattleInventory.Ground) placed = Try(section);
            }
            if (placed is null)
                foreach (var section in rules.Campaign.ExtendedInventorySlotSorting
                    ? rules.Battle.Equipment.Inventories.Where(static section => section.Type != RuntimeBattleInventory.Ground) : sections)
                    if ((placed = Try(section)) is not null) break;
            if (placed is not null)
            {
                result = placed;
                return null;
            }
        }
        return warning;
    }

    /// <summary>RuleInventory::getCost to a hand: 0 within the hand; a section without the entry is false (the reference reads past its table; refused as cost-unknown).</summary>
    private static bool HandCost(RuntimeBattleInventory section, RuntimeBattleInventory hand, out int cost)
    {
        cost = 0;
        return section.Id == hand.Id || section.Costs.TryGetValue(hand.Id, out cost);
    }

    private static IEnumerable<string> CategoryInvOrder(RuntimeRuleCatalog rules, RuntimeBattleItem item)
    {
        foreach (var category in item.Categories)
            if (rules.Battle.Equipment.CategoryInventoryOrders.TryGetValue(category, out var order)) return order;
        return [];
    }

    /// <summary>
    /// InventoryState::saveEquipmentLayout: each X-COM soldier's unit keeps its inventory as the soldier's layout. A fixed weapon is
    /// kept only for the ammunition it holds in its first slot; other items only when the mod lets them go on a craft.
    /// </summary>
    internal IReadOnlyDictionary<int, IReadOnlyList<Campaigns.SoldierLayoutItem>> SaveEquipmentLayout(RuntimeRuleCatalog rules)
    {
        var layouts = new Dictionary<int, IReadOnlyList<Campaigns.SoldierLayoutItem>>();
        foreach (var unit in _units)
        {
            if (unit.SoldierId is not { } soldier) continue;
            var entries = new List<Campaigns.SoldierLayoutItem>();
            foreach (var item in OrderedInventoryOf(unit))
            {
                var rule = rules.Battle.Equipment.Items[item.ItemType];
                if (rule.Fixed)
                {
                    if (!(item.IsWeaponWithAmmo && item.AmmoSlots[0] != item.Id && item.AmmoSlots[0] is not null)) continue;
                }
                else if (rule.IgnoreInCraftEquip)
                {
                    continue;
                }
                var ammo = new string[BattleItem.AmmoSlotCount];
                for (var slot = 0; slot < ammo.Length; slot++)
                    ammo[slot] = item.IsWeaponWithAmmo && item.AmmoSlots[slot] is { } id && id != item.Id && FindItem(id) is { } loaded ? loaded.ItemType : "";
                entries.Add(new Campaigns.SoldierLayoutItem(item.ItemType, item.Slot, item.SlotX, item.SlotY, ammo, item.FuseTimer, rule.Fixed));
            }
            layouts[soldier] = entries;
        }
        return layouts;
    }

    /// <summary>The unit's inventory screen as the owning faction sees it.</summary>
    internal BattleInventoryView? InventoryView(RuntimeRuleCatalog rules, BattleViewer viewer, int unitId)
    {
        if (viewer.Faction != BattleFaction.Player || InventoryAvailable(rules, unitId, out var unit) is not null) return null;
        BattleInventoryItemView View(BattleItem item)
        {
            var rule = rules.Battle.Equipment.Items[item.ItemType];
            var loaded = new List<string>();
            for (var slot = 0; slot < item.AmmoSlots.Length; slot++)
                loaded.Add(item.IsWeaponWithAmmo && item.AmmoSlots[slot] is { } id && id != item.Id && FindItem(id) is { } ammo ? ammo.ItemType : "");
            return new BattleInventoryItemView(item.Id, item.ItemType, item.Slot, item.SlotX, item.SlotY, rule.InventoryWidth, rule.InventoryHeight,
                item.FuseTimer, item.AmmoQuantity, loaded.AsReadOnly(), rule.Fixed);
        }
        var slots = rules.Battle.Equipment.Inventories.Select(static section => new BattleInventorySlotView(section.Id, section.Type,
            section.Slots.Select(static cell => new BattleInventoryCell(cell.X, cell.Y)).ToArray())).ToArray();
        return new BattleInventoryView(unitId, unit!.TimeUnits, InventoryChargesTimeUnits, slots,
            OrderedInventoryOf(unit).Select(View).ToArray(), ItemsOnTile(unit.Position!.Value).Select(View).ToArray());
    }
}
