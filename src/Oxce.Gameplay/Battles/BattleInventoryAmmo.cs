using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The inventory's ammunition and fuse commands (Phase 7 branch 5 S8, third part): <c>Inventory::mouseClick</c>'s "put item in weapon" (a load, and the Shift quick swap),
/// <c>Inventory::unload</c> (the button, and the Shift quick unload) and the right click's priming and unpriming of the pre-battle inventory (<c>PrimeGrenadeState</c> in an
/// inventory). Each is planned (every check, nothing changed) and the plan applies itself and returns its events. User options at their defaults:
/// <c>oxceInventoryUnloadFixedWeapons</c> is off (an unload names its item). The sounds and the stacking of the ground are presentation.
/// </summary>
internal sealed partial class BattleState
{
    private static bool NeedsAmmoForSlot(BattleItem weapon, int slot) => weapon.IsWeaponWithAmmo && weapon.AmmoSlots[slot] != weapon.Id;

    /// <summary>
    /// BattleItem::setAmmoForSlot: only a slot the weapon needs ammo for takes it; the old ammo stops being ammo and is returned, the new one leaves its tile and owner
    /// and its section. A null ammo empties the slot.
    /// </summary>
    private BattleItem? SetAmmoForSlot(BattleItem weapon, int slot, BattleItem? ammo)
    {
        if (!NeedsAmmoForSlot(weapon, slot)) return null;
        var old = weapon.AmmoSlots[slot] is { } id ? FindItem(id) : null;
        if (old is not null) old.IsAmmo = false;
        weapon.AmmoSlots[slot] = ammo?.Id;
        if (ammo is not null)
        {
            MoveToOwner(ammo, null);
            ammo.Slot = "";
            ammo.IsSpecialWeapon = false;
            ammo.IsAmmo = true;
        }
        return old;
    }

    /// <summary>
    /// A section these commands cannot work without (the hands and the ground). Battle generation rejects rules that lack them
    /// (<c>Inventory X does not exist</c>), so a battle never has such rules: a missing one is a broken invariant, not a refusal.
    /// </summary>
    private RuntimeBattleInventory RequiredSection(string id) =>
        Section(id) ?? throw new InvalidOperationException($"The rules define no inventory section {id}, which battle generation requires.");

    /// <summary>The unit's item for an inventory command: carried or lying on its tile (<c>Inventory::_selItem</c> was picked from there).</summary>
    private BattleItem? InventoryItem(BattleUnit unit, int itemId) =>
        _items.Find(candidate => candidate.Id == itemId) is { } item && (IsHeld(unit, item) || IsOnTileOf(unit, item)) ? item : null;

    /// <summary>A grenade with a live fuse dropped to the ground sets the danger zone of the artificial intelligence (branch 6): refused until then.</summary>
    private bool SetsDangerZone(BattleItem item, RuntimeBattleInventory to) =>
        to.Type == RuntimeBattleInventory.Ground && item.FuseEnabled && RuleBattleType(item) == GrenadeBattleType;

    /// <summary>
    /// <c>Inventory::mouseClick</c>'s "put item in weapon": the ammunition <paramref name="selected"/> dropped on <paramref name="weapon"/>. The slot is the first whose compatible ammunition
    /// lists it; the cost is <c>tuLoad</c> (with the mod's extended reload cost, plus the move to the right hand of an item that is in no hand). A loaded slot refuses the drop, but
    /// with Shift it is the quick swap: the old clip costs <c>tuUnload</c> (and the drop to the ground when it goes there) and goes to the hand the new clip left, else the other free
    /// hand, else the ground (always the ground before the battle). In the battle only a weapon in a hand is swapped.
    /// </summary>
    private CampaignReason? PlanLoad(BattleUnit unit, BattleItem selected, BattleItem weapon, bool shift, out Func<IReadOnlyList<ICampaignEvent>>? load)
    {
        load = null;
        var slotAmmo = UnitEquipment.SlotForAmmo(RequiredRules, ItemRuleOf(weapon.ItemType), selected.ItemType);
        if (slotAmmo == -1) return BattleInventoryReasons.WrongAmmunition;
        var weaponRule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        var charges = InventoryChargesTimeUnits;
        var right = RequiredSection(RightHand);
        var left = RequiredSection(LeftHand);
        var ground = RequiredSection(GroundSlot);
        var cost = weaponRule.Combat.TimeUnitsToLoad[slotAmmo];
        var selectedSection = Section(selected.Slot) ?? ground;
        if (charges && RequiredRules.Campaign.ExtendedItemReloadCost && selectedSection is { Type: not RuntimeBattleInventory.Hand })
        {
            if (MoveToCost(selected, selectedSection, right) is not { } toHand) return BattleInventoryReasons.CostUnknown;
            cost += toHand;
        }

        var rightWeapon = HandItem(unit, right: true);
        var leftWeapon = HandItem(unit, right: false);
        var goesTo = ground;
        if (rightWeapon is null || rightWeapon.Id == selected.Id) goesTo = right;
        else if (leftWeapon is null || leftWeapon.Id == selected.Id) goesTo = left;

        var old = weapon.AmmoSlots[slotAmmo] is { } oldId ? FindItem(oldId) : null;
        if (old is not null)
        {
            var unloadCost = weaponRule.Combat.TimeUnitsToUnload[slotAmmo];
            if (!shift || charges && unloadCost == 0) return BattleInventoryReasons.AlreadyLoaded;
            if (!charges) goesTo = ground;
            else if (Section(weapon.Slot) is not { Type: RuntimeBattleInventory.Hand }) return BattleInventoryReasons.SwapNotHeld;
            cost += unloadCost;
            if (goesTo is { Type: RuntimeBattleInventory.Ground } && charges)
            {
                if (!HandCost(right, ground, out var drop)) return BattleInventoryReasons.CostUnknown;
                cost += drop;
            }
            if (SetsDangerZone(old, goesTo)) return BattleInventoryReasons.DangerZoneUnsupported;
        }
        if (charges && cost > unit.TimeUnits) return BattleInventoryReasons.NotEnoughTimeUnits;

        var spent = charges ? cost : 0;
        load = () =>
        {
            unit.TimeUnits -= spent;
            var replaced = SetAmmoForSlot(weapon, slotAmmo, selected);
            if (replaced is not null) Relocate(unit, replaced, goesTo!, 0, 0);
            MarkChanged();
            return [new InventoryAmmoLoaded(unit.Id, weapon.Id, selected.Id, slotAmmo, replaced?.Id ?? -1, replaced is null ? "" : goesTo!.Id, spent)];
        };
        return null;
    }

    /// <summary>
    /// <c>Inventory::unload</c> for the unit's item. A weapon gives up the ammunition of the first slot that has one (a slot with a zero <c>tuUnload</c> only in the battle) and a grenade
    /// that is primed, can be unprimed and has an unprime cost is unprimed. Before the battle a Shift click (<paramref name="shift"/>) is the quick unload: the weapon stays where it is and
    /// the ammunition drops to the ground (a grenade is unprimed where it lies). Otherwise the item goes to the first free hand and the ammunition to the second, or to the ground; in the
    /// battle that costs <c>tuUnload</c>, the drop from the hand, the item's move to the hand (when it is in none) and for a grenade the unprime's cost; a Shift click in the battle needs
    /// the item in a hand.
    /// </summary>
    internal CampaignReason? PlanUnload(int unitId, int itemId, bool shift, out Func<IReadOnlyList<ICampaignEvent>>? unload)
    {
        unload = null;
        if (InventoryAvailable(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (InventoryItem(unit, itemId) is not { } item) return BattleInventoryReasons.ItemUnknown;
        var charges = InventoryChargesTimeUnits;
        var ground = RequiredSection(GroundSlot);
        var itemSection = Section(item.Slot) ?? ground;
        var quick = false;
        if (shift)
        {
            if (!charges) quick = true;
            else if (itemSection.Type != RuntimeBattleInventory.Hand) return BattleInventoryReasons.UnloadNotHeld;
        }

        var rule = RequiredRules.Battle.Equipment.Items[item.ItemType];
        // Inventory::mouseClick never picks a fixed item up, so the button has no item to unload; only the Shift click (above the isFixed test) reaches it.
        if (!shift && rule.Fixed) return BattleInventoryReasons.ItemFixed;
        var battleType = ItemRuleOf(item.ItemType).BattleType;
        var grenade = battleType is RuntimeBattleItem.GrenadeBattleType or RuntimeBattleItem.ProximityGrenadeBattleType;
        var slotForUnload = -1;
        var unloadCost = 0;
        if (grenade)
        {
            if (item.FuseTimer == -1 || rule.FuseType == NoFuseType || rule.ActionCosts.Of(RuntimeItemActionCosts.Unprime).Cost.Time == 0)
                return BattleInventoryReasons.NothingToUnload;
        }
        else if (battleType is BattleTypeFirearm or BattleTypeMelee)
        {
            var showError = false;
            for (var slot = 0; slot < BattleItem.AmmoSlotCount && slotForUnload == -1; slot++)
            {
                if (!NeedsAmmoForSlot(item, slot)) continue;
                var tu = rule.Combat.TimeUnitsToUnload[slot];
                if (tu == 0 && !charges) continue;
                if (item.AmmoSlots[slot] is not null)
                {
                    unloadCost = tu;
                    slotForUnload = slot;
                }
                else showError = true;
            }
            if (slotForUnload == -1) return showError ? BattleInventoryReasons.NoAmmunitionLoaded : BattleInventoryReasons.NothingToUnload;
        }
        else
        {
            return BattleInventoryReasons.NothingToUnload;
        }

        if (quick)
        {
            if (!grenade && item.AmmoSlots[slotForUnload] is { } dropped && FindItem(dropped) is { } clip && SetsDangerZone(clip, ground!))
                return BattleInventoryReasons.DangerZoneUnsupported;
            unload = () =>
            {
                if (grenade)
                {
                    item.SetFuseTimer(-1, rule);
                    MarkChanged();
                    return [new InventoryItemUnloaded(unit.Id, item.Id, -1, item.Slot, "", 0), new ItemUnprimed(unit.Id, item.Id)];
                }
                var ammo = SetAmmoForSlot(item, slotForUnload, null)!;
                Relocate(unit, ammo, ground!, 0, 0);
                MarkChanged();
                return [new InventoryItemUnloaded(unit.Id, item.Id, ammo.Id, item.Slot, ground!.Id, 0)];
            };
            return null;
        }

        var right = RequiredSection(RightHand);
        var left = RequiredSection(LeftHand);
        RuntimeBattleInventory? first = right;
        RuntimeBattleInventory? second = left;
        foreach (var other in OrderedInventoryOf(unit))
        {
            if (other.Id == item.Id || Section(other.Slot) is not { Type: RuntimeBattleInventory.Hand } hand) continue;
            if (hand.Id == second?.Id) second = null;
            if (hand.Id == first?.Id) first = null;
        }
        if (first is null)
        {
            first = second;
            second = null;
        }
        if (first is null) return BattleInventoryReasons.HandsFull;

        var cost = grenade ? BattleActionCost.For(RequiredRules, BattleActionType.Unprime, unit, item) : new BattleActionCost(unit) { Weapon = item };
        if (charges)
        {
            if (!grenade)
            {
                cost.Time += unloadCost;
                if (second is null)
                {
                    if (!HandCost(first, ground, out var drop)) return BattleInventoryReasons.CostUnknown;
                    cost.Time += drop;
                }
            }
            if (cost.HaveTimeUnits() && itemSection.Type != RuntimeBattleInventory.Hand)
            {
                if (MoveToCost(item, itemSection, first) is not { } toHand) return BattleInventoryReasons.CostUnknown;
                cost.Time += toHand;
            }
            var shortfall = cost.Shortfall();
            // BattleActionCost::haveTU: an action with no time cost fails without a message.
            if (shortfall == BattleActionShortfall.NoAction) return BattleInventoryReasons.NothingToUnload;
            if (shortfall != BattleActionShortfall.None) return BattleAttackReasons.ForMessage(ShortfallMessage(shortfall));
        }
        if (!grenade)
        {
            var target = second ?? ground;
            if (item.AmmoSlots[slotForUnload] is { } held && FindItem(held) is { } ammoItem && SetsDangerZone(ammoItem, target))
                return BattleInventoryReasons.DangerZoneUnsupported;
        }

        var weaponTo = first;
        unload = () =>
        {
            if (charges) unit.SpendCost(cost);
            var spent = charges ? cost.Time : 0;
            Relocate(unit, item, weaponTo, 0, 0);
            if (grenade)
            {
                item.SetFuseTimer(-1, rule);
                MarkChanged();
                return [new InventoryItemUnloaded(unit.Id, item.Id, -1, weaponTo.Id, "", spent), new ItemUnprimed(unit.Id, item.Id)];
            }
            var ammo = SetAmmoForSlot(item, slotForUnload, null)!;
            var ammoTo = second ?? ground!;
            Relocate(unit, ammo, ammoTo, 0, 0);
            MarkChanged();
            return [new InventoryItemUnloaded(unit.Id, item.Id, ammo.Id, weaponTo.Id, ammoTo.Id, spent)];
        };
        return null;
    }

    /// <summary>
    /// The right click of the pre-battle inventory (<c>Inventory::mouseClick</c>, <c>!_tu</c>): an item with a fuse and a prime cost is primed (a settable fuse for the
    /// <paramref name="turns"/> of <c>PrimeGrenadeState</c>, 0 to 23; any other for its default), a primed one with an unprime cost is unprimed. No time units are spent and the
    /// unit's light is not changed. In the battle the right click closes the screen, and the action menu primes.
    /// </summary>
    internal CampaignReason? PlanInventoryPrime(int unitId, int itemId, int turns, out Func<IReadOnlyList<ICampaignEvent>>? prime)
    {
        prime = null;
        if (InventoryAvailable(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (InventoryChargesTimeUnits) return BattleInventoryReasons.PrimeInBattle;
        if (InventoryItem(unit, itemId) is not { } item) return BattleInventoryReasons.ItemUnknown;
        var rule = RequiredRules.Battle.Equipment.Items[item.ItemType];
        if (rule.FuseType == NoFuseType) return BattleInventoryReasons.ItemNeedsNoFuse;
        if (item.FuseTimer == -1)
        {
            if (rule.ActionCosts.Of(RuntimeItemActionCosts.Prime).Cost.Time <= 0) return BattleInventoryReasons.ItemNeedsNoFuse;
            var fuse = turns;
            if (rule.FuseType == SetFuseType)
            {
                if (fuse is < 0 or >= MaximumFuseTurns) return BattlePrimeReasons.TurnsInvalid;
            }
            else
            {
                fuse = FuseTimerDefault(rule);
                if (fuse < 0) return BattlePrimeReasons.FuseTypeUnknown;
            }
            prime = () =>
            {
                item.SetFuseTimer(fuse, rule);
                MarkChanged();
                return [new ItemPrimed(unit.Id, item.Id, fuse)];
            };
            return null;
        }
        if (rule.ActionCosts.Of(RuntimeItemActionCosts.Unprime).Cost.Time <= 0) return BattleInventoryReasons.ItemNeedsNoFuse;
        prime = () =>
        {
            item.SetFuseTimer(-1, rule);
            MarkChanged();
            return [new ItemUnprimed(unit.Id, item.Id)];
        };
        return null;
    }
}
