using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// <c>Inventory::mouseClick</c>'s drop: a unit's item goes to a position of an inventory section (a hand, a grid slot, or
/// the ground of the unit's tile). Before the first turn it costs nothing; in the battle it costs the section's time units.
/// A ground drop ignores the position: the ground holds any number of items and the screen arranges them.
/// </summary>
public sealed record MoveInventoryItem(int UnitId, int ItemId, string Slot, int SlotX, int SlotY) : IBattleCommand, IUnitCommand;

/// <summary>
/// <c>Inventory::mouseClick</c>'s Ctrl click: a ground item goes to the best section that holds it (the cheapest to reach
/// a hand when <paramref name="Smart"/>, the client's <c>oxceSmartCtrlEquip</c>), a carried item drops to the ground.
/// </summary>
public sealed record AutoPlaceInventoryItem(int UnitId, int ItemId, bool Smart) : IBattleCommand, IUnitCommand;

/// <summary>What an inventory screen can ask for that is not ported yet; each is refused by its own name.</summary>
public enum InventoryFeature
{
    /// <summary>Loading a clip into a weapon, and Shift-swapping it (<c>Inventory::mouseClick</c>'s "put item in weapon").</summary>
    LoadAmmo,

    /// <summary>Unloading a weapon (<c>Inventory::unload</c>).</summary>
    UnloadAmmo,

    /// <summary>Throwing from the inventory screen.</summary>
    Throw,

    /// <summary>Priming a grenade (<c>PrimeGrenadeState</c>) and unpriming it.</summary>
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

/// <summary>A request for a feature of the inventory screen that arrives later; it is always refused by the feature's name.</summary>
public sealed record UseInventoryFeature(int UnitId, int ItemId, InventoryFeature Feature) : IBattleCommand, IUnitCommand;

/// <summary>An item moved between sections, or to or from the ground.</summary>
public sealed record InventoryItemMoved(int UnitId, int ItemId, string FromSlot, string ToSlot, int SlotX, int SlotY, int TimeUnitsSpent) : ICampaignEvent;

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

    internal static readonly CampaignReason DangerZoneUnsupported = new("battle.inventory-danger-zone-unsupported",
        "Dropping an enabled grenade marks a danger zone for the artificial intelligence, which arrives with turn processing (Phase 7 branch 6).");

    internal static readonly CampaignReason AmmoUnsupported = new("battle.inventory-ammo-unsupported",
        "Loading and unloading ammunition in the inventory is not ported yet.");

    internal static readonly CampaignReason ThrowUnsupported = new("battle.inventory-throw-unsupported",
        "Throwing from the inventory is not ported yet.");

    internal static readonly CampaignReason PrimeUnsupported = new("battle.inventory-prime-unsupported",
        "Priming and unpriming from the inventory is not ported yet.");

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
        InventoryFeature.LoadAmmo or InventoryFeature.UnloadAmmo => AmmoUnsupported,
        InventoryFeature.Throw => ThrowUnsupported,
        InventoryFeature.Prime => PrimeUnsupported,
        InventoryFeature.ClearInventory => ClearUnsupported,
        InventoryFeature.AutoEquip => AutoEquipUnsupported,
        InventoryFeature.Template => TemplateUnsupported,
        InventoryFeature.RenameSoldier => RenameUnsupported,
        _ => LayoutScreensUnsupported,
    };
}
