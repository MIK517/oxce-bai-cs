using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons the priming commands refuse (Phase 7 branch 5 S8).</summary>
internal static class BattlePrimeReasons
{
    internal static readonly CampaignReason NotOffered = new("battle.prime-action-not-offered",
        "The item does not offer this action to this unit: it has no fuse, no time cost or no action name for it, it is already primed (or not primed for the unprime), or the unit lacks the psi skill or the mana feature the item needs.");

    internal static readonly CampaignReason TurnsInvalid = new("battle.prime-turns-invalid",
        "An item with a settable fuse is primed for 0 to 23 turns.");

    internal static readonly CampaignReason FuseTypeUnknown = new("battle.prime-fuse-type-unknown",
        "The item's fuse type is not a settable, instant or fixed (0 to 63 turns) fuse, so the item has no default fuse to prime it with.");
}

/// <summary>
/// Throwing and priming (Phase 7 branch 5 S8): the fuse rules of a thrown item (<c>BattleItem::fuseThrowEvent</c>, <c>RuleItem::getFuseTimerDefault</c>), and
/// <c>BattlescapeGame::handleNonTargetAction</c>'s priming and unpriming with the menu's offer of them. The throw itself is <see cref="ThrowAction"/> and the throw branches of
/// <c>ProjectileFlyBState</c> (in <c>BattleShooting.cs</c>). User options at their defaults: <c>battleInstantGrenade</c> and <c>instantPrime</c> are off.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>BattleFuseType BFT_INSTANT; BFT_NONE is <c>NoFuseType</c> and BFT_SET -1.</summary>
    private const int InstantFuseType = -2;

    private const int SetFuseType = -1;
    private const int MaximumFuseTurns = 24;

    /// <summary>RuleItem::getFuseTimerDefault: a fixed fuse is its value, a settable or instant one 0, none -1 (cannot be primed).</summary>
    internal static int FuseTimerDefault(RuntimeBattleItem rule)
    {
        if (rule.FuseType is >= 0 and < 64) return rule.FuseType;
        return rule.FuseType is SetFuseType or InstantFuseType ? 0 : -1;
    }

    /// <summary>
    /// BattleItem::fuseThrowEvent: a throw trigger enables the fuse (an item with no fuse type starts it at 0); an item whose fuse then runs at 0 explodes when it lands if its
    /// trigger says so (<c>throwExplode</c>, or by default a grenade with an instant fuse), with the item's <c>specialChance</c>.
    /// </summary>
    internal bool FuseThrowEvent(BattleItem item)
    {
        var rule = RequiredRules.Battle.Equipment.Items[item.ItemType];
        var trigger = rule.FuseTrigger;
        if (trigger.ThrowTrigger)
        {
            if (rule.FuseType == NoFuseType)
            {
                item.FuseEnabled = true;
                item.FuseTimer = 0;
            }
            else if (item.FuseTimer >= 0)
            {
                item.FuseEnabled = true;
            }
        }

        if (!item.FuseEnabled || item.FuseTimer != 0) return false;
        var explodes = trigger.ThrowExplode || trigger.DefaultBehavior && ItemRuleOf(item.ItemType).BattleType == RuntimeBattleItem.GrenadeBattleType && rule.FuseType == InstantFuseType;
        return explodes && RandomPercent(rule.Combat.SpecialChance);
    }

    /// <summary>
    /// ActionMenuState's priming lines: an item with a fuse offers the priming while its timer is -1 (with a prime cost), the unpriming otherwise (with an unprime cost
    /// and name); a psi weapon the unit has no skill for, and a mana weapon without the feature, offer neither.
    /// </summary>
    internal bool WeaponOffersPrime(BattleResearchGate research, BattleUnit unit, BattleItem weapon, bool prime)
    {
        var rule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        if (!WeaponShoots(research, unit, weapon) || rule.FuseType == NoFuseType) return false;
        if (prime != (weapon.FuseTimer == -1)) return false;
        // ActionMenuState.cpp:110: only the menu asks for the unprime name; the cost's own readers (inventory, itemDropInventory) do not.
        if (!prime && rule.UnprimeActionName.Length == 0) return false;
        return rule.ActionCosts.Of(prime ? RuntimeItemActionCosts.Prime : RuntimeItemActionCosts.Unprime).Cost.Time > 0;
    }

    /// <summary>
    /// <c>ActionMenuState::handleAction</c> then <c>handleNonTargetAction</c> for <c>BA_PRIME</c> and <c>BA_UNPRIME</c>: the item is in the unit's hand, the menu offers the
    /// action, the research and <c>canUseWeapon</c> allow it, the time units are paid, and the fuse is set (for a settable fuse the <paramref name="turns"/> the player chose, for
    /// any other the item's default) or cleared. The unit's light moves with a flare, and the sight around it is updated.
    /// </summary>
    internal CampaignReason? PrimeAction(BattleResearchGate research, int unitId, int itemId, bool prime, int turns, out int fuse)
    {
        ArgumentNullException.ThrowIfNull(research);
        fuse = -1;
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is null) return BattleSelectionReasons.UnitUnknown;
        if (WeaponOf(unit, itemId) is not { } weapon) return BattleAttackReasons.WeaponUnavailable;
        if (!WeaponOffersPrime(research, unit, weapon, prime)) return BattlePrimeReasons.NotOffered;
        var type = prime ? BattleActionType.Prime : BattleActionType.Unprime;
        var usable = CanUseWeapon(research, weapon, unit, false, type);
        if (!usable.Usable) return BattleAttackReasons.ForMessage(usable.Message);

        var rule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        if (prime)
        {
            // Only a settable fuse is bounded by the menu's 24 buttons; any other type primes for its default (a fixed fuse may be up to 63).
            if (rule.FuseType == SetFuseType)
            {
                fuse = turns;
                if (fuse is < 0 or >= MaximumFuseTurns) return BattlePrimeReasons.TurnsInvalid;
            }
            else
            {
                // A fuse type the reference reads as -1 (ActionMenuState.cpp:333) is not primed: no cost, no message.
                fuse = FuseTimerDefault(rule);
                if (fuse < 0) return BattlePrimeReasons.FuseTypeUnknown;
            }
        }
        var cost = BattleActionCost.For(RequiredRules, type, unit, weapon);
        if (cost.Shortfall() is var shortfall && shortfall != BattleActionShortfall.None) return BattleAttackReasons.ForMessage(ShortfallMessage(shortfall));
        unit.SpendCost(cost);
        weapon.SetFuseTimer(prime ? fuse : -1, rule);
        var position = unit.RequiredPosition;
        CalculateLighting(BattleLightLayer.Units, position);
        Sight().CalculateFov(position, VisibilityUpdateRange(weapon), updateTiles: false);
        MarkChanged();
        return null;
    }
}
