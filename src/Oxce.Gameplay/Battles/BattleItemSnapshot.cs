using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// One saved item (<c>SavedBattleGame::load</c>'s item passes and <c>BattleItem::load</c> and <c>save</c>, Phase 7 branch 2
/// S4). A nullable value is one the save may leave out, which the load then takes from the BattleItem constructor;
/// <see cref="BattleState.Capture"/> fills every one. Unit IDs are -1 for none.
/// </summary>
/// <remarks>
/// <see cref="RuleInventoryMoveCostPercent"/>, <see cref="SlotIsGrid"/>, <see cref="AmmoRechargeable"/> and
/// <see cref="Medikit"/> are the rule values <c>save</c>'s conditions read, which the reader and the capture take from the
/// content; they are not saved.
/// </remarks>
public sealed record BattleItemSnapshot(int Id, string ItemType)
{
    /// <summary>The tag owner of an item's script values (ScriptValues&lt;BattleItem&gt;).</summary>
    public const string ScriptOwner = "BattleItem";

    /// <summary><c>owner</c>, <c>previousOwner</c> and <c>unit</c> (a corpse's unit).</summary>
    public int OwnerId { get; init; } = -1;
    public int PreviousOwnerId { get; init; } = -1;
    public int UnitId { get; init; } = -1;

    /// <summary><c>inventoryMoveCost: {basePercent}</c>, the rule's when absent.</summary>
    public int? InventoryMoveCostPercent { get; init; }

    public int? RuleInventoryMoveCostPercent { get; init; }

    /// <summary>
    /// <c>inventoryslot</c>: a known inventory section ("" for <c>NULL</c> or none). The reader resolves an unknown one to
    /// the ground, as the load does.
    /// </summary>
    public string Slot { get; init; } = "";

    /// <summary>Whether the slot is an inventory grid (INV_SLOT), the only kind whose <c>inventoryX</c> and <c>inventoryY</c> are saved.</summary>
    public bool SlotIsGrid { get; init; }

    public int SlotX { get; init; }
    public int SlotY { get; init; }

    /// <summary><c>position</c>: the tile of an item on the ground (read for a ground slot only; x -1 is none).</summary>
    public BattlePosition? Position { get; init; }

    /// <summary><c>ammoqty</c>, the rule's clip size when absent.</summary>
    public int? AmmoQuantity { get; init; }

    public bool AmmoRechargeable { get; init; }

    /// <summary><c>ammoItem</c>: the first ammo slot's item (the older form, read only without <c>ammoItemSlots</c>).</summary>
    public int? AmmoItem { get; init; }

    /// <summary><c>ammoItemSlots</c>: each ammo slot's item, -1 for none, up to the last filled slot; null when absent.</summary>
    public IReadOnlyList<int>? AmmoItemSlots { get; init; }

    /// <summary>Whether the item is a medikit (BT_MEDIKIT), whose quantities are always saved.</summary>
    public bool Medikit { get; init; }

    /// <summary><c>painKiller</c>, <c>heal</c> and <c>stimulant</c>, the constructor's when absent.</summary>
    public int? PainKiller { get; init; }
    public int? Heal { get; init; }
    public int? Stimulant { get; init; }

    /// <summary><c>fuseTimer</c> (setFuseTimer on load, which also sets the fuse) and <c>fuseEnabed</c> (sic), each when present.</summary>
    public int? FuseTimer { get; init; }
    public bool? FuseEnabled { get; init; }

    public bool DroppedOnAlienTurn { get; init; }

    /// <summary><c>XCOMProperty</c>.</summary>
    public bool XcomProperty { get; init; }

    /// <summary>The item's <c>tags</c> (ScriptValues of <c>BattleItem</c>), non-zero values in tag order.</summary>
    public IReadOnlyList<ScriptValueEntry> ScriptValues { get; init; } = [];
}
