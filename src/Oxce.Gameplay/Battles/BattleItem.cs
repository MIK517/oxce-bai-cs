using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// An item in a battle (BattleItem): on a tile or carried by a unit, in an inventory slot. IDs follow
/// <c>SavedBattleGame::getCurrentItemId</c>, in creation order.
/// </summary>
internal sealed class BattleItem(int id, string itemType)
{
    public int Id { get; } = id;
    public string ItemType { get; } = itemType;

    /// <summary>The tile the item lies on, or null while it is carried or nowhere.</summary>
    public BattlePosition? Tile { get; set; }

    /// <summary>The unit carrying the item, or null.</summary>
    public int? OwnerUnitId { get; set; }

    /// <summary>BattleItem::getPreviousOwner: the unit that carried it before (setOwner keeps the last owner), or null.</summary>
    public int? PreviousOwnerUnitId { get; set; }

    /// <summary>BattleItem::getUnit: the unit a corpse item is (dead or unconscious), or null.</summary>
    public int? CorpseUnitId { get; set; }

    /// <summary>BattleItem's <c>_inventoryMoveCostPercent</c>: starts at the rule's.</summary>
    public int InventoryMoveCostPercent { get; set; } = 100;

    /// <summary>BattleItem::isFuseEnabled: the primed fuse has been triggered (setFuseTimer derives it).</summary>
    public bool FuseEnabled { get; set; }

    /// <summary>BattleItem::getTurnFlag (<c>droppedOnAlienTurn</c>).</summary>
    public bool DroppedOnAlienTurn { get; set; }

    /// <summary>The item's script values (owner <c>BattleItem</c>); null for an item created without content.</summary>
    internal ScriptValueState? ScriptValues { get; set; }

    /// <summary>
    /// BattleItem::setFuseTimer: the timer, and the fuse enabled for a primed timer (0 or more) when the rule's fuse trigger
    /// keeps the default behavior and neither a throw nor a proximity trigger sets it off.
    /// </summary>
    internal void SetFuseTimer(int turns, RuntimeBattleItem rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        FuseTimer = turns;
        var trigger = rule.FuseTrigger;
        FuseEnabled = turns >= 0 && !trigger.ThrowTrigger && !trigger.ProximityTrigger && trigger.DefaultBehavior;
    }

    /// <summary>The inventory slot rule (STR_GROUND on a tile) and the position in it.</summary>
    public string Slot { get; set; } = "";
    public int SlotX { get; set; }
    public int SlotY { get; set; }

    /// <summary>BattleItem::getAmmoQuantity: starts at the rule's clip size.</summary>
    public int AmmoQuantity { get; set; }

    /// <summary>BattleItem::getFuseTimer: -1 when not primed.</summary>
    public int FuseTimer { get; set; } = -1;

    /// <summary>BattleItem::getXCOMProperty: recovered as X-COM's own equipment after the battle.</summary>
    public bool XcomProperty { get; set; }

    /// <summary>
    /// BattleItem::_ammoItem: the item loaded in each of the four ammo slots (the weapon's own ID for a slot without
    /// compatible ammo, which needs none), or null.
    /// </summary>
    public int?[] AmmoSlots { get; } = new int?[AmmoSlotCount];

    /// <summary>BattleItem::isWeaponWithAmmo: a firearm or melee weapon with at least one slot that takes ammo.</summary>
    public bool IsWeaponWithAmmo { get; set; }

    /// <summary>BattleItem::isAmmo: loaded into a weapon.</summary>
    public bool IsAmmo { get; set; }

    /// <summary>BattleItem::isSpecialWeapon: a unit's special built-in item, owned but in no inventory section.</summary>
    public bool IsSpecialWeapon { get; set; }

    /// <summary>BattleItem::getHealQuantity, getPainKillerQuantity, getStimulantQuantity (medikits).</summary>
    public int HealQuantity { get; set; }
    public int PainKillerQuantity { get; set; }
    public int StimulantQuantity { get; set; }

    /// <summary>RuleItem::AmmoSlotMax.</summary>
    internal const int AmmoSlotCount = 4;
}
