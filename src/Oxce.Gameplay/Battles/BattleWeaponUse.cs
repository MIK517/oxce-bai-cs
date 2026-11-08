using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>What <see cref="BattleState.CanUseWeapon"/> found: whether the unit may use the weapon, and the message it shows for a refusal that has one.</summary>
internal readonly record struct BattleWeaponUse(bool Usable, string Message = "")
{
    public static BattleWeaponUse Allowed { get; } = new(true);
    public static BattleWeaponUse Refused { get; } = new(false);
}

/// <summary>
/// What <c>SavedGame</c> knows that using a weapon asks about: whether a research topic is done (<c>isResearched</c>; always true in debug mode), and
/// the same for the mana feature (<c>isManaUnlocked</c>). A battle has no campaign of its own, so the host hands it over.
/// </summary>
internal sealed record BattleResearchGate(Func<string, bool> IsResearched, bool DebugMode)
{
    /// <summary>Every research is done (the debug mode).</summary>
    public static BattleResearchGate Everything { get; } = new(static _ => true, true);
}

/// <summary>
/// Using a weapon and shooting with it (Phase 7 branch 5 S2): <c>SavedBattleGame::canUseWeapon</c>, <c>BattleUnit::getFiringAccuracy</c> and
/// <c>getAccuracyModifier</c>, the item's range checks and limits (<c>RuleItem::isOutOfRange</c>, <c>isOutOfThrowRange</c>, <c>calculateLimits</c>),
/// <c>BattleItem::getTotalWeight</c> and <c>getArcingShot</c>, and <c>Projectile::getHitChance</c>. The user option <c>battleUFOExtenderAccuracy</c>
/// is off, so <c>calculateLimits</c> takes the aimed range for every shot.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>Mod::maxAccuracy, distanceRows and accPerRowCount: the hit chance table's size.</summary>
    private const int HitChanceMaxAccuracy = 120;

    private const int HitChanceDistanceRows = 40;
    private const int HitChanceAccuracyPerRow = 61;

    /// <summary>A refusal the reference gives no message for; the menu shows STR_UNKNOWN for it (<c>ActionMenuState</c>).</summary>
    private static readonly BattleWeaponUse NoMessage = new(false, "STR_UNKNOWN");

    /// <summary>BattleItem::getTotalWeight: the item's weight with that of every ammunition item loaded into it (not itself).</summary>
    internal int TotalWeight(RuntimeRuleCatalog rules, BattleItem item)
    {
        var weight = ItemRuleOf(rules, item.ItemType).Weight;
        foreach (var id in item.AmmoSlots)
            if (id is { } ammoId && ammoId != item.Id && _items.FirstOrDefault(candidate => candidate.Id == ammoId) is { } ammo)
                weight += ItemRuleOf(rules, ammo.ItemType).Weight;
        return weight;
    }

    /// <summary>BattleItem::getAmmoForAction (const) by action type: the item feeding the action; null for an action without a config, or without ammunition.</summary>
    internal BattleItem? AmmoForActionType(RuntimeRuleCatalog rules, BattleItem item, BattleActionType type) => ActionIndex(type) is { } action ? AmmoForAction(rules, item, action) : null;

    /// <summary>The index of the action's config (0 aimed or launch, 1 auto, 2 snap, 3 melee), or null for an action without one (<c>getActionConf</c>).</summary>
    internal static int? ActionIndex(BattleActionType type) => type switch
    {
        BattleActionType.Launch or BattleActionType.AimedShot => 0,
        BattleActionType.AutoShot => 1,
        BattleActionType.SnapShot => 2,
        BattleActionType.Hit => 3,
        _ => null,
    };

    /// <summary>BattleItem::getArcingShot: the item arcs every shot, or the action's config does.</summary>
    internal static bool ArcingShot(RuntimeRuleCatalog rules, BattleItem weapon, BattleActionType type)
    {
        var combat = rules.Battle.Equipment.Items[weapon.ItemType].Combat;
        if (combat.ArcingShot) return true;
        return type switch
        {
            BattleActionType.Launch or BattleActionType.AimedShot => combat.Aimed.Arcing,
            BattleActionType.AutoShot => combat.Auto.Arcing,
            BattleActionType.SnapShot => combat.Snap.Arcing,
            BattleActionType.Hit => combat.Melee.Arcing,
            _ => false,
        };
    }

    /// <summary>RuleItem::isOutOfRange: the squared distance in tiles is past the range, but a diagonal step is allowed to a range of 1 or 2.</summary>
    internal static bool IsOutOfRange(RuntimeRuleCatalog rules, BattleItem weapon, int distanceSquared)
    {
        var maxRange = rules.Battle.Equipment.Items[weapon.ItemType].Combat.MaxRange;
        var outOfRange = distanceSquared > maxRange * maxRange;
        if (outOfRange)
        {
            // Special handling for a range of 1 (one diagonal move) and 2 (a diagonal move and a straight one).
            if (maxRange == 1 && distanceSquared <= 3) outOfRange = false;
            else if (maxRange == 2 && distanceSquared <= 6) outOfRange = false;
        }
        return outOfRange;
    }

    /// <summary>RuleItem::isOutOfThrowRange: past the throw range (the underwater one under water); a diagonal gets no allowance.</summary>
    internal static bool IsOutOfThrowRange(RuntimeRuleCatalog rules, BattleItem weapon, int distanceSquared, int depth)
    {
        var combat = rules.Battle.Equipment.Items[weapon.ItemType].Combat;
        var range = depth > 0 ? combat.UnderwaterThrowRange : combat.ThrowRange;
        return distanceSquared > range * range;
    }

    /// <summary>
    /// RuleItem::calculateLimits: the range up to which the accuracy holds (200 tiles for a throw, else the aimed range) and the range below which it
    /// drops (0 for a throw, else the minimum range), and the loss per tile past them (the throw's or the weapon's dropoff).
    /// </summary>
    internal static int CalculateLimits(RuntimeRuleCatalog rules, BattleItem weapon, BattleActionType type, out int upperLimit, out int lowerLimit)
    {
        var combat = rules.Battle.Equipment.Items[weapon.ItemType].Combat;
        upperLimit = type == BattleActionType.Throw ? 200 : combat.Aimed.Range;
        lowerLimit = type == BattleActionType.Throw ? 0 : combat.MinRange;
        return type == BattleActionType.Throw ? combat.ThrowDropoff : combat.Dropoff;
    }

    /// <summary>BattleUnit::getAccuracyModifier: the unit's health and the fatal wounds of its head and the arm or arms holding the weapon, as a percentage (at least 10).</summary>
    internal static int AccuracyModifier(RuntimeRuleCatalog rules, BattleUnit unit, BattleItem? item)
    {
        var wounds = unit.FatalWounds[HeadPart];
        if (item is not null)
        {
            if (ItemRuleOf(rules, item.ItemType).TwoHanded) wounds += unit.FatalWounds[RightArmPart] + unit.FatalWounds[LeftArmPart];
            else if (Section(rules, item.Slot) is { } slot)
            {
                // Why should broken hands affect the aim when the weapon is not shot with them: only the hand holding it counts.
                if (slot.IsRightHand) wounds += unit.FatalWounds[RightArmPart];
                if (slot.IsLeftHand) wounds += unit.FatalWounds[LeftArmPart];
            }
        }
        return Math.Max(10, 25 * unit.Health / unit.Stats.Health + 75 + -10 * wounds);
    }

    private const int HeadPart = 0;
    private const int RightArmPart = 2;
    private const int LeftArmPart = 3;

    /// <summary>
    /// BattleUnit::getFiringAccuracy: the percentage of a shot, throw or hit with the weapon. The weapon's accuracy for the action times the unit's
    /// multiplier, less the unit's kneel bonus (a firing action only, so a kneeling hit or throw gets none) and the one-handed penalty of a two-handed
    /// weapon, all times the accuracy modifier.
    /// </summary>
    internal int FiringAccuracy(RuntimeRuleCatalog rules, BattleUnit attacker, BattleItem weapon, BattleActionType type)
    {
        var rule = ItemRuleOf(rules, weapon.ItemType);
        var combat = rules.Battle.Equipment.Items[weapon.ItemType].Combat;
        var modifier = AccuracyModifier(rules, attacker, weapon);
        var result = 0;
        var kneeled = attacker.Kneeled;
        switch (type)
        {
            case BattleActionType.SnapShot:
                result = combat.StatBonuses.Accuracy.Evaluate(attacker) * rule.Accuracy.Snap / 100;
                break;
            case BattleActionType.AimedShot or BattleActionType.Launch:
                result = combat.StatBonuses.Accuracy.Evaluate(attacker) * rule.Accuracy.Aimed / 100;
                break;
            case BattleActionType.AutoShot:
                result = combat.StatBonuses.Accuracy.Evaluate(attacker) * rule.Accuracy.Auto / 100;
                break;
            case BattleActionType.Hit:
                kneeled = false;
                result = combat.StatBonuses.MeleeMultiplier.Evaluate(attacker) * rule.Accuracy.Melee / 100;
                break;
            case BattleActionType.Throw:
                kneeled = false;
                result = combat.StatBonuses.Throw.Evaluate(attacker) * rule.Accuracy.Throw / 100;
                break;
            case BattleActionType.CloseQuarters:
                kneeled = false;
                result = combat.StatBonuses.CloseQuarters.Evaluate(attacker) * combat.AccuracyCloseQuarters / 100;
                break;
        }
        if (kneeled) result = result * combat.KneelBonus / 100;
        if (rule.TwoHanded && type != BattleActionType.Throw)
        {
            // A two handed weapon means one hand should be empty.
            if (HandWeapon(attacker, RightHand) is not null && HandWeapon(attacker, LeftHand) is not null) result = result * combat.OneHandedPenalty / 100;
            else if (combat.SpecialUseEmptyHand && (HandWeapon(attacker, RightHand) is not null || HandWeapon(attacker, LeftHand) is not null))
            {
                // A special weapon that uses an empty hand: one hand with an item is already enough for the penalty.
                result = result * combat.OneHandedPenalty / 100;
            }
        }
        return result * modifier / 100;
    }

    /// <summary>
    /// SavedBattleGame::canUseWeapon: whether the unit may use the weapon (for an action, which decides the ammunition whose depth rule counts; none
    /// asks as the standard artificial intelligence does, before it knows the action). A hostile unit waits for the weapon's AI use delay; a unit
    /// that began on X-COM's side needs the weapon's research (and the mana feature unlocked for a mana weapon); a psi weapon needs psi skill; water
    /// and land equipment need the depth; a weapon that blocks both hands needs one free (a berserk unit ignores that).
    /// </summary>
    internal BattleWeaponUse CanUseWeapon(RuntimeRuleCatalog rules, BattleResearchGate research, BattleItem? weapon, BattleUnit? unit, bool berserking, BattleActionType type)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(research);
        if (weapon is null || unit is null) return BattleWeaponUse.Refused;
        var rule = ItemRuleOf(rules, weapon.ItemType);
        var battleRule = rules.Battle.Equipment.Items[weapon.ItemType];
        // The action type selected by the player, a leeroy jenkins AI, all reaction fire and every berserk unit know their ammunition; the standard AI does not.
        var ammo = type != BattleActionType.None ? AmmoForActionType(rules, weapon, type) : null;
        var ammoRule = ammo is null ? null : ItemRuleOf(rules, ammo.ItemType);
        var ammoBattleRule = ammo is null ? null : rules.Battle.Equipment.Items[ammo.ItemType];

        if (unit.Faction == BattleFaction.Hostile && Turn < battleRule.Combat.AiUseDelay) return NoMessage;
        if (unit.OriginalFaction == BattleFaction.Player && !research.DebugMode &&
            !rule.Requirements.All(requirement => research.IsResearched(rules.Research.GetExternalId(requirement))))
            return new BattleWeaponUse(false, "STR_UNABLE_TO_USE_ALIEN_ARTIFACT_UNTIL_RESEARCHED");
        if (battleRule.PsiRequired && unit.Stats.PsiSkill <= 0) return NoMessage;
        if (battleRule.ManaRequired && unit.OriginalFaction == BattleFaction.Player)
        {
            var combat = rules.Campaign.Combat;
            if (!combat.ManaEnabled) return NoMessage;
            // SavedGame::isManaUnlocked: no research is named, or it is done (always, in the debug mode).
            if (!(combat.ManaUnlockResearch.Length == 0 || combat.ManaUnlockResearch == "STR_NULL" || research.DebugMode || research.IsResearched(combat.ManaUnlockResearch)))
                return NoMessage;
        }
        if (Depth == 0)
        {
            if (rule.WaterOnly || ammoRule is { WaterOnly: true }) return new BattleWeaponUse(false, "STR_UNDERWATER_EQUIPMENT");
        }
        else if (battleRule.Combat.LandOnly || ammoBattleRule is { Combat.LandOnly: true }) return new BattleWeaponUse(false, "STR_LAND_EQUIPMENT");
        if (rule.BlockingBothHands && unit.Faction == BattleFaction.Player && !berserking && HandWeapon(unit, LeftHand) is not null && HandWeapon(unit, RightHand) is not null)
            return new BattleWeaponUse(false, "STR_MUST_USE_BOTH_HANDS");
        return BattleWeaponUse.Allowed;
    }

    /// <summary>
    /// Projectile::getHitChance: the chance to hit from the hit chance table at a distance in tiles (1 to 40) and an accuracy (0 to 120), or the accuracy
    /// itself without a table; an odd accuracy is the rounded mean of the two even ones around it.
    /// </summary>
    internal static int HitChance(int distance, int accuracy, IReadOnlyList<int>? table)
    {
        if (table is null) return accuracy;
        accuracy = Math.Clamp(accuracy, 0, HitChanceMaxAccuracy);
        distance = Math.Clamp(distance, 1, HitChanceDistanceRows);
        if (accuracy % 2 == 0) return table[(distance - 1) * HitChanceAccuracyPerRow + accuracy / 2];
        var index = (distance - 1) * HitChanceAccuracyPerRow + (accuracy - 1) / 2;
        return (int)Math.Floor((table[index] + (double)table[index + 1]) / 2 + 0.5);
    }
}
