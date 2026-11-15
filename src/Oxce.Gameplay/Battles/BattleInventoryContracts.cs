using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// <c>Inventory::mouseClick</c>'s drop: a unit's item goes to a position of an inventory section (a hand, a grid slot, or
/// the ground of the unit's tile). Before the first turn it costs nothing; in the battle it costs the section's time units.
/// A ground drop ignores the position: the ground holds any number of items and the screen arranges them. A drop on a position held by a weapon that takes
/// the item as ammunition loads it (<c>Inventory::mouseClick</c>'s "put item in weapon", with the cost of <c>tuLoad</c>); with <paramref name="Shift"/> over a loaded weapon
/// it swaps the clips (the quick swap). The port's ground has no positions, so a drop on the ground names the item it lands on:
/// <paramref name="OntoItemId"/> is a ground item of the unit's tile; a weapon with ammunition that the dropped item cannot stack with takes it as a load (the same
/// load as for a carried weapon, with <c>Shift</c> the quick swap, which in the battle needs the weapon in a hand); without it a drop on the ground is a plain drop.
/// </summary>
public sealed record MoveInventoryItem(int UnitId, int ItemId, string Slot, int SlotX, int SlotY, bool Shift = false, int? OntoItemId = null) : IBattleCommand, IUnitCommand;

/// <summary>
/// <c>Inventory::mouseClick</c>'s Ctrl click: a ground item goes to the best section that holds it (the cheapest to reach
/// a hand when <paramref name="Smart"/>, the client's <c>oxceSmartCtrlEquip</c>), a carried item drops to the ground.
/// </summary>
public sealed record AutoPlaceInventoryItem(int UnitId, int ItemId, bool Smart) : IBattleCommand, IUnitCommand;

/// <summary>What an inventory screen can ask for beyond the moves: the unload and the pre-battle priming run, the others are refused by their own names.</summary>
public enum InventoryFeature
{
    /// <summary>Unloading a weapon or unpriming a grenade (<c>Inventory::unload</c>: the button, or Shift on the item with <c>Shift</c>).</summary>
    UnloadAmmo,

    /// <summary>The right click of the pre-battle inventory: primes an item with a fuse (for <c>Turns</c> when the fuse is settable) or unprimes a primed one.</summary>
    Prime,

    /// <summary>Saving a unit's layout under a name (<c>InventorySaveState</c>).</summary>
    SaveLayout,

    /// <summary>Loading a saved layout (<c>InventoryLoadState</c>).</summary>
    LoadLayout,

    /// <summary>Clearing the unit's inventory to the ground (<c>InventoryState::onClearInventory</c>).</summary>
    ClearInventory,

    /// <summary>Equipping the unit automatically (<c>InventoryState::onAutoequip</c>).</summary>
    AutoEquip,

    /// <summary>Applying a global or personal equipment template, or creating a personal one.</summary>
    Template,

    /// <summary>Renaming the soldier from the inventory screen.</summary>
    RenameSoldier,
}

/// <summary>
/// A feature of the inventory screen: the unload and the pre-battle priming run; the others arrive later and are always refused by the feature's name.
/// <paramref name="Shift"/> is the Shift click of an unload (the quick unload), <paramref name="Turns"/> the turns of a settable fuse.
/// </summary>
public sealed record UseInventoryFeature(int UnitId, int ItemId, InventoryFeature Feature, bool Shift = false, int Turns = 0) : IBattleCommand, IUnitCommand;

/// <summary>An item moved between sections, or to or from the ground.</summary>
public sealed record InventoryItemMoved(int UnitId, int ItemId, string FromSlot, string ToSlot, int SlotX, int SlotY, int TimeUnitsSpent) : ICampaignEvent;

/// <summary>
/// An ammunition item loaded into a weapon's slot. A clip it replaced (quick swap) is <paramref name="ReplacedItemId"/> (-1 for none) and went to
/// <paramref name="ReplacedTo"/>, a section.
/// </summary>
public sealed record InventoryAmmoLoaded(int UnitId, int WeaponId, int AmmoId, int AmmoSlot, int ReplacedItemId, string ReplacedTo, int TimeUnitsSpent) : ICampaignEvent;

/// <summary>
/// A weapon unloaded: its ammunition <paramref name="AmmoId"/> went to the section <paramref name="AmmoTo"/> and the weapon is in <paramref name="WeaponSlot"/>.
/// A grenade is unprimed instead (an <see cref="ItemUnprimed"/> follows; AmmoId is -1).
/// </summary>
public sealed record InventoryItemUnloaded(int UnitId, int WeaponId, int AmmoId, string WeaponSlot, string AmmoTo, int TimeUnitsSpent) : ICampaignEvent;

/// <summary>An item as the inventory screen draws it: its section and position, size and what it holds.</summary>
public sealed record BattleInventoryItemView(
    int ItemId,
    string ItemType,
    string Slot,
    int SlotX,
    int SlotY,
    int Width,
    int Height,
    int FuseTimer,
    int AmmoQuantity,
    IReadOnlyList<string> LoadedAmmo,
    bool Fixed);

/// <summary>A grid cell of a section.</summary>
public readonly record struct BattleInventoryCell(int X, int Y);

/// <summary>An inventory section: <see cref="Type"/> is 0 for a grid, 1 for a hand and 2 for the ground.</summary>
public sealed record BattleInventorySlotView(string Id, int Type, IReadOnlyList<BattleInventoryCell> Cells);

/// <summary>A unit's inventory screen: sections, carried items and the items on its tile.</summary>
public sealed record BattleInventoryView(
    int UnitId,
    int TimeUnits,
    bool ChargesTimeUnits,
    IReadOnlyList<BattleInventorySlotView> Slots,
    IReadOnlyList<BattleInventoryItemView> Carried,
    IReadOnlyList<BattleInventoryItemView> Ground);

/// <summary>What the inventory screen reads (ADR 0038: a viewer sees only its own faction's inventory).</summary>
public interface IBattleInventoryQuery
{
    /// <summary>The unit's inventory, or null when the viewer's faction does not own the unit or the inventory is not open to it.</summary>
    BattleInventoryView? QueryInventory(BattleViewer viewer, int unitId);

    /// <summary>The refusal code a move would get, or null when it would go through; nothing changes.</summary>
    string? CheckInventoryMove(BattleViewer viewer, int unitId, int itemId, string slot, int slotX, int slotY);
}

/// <summary>The reasons the inventory refuses; the ported ones follow the reference's warnings, the rest name the owner.</summary>
internal static class BattleInventoryReasons
{
    internal static readonly CampaignReason Unavailable = new("battle.inventory-unavailable",
        "The inventory opens after the briefing, and in the battle only on X-COM's turn.");

    internal static readonly CampaignReason UnitUnavailable = new("battle.inventory-unit-unavailable",
        "The unit is not a live X-COM unit on the map whose armor has an inventory.");

    internal static readonly CampaignReason ItemUnknown = new("battle.inventory-item-unknown",
        "The item is neither carried by the unit nor lying on its tile.");

    internal static readonly CampaignReason ItemFixed = new("battle.inventory-item-fixed",
        "A fixed item cannot be moved.");

    internal static readonly CampaignReason SlotUnknown = new("battle.inventory-slot-unknown",
        "The mod has no such inventory section.");

    internal static readonly CampaignReason SectionUnsupported = new("battle.inventory-section-unsupported",
        "The item cannot be placed into this inventory section (STR_CANNOT_PLACE_ITEM_INTO_THIS_SECTION).");

    internal static readonly CampaignReason NoRoom = new("battle.inventory-no-room",
        "The item does not fit there, or another item is in the way (STR_NOT_ENOUGH_SPACE).");

    internal static readonly CampaignReason NotEnoughTimeUnits = new("battle.inventory-not-enough-time-units",
        "The unit has too few time units for the move (STR_NOT_ENOUGH_TIME_UNITS).");

    internal static readonly CampaignReason CostUnknown = new("battle.inventory-cost-unknown",
        "The inventory sections define no cost for this move; the reference reads past the end of its cost table.");

    internal static readonly CampaignReason WrongAmmunition = new("battle.inventory-ammo-wrong",
        "The item is no ammunition for this weapon (STR_WRONG_AMMUNITION_FOR_THIS_WEAPON).")
    { TextKey = "STR_WRONG_AMMUNITION_FOR_THIS_WEAPON" };

    internal static readonly CampaignReason AlreadyLoaded = new("battle.inventory-ammo-already-loaded",
        "The weapon is loaded; a Shift drop swaps the clips (STR_WEAPON_IS_ALREADY_LOADED).")
    { TextKey = "STR_WEAPON_IS_ALREADY_LOADED" };

    internal static readonly CampaignReason SwapNotHeld = new("battle.inventory-ammo-swap-not-held",
        "In the battle only a weapon held in a hand takes the quick swap; the reference refuses without a message.");

    internal static readonly CampaignReason UnloadNotHeld = new("battle.inventory-unload-not-held",
        "In the battle only a weapon held in a hand is Shift-unloaded; the reference does nothing.");

    internal static readonly CampaignReason NothingToUnload = new("battle.inventory-nothing-to-unload",
        "The item is no weapon with a loaded slot nor a primed grenade that can be unprimed; the reference does nothing.");

    internal static readonly CampaignReason NoAmmunitionLoaded = new("battle.inventory-no-ammunition-loaded",
        "The weapon holds no ammunition (STR_NO_AMMUNITION_LOADED).")
    { TextKey = "STR_NO_AMMUNITION_LOADED" };

    internal static readonly CampaignReason HandsFull = new("battle.inventory-hands-full",
        "Unloading needs one empty hand (STR_ONE_HAND_MUST_BE_EMPTY).")
    { TextKey = "STR_ONE_HAND_MUST_BE_EMPTY" };

    internal static readonly CampaignReason PrimeInBattle = new("battle.inventory-prime-in-battle",
        "The inventory primes only before the battle (the right click closes the battle's inventory); the action menu primes in the battle.");

    internal static readonly CampaignReason ItemNeedsNoFuse = new("battle.inventory-prime-not-offered",
        "The item has no fuse, or no time cost to prime or unprime it.");

    internal static readonly CampaignReason LayoutScreensUnsupported = new("battle.inventory-layout-screens-unsupported",
        "Saving and loading named inventory layouts is not ported yet.");

    internal static readonly CampaignReason ClearUnsupported = new("battle.inventory-clear-unsupported",
        "Clearing a unit's inventory to the ground is not ported yet.");

    internal static readonly CampaignReason AutoEquipUnsupported = new("battle.inventory-autoequip-unsupported",
        "Automatic equipping from the inventory screen is not ported yet.");

    internal static readonly CampaignReason TemplateUnsupported = new("battle.inventory-template-unsupported",
        "Applying and creating inventory templates, which write the soldier's personal layout, is not ported yet.");

    internal static readonly CampaignReason RenameUnsupported = new("battle.inventory-rename-unsupported",
        "Renaming a soldier from the inventory screen is not ported yet.");

    internal static CampaignReason For(InventoryFeature feature) => feature switch
    {
        InventoryFeature.ClearInventory => ClearUnsupported,
        InventoryFeature.AutoEquip => AutoEquipUnsupported,
        InventoryFeature.Template => TemplateUnsupported,
        InventoryFeature.RenameSoldier => RenameUnsupported,
        InventoryFeature.SaveLayout or InventoryFeature.LoadLayout => LayoutScreensUnsupported,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "The feature runs; it has no refusal of its own."),
    };
}
