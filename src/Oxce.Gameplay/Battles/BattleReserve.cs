using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>What <see cref="BattleState.CheckReservedTimeUnits"/> found.</summary>
internal enum ReservedTimeUnitsWarning
{
    None,

    /// <summary>STR_TIME_UNITS_RESERVED_FOR_KNEELING.</summary>
    Kneeling,

    /// <summary>STR_TIME_UNITS_RESERVED_FOR_KNEELING_AND_FIRING.</summary>
    KneelingAndFiring,

    /// <summary>STR_TIME_UNITS_RESERVED_FOR_SNAP_SHOT.</summary>
    SnapShot,

    /// <summary>STR_TIME_UNITS_RESERVED_FOR_AUTO_SHOT.</summary>
    AutoShot,

    /// <summary>STR_TIME_UNITS_RESERVED_FOR_AIMED_SHOT.</summary>
    AimedShot,

    /// <summary>The reserve was refused with no warning text (a justChecking call, or a reserve of another type).</summary>
    Silent,

    /// <summary>The hostile side's percentage reserve (the AI's reserve mode) is branch 6's; the check does not run.</summary>
    HostileReserveUnsupported,
}

/// <summary>The answer of <c>BattlescapeGame::checkReservedTU</c>: whether the unit may spend, and the warning a refusal shows.</summary>
internal readonly record struct ReservedTimeUnitsResult(bool Allowed, ReservedTimeUnitsWarning Warning = ReservedTimeUnitsWarning.None)
{
    /// <summary>The named refusal when the check did not run (not a reserve that was exceeded), else null.</summary>
    public CampaignReason? Unsupported { get; init; }
}

/// <summary>
/// The time units a unit keeps in reserve (Phase 7 branch 4 S1): <c>BattlescapeGame::checkReservedTU</c> for the player's side
/// with the saved <c>TUReserved</c> and <c>kneelReserved</c>, and the weapon it reads (<c>BattleUnit::getMainHandWeapon</c>).
/// </summary>
internal sealed partial class BattleState
{
    internal static readonly CampaignReason HostileReserveUnsupportedReason = new("battle.hostile-reserve-unsupported",
        "The hostile side's percentage time unit reserve (the AI's reserve mode) arrives with the AI turn (Phase 7 branch 6).");

    private const int BattleTypeFirearm = 1;
    private const int BattleTypeMelee = 3;
    private const string RightHand = "STR_RIGHT_HAND";
    private const string LeftHand = "STR_LEFT_HAND";

    internal static RuntimeBattleArmor ArmorOf(RuntimeRuleCatalog rules, BattleUnit unit) =>
        rules.Battle.Equipment.Armors.TryGetValue(unit.ArmorId, out var armor) ? armor
            : throw new InvalidOperationException($"Battle unit {unit.Id} wears armor {unit.ArmorId}, which does not exist.");

    private static RuntimeItemRule ItemRuleOf(RuntimeRuleCatalog rules, string itemType) =>
        rules.Items.TryGet(itemType, out var handle) ? rules.Items[handle].Value
            : throw new InvalidOperationException($"Item {itemType} does not exist.");

    /// <summary>Armor::allowsKneeling for the unit: the armor's choice, else whether the unit is a soldier (<c>getType() == "SOLDIER"</c>).</summary>
    internal static bool AllowsKneeling(RuntimeRuleCatalog rules, BattleUnit unit) =>
        RuntimeBattleArmor.Allows(ArmorOf(rules, unit).AllowsKneeling, unit.SoldierId is not null);

    /// <summary>The time units kept for kneeling down: the kneel cost while kneeling is reserved and the unit stands and may kneel, else 0.</summary>
    private int ReservedKneelCost(RuntimeRuleCatalog rules, BattleUnit unit) =>
        KneelReserved && !unit.Kneeled && AllowsKneeling(rules, unit) ? BattleUnit.KneelDownCost : 0;

    /// <summary>BattleUnit::getSpecialWeapon's candidates: the unit's special weapons that are in the battle, in its order.</summary>
    private IEnumerable<BattleItem> SpecialWeapons(BattleUnit unit)
    {
        foreach (var id in unit.SpecialWeaponIds)
            if (_items.FirstOrDefault(item => item.Id == id) is { } special) yield return special;
    }

    /// <summary>BattleItem::getAmmoForAction (const): the item feeding an action (0 aimed or launch, 1 auto, 2 snap, 3 hit), null when unloaded or spent.</summary>
    private BattleItem? AmmoForAction(RuntimeRuleCatalog rules, BattleItem item, int action)
    {
        var slot = rules.Battle.Equipment.Items[item.ItemType].ActionAmmoSlots[action];
        if (slot == -1) return item;
        if (slot < 0 || slot >= BattleItem.AmmoSlotCount) throw new InvalidOperationException($"Item {item.ItemType} names ammo slot {slot}.");
        if (item.AmmoSlots[slot] is not { } id) return null;
        var ammo = id == item.Id ? item : _items.First(candidate => candidate.Id == id);
        return ammo.AmmoQuantity == 0 && !rules.Battle.Equipment.Items[ammo.ItemType].AmmoRechargeable ? null : ammo;
    }

    /// <summary>BattleItem::haveAnyAmmo: loaded (an empty clip counts) for its aimed, auto or snap shot, or its hit for melee.</summary>
    internal bool HaveAnyAmmo(RuntimeRuleCatalog rules, BattleItem item)
    {
        if (!item.IsWeaponWithAmmo) return true;
        return ItemRuleOf(rules, item.ItemType).BattleType == BattleTypeMelee
            ? AmmoForAction(rules, item, 3) is not null
            : AmmoForAction(rules, item, 0) is not null || AmmoForAction(rules, item, 1) is not null || AmmoForAction(rules, item, 2) is not null;
    }

    /// <summary>BattleItem::getCurrentWaypoints: the item's waypoints, else those of the ammo its launch uses.</summary>
    private int CurrentWaypoints(RuntimeRuleCatalog rules, BattleItem item)
    {
        var waypoints = ItemRuleOf(rules, item.ItemType).Waypoints;
        if (waypoints == 0 && AmmoForAction(rules, item, 0) is { } ammo && ammo != item) waypoints = ItemRuleOf(rules, ammo.ItemType).Waypoints;
        return waypoints;
    }

    /// <summary>BattleUnit::getRightHandWeapon and getLeftHandWeapon: the first item of the unit's inventory in the hand.</summary>
    private BattleItem? HandWeapon(BattleUnit unit, string hand) => OrderedInventoryOf(unit).FirstOrDefault(item => item.Slot == hand);

    /// <summary>
    /// BattleUnit::getMainHandWeapon(quickest, needAmmo, reactions): the weapon of the hand that is loaded; with two, the one with the
    /// cheaper snap shot (the quickest one, else the active hand for a player's unit, else a hostile unit's other hand), a blaster first
    /// for a non-player; with none, a special firearm.
    /// </summary>
    internal BattleItem? MainHandWeapon(RuntimeRuleCatalog rules, BattleUnit unit, bool quickest = true, bool needAmmo = true, bool reactions = false)
    {
        var right = HandWeapon(unit, RightHand);
        var left = HandWeapon(unit, LeftHand);
        if (right is null || needAmmo && !HaveAnyAmmo(rules, right)) right = null;
        if (left is null || needAmmo && !HaveAnyAmmo(rules, left)) left = null;
        if (reactions && unit.Faction == BattleFaction.Player)
        {
            if (unit.ReactionsDisabledForRightHand) right = null;
            if (unit.ReactionsDisabledForLeftHand) left = null;
        }
        if (right is not null && left is null) return right;
        if (right is null && left is not null) return left;
        if (right is null)
        {
            // The AI may also use a special weapon, when both hands are empty: the first special firearm.
            return SpecialWeapons(unit).FirstOrDefault(special => ItemRuleOf(rules, special.ItemType).BattleType == BattleTypeFirearm);
        }
        var timeRight = unit.ActionCost(rules.Battle.Equipment.Items[right.ItemType].ActionCosts, BattleActionType.SnapShot).Time;
        var timeLeft = unit.ActionCost(rules.Battle.Equipment.Items[left!.ItemType].ActionCosts, BattleActionType.SnapShot).Time;
        // getActiveHand: the active hand's weapon, else the left one, else the right one.
        var active = unit.ActiveHand == RightHand ? right : left;
        if (!quickest && unit.Faction != BattleFaction.Player)
        {
            if (CurrentWaypoints(rules, right) != 0) return right;
            if (CurrentWaypoints(rules, left) != 0) return left;
        }
        if (timeLeft <= 0 && timeRight > 0) return right;
        if (timeRight <= 0 && timeLeft > 0) return left;
        if (timeLeft >= timeRight)
            return quickest ? right : unit.Faction == BattleFaction.Player ? active : left;
        return quickest ? left : unit.Faction == BattleFaction.Player ? active : right;
    }

    /// <summary>
    /// <c>BattlescapeGame::checkReservedTU</c>, the player's side: whether the unit can spend <paramref name="timeUnits"/> and
    /// <paramref name="energy"/> and still keep the time units reserved for its weapon (<see cref="TimeUnitsReserved"/>: auto falls back to
    /// snap, snap to aimed, aimed to none) and for kneeling (<see cref="KneelReserved"/>, when the armor allows it and the unit
    /// stands). A unit of another side than the one acting, or any neutral unit, only needs the time units. The warning a refusal shows is
    /// classified; its text belongs to the client. The hostile side's reserve is a percentage of the AI's reserve mode (branch 6): it is
    /// not run and answers <see cref="ReservedTimeUnitsWarning.HostileReserveUnsupported"/>. <paramref name="reserved"/> replaces the saved reserve (the path preview's temporary auto shot reserve).
    /// </summary>
    internal ReservedTimeUnitsResult CheckReservedTimeUnits(RuntimeRuleCatalog rules, BattleUnit unit, int timeUnits, int energy, bool justChecking, int? reserved = null)
    {
        var reserve = reserved ?? TimeUnitsReserved;
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(unit);
        var cost = new BattleActionCost(unit)
        {
            Type = (BattleActionType)reserve,
            Weapon = MainHandWeapon(rules, unit, quickest: false),
        };
        if (Side != unit.Faction || Side == BattleFaction.Neutral) return new(timeUnits <= unit.TimeUnits);
        if (Side == BattleFaction.Hostile) return new(false, ReservedTimeUnitsWarning.HostileReserveUnsupported) { Unsupported = HostileReserveUnsupportedReason };

        cost.UpdateTimeUnits(rules);
        // If the weapon has no autoshot, reserve for a snap shot; likewise from snap to aimed.
        if (cost.Time == 0 && cost.Type == BattleActionType.AutoShot)
        {
            cost.Type = BattleActionType.SnapShot;
            cost.UpdateTimeUnits(rules);
        }
        if (cost.Time == 0 && cost.Type == BattleActionType.SnapShot)
        {
            cost.Type = BattleActionType.AimedShot;
            cost.UpdateTimeUnits(rules);
        }
        var kneel = ReservedKneelCost(rules, unit);
        // No aimed shot either: reserve nothing, or only the kneel.
        if (cost.Time == 0 && cost.Type == BattleActionType.AimedShot)
        {
            if (kneel > 0) cost.Type = BattleActionType.Kneel;
            else return new(true);
        }
        cost.Time += kneel;

        // The unit has less than the reserve needs: nothing can be reserved.
        if (!cost.HaveTimeUnits() && !justChecking) return new(true);

        cost.Time += timeUnits;
        cost.Energy += energy;
        if ((cost.Type != BattleActionType.None || KneelReserved) && !cost.HaveTimeUnits())
        {
            if (justChecking) return new(false, ReservedTimeUnitsWarning.Silent);
            if (kneel != 0)
                return new(false, cost.Type == BattleActionType.Kneel ? ReservedTimeUnitsWarning.Kneeling : ReservedTimeUnitsWarning.KneelingAndFiring);
            return new(false, (BattleActionType)reserve switch
            {
                BattleActionType.SnapShot => ReservedTimeUnitsWarning.SnapShot,
                BattleActionType.AutoShot => ReservedTimeUnitsWarning.AutoShot,
                BattleActionType.AimedShot => ReservedTimeUnitsWarning.AimedShot,
                _ => ReservedTimeUnitsWarning.Silent,
            });
        }
        return new(true);
    }
}
