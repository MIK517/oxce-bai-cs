using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// <c>TileEngine::ReactionScore</c>: a unit that could react to a mover, the weapon and action it would use (none when it has no way to react), its
/// reaction score, what each reaction takes off that score and how many times it has reacted in this check.
/// </summary>
internal sealed class BattleReaction(BattleUnit unit, double score)
{
    public BattleUnit Unit { get; } = unit;
    public BattleItem? Weapon { get; set; }
    public BattleActionType AttackType { get; set; } = BattleActionType.None;
    public double Score { get; set; } = score;
    public double Reduction { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>
/// Phase 7 branch 5 S9: who reacts to a move and with what (<c>TileEngine::determineReactionType</c> and <c>getReactor</c>, with the spotting of
/// <see cref="BattleState.SpottingUnits"/>), and the weapon helpers they read (<c>BattleUnit::getWeaponForReactions</c>, <c>getUtilityWeapon</c> and
/// <c>getSpecialWeapon</c>).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>BattleUnit::getSpecialWeapon(type): the first special weapon of the unit of that battle type.</summary>
    private BattleItem? SpecialWeapon(BattleUnit unit, int battleType) =>
        SpecialWeapons(unit).FirstOrDefault(special => ItemRuleOf(special.ItemType).BattleType == battleType);

    /// <summary>BattleUnit::getUtilityWeapon(type): the weapon of that battle type in the right hand, else the left, else a special weapon.</summary>
    private BattleItem? UtilityWeapon(BattleUnit unit, int battleType)
    {
        if (HandWeapon(unit, RightHand) is { } right && ItemRuleOf(right.ItemType).BattleType == battleType) return right;
        if (HandWeapon(unit, LeftHand) is { } left && ItemRuleOf(left.ItemType).BattleType == battleType) return left;
        return SpecialWeapon(unit, battleType);
    }

    /// <summary>The battle types <see cref="EmptyHandWeapon"/> looks at, in order.</summary>
    private static readonly int[] ReactionEmptyHandTypes =
        [RuntimeBattleItem.MeleeBattleType, RuntimeBattleItem.PsiAmpBattleType, RuntimeBattleItem.FirearmBattleType];

    /// <summary>The special weapon that uses an empty hand: the first of a melee, psi and firearm special weapon that does, or null (the loop of the reference's two callers).</summary>
    private BattleItem? EmptyHandWeapon(BattleUnit unit) => FirstEmptyHandSpecialWeapon(unit, ReactionEmptyHandTypes, shown: false);

    /// <summary>
    /// The first special weapon of the given battle types, in their order, that uses an empty hand (<c>specialUseEmptyHand</c>), and with
    /// <paramref name="shown"/> also <c>specialUseEmptyHandShow</c>; null for none.
    /// </summary>
    private BattleItem? FirstEmptyHandSpecialWeapon(BattleUnit unit, int[] types, bool shown)
    {
        foreach (var type in types)
            if (SpecialWeapon(unit, type) is { } special && RequiredRules.Battle.Equipment.Items[special.ItemType].Combat is var combat && combat.SpecialUseEmptyHand &&
                (!shown || combat.SpecialUseEmptyHandShow)) return special;
        return null;
    }

    /// <summary>
    /// BattleUnit::getWeaponForReactions: the weapon of the preferred hand (an empty hand falls back on a special weapon that uses an empty hand, never a psi
    /// one); a melee weapon as it is, any other only with ammunition and a snap shot that costs time units.
    /// </summary>
    internal BattleItem? WeaponForReactions(BattleUnit unit)
    {
        if (unit.PreferredHandForReactions.Length == 0) return null;
        var weapon = HandWeapon(unit, unit.PreferredHandForReactions == RightHand ? RightHand : LeftHand);
        if (weapon is null)
        {
            weapon = EmptyHandWeapon(unit);
            // Only a melee weapon or a firearm: a psi amp has neither a hit nor a snap shot.
            if (weapon is not null && ItemRuleOf(weapon.ItemType).BattleType == RuntimeBattleItem.PsiAmpBattleType) weapon = null;
        }
        if (weapon is null) return null;
        if (ItemRuleOf(weapon.ItemType).BattleType == RuntimeBattleItem.MeleeBattleType) return weapon;
        // ignore weapons without ammo (rules out grenades)
        if (!HaveAnyAmmo(weapon)) return null;
        return unit.ActionCost(RequiredRules.Battle.Equipment.Items[weapon.ItemType].ActionCosts, BattleActionType.SnapShot).Time > 0 ? weapon : null;
    }

    /// <summary>
    /// TileEngine::determineReactionType: the weapon and action a unit would react to the target with. The candidates are the preferred weapon (X-COM's units
    /// only), the melee weapon, then the main hand weapon (the quickest for the AI, the selected one for a player); one the player disabled for reactions
    /// is skipped. The first that can strike (a melee reach to the target, ammunition for the hit and the time units for it) wins, else the first with a
    /// snap shot (a firearm in range, with ammunition and the time units). The reduction is the action's time units (a percentage of the unit's time units, or a flat number) times its reactions over its time units.
    /// </summary>
    internal BattleReaction DetermineReactionType(BattleUnit unit, BattleUnit target)
    {
        var reaction = new BattleReaction(unit, unit.ReactionScore);
        // to avoid 0 that cause infinite loop we set minimal reaction handled by logic
        if (reaction.Score <= 0.001) return reaction;

        BattleReaction Set(BattleActionType type, BattleItem weapon)
        {
            reaction.AttackType = type;
            reaction.Weapon = weapon;
            reaction.Reduction = 1.0 * BattleActionCost.For(RequiredRules, type, unit, weapon).Time * unit.Stats.Reactions / unit.Stats.TimeUnits;
            return reaction;
        }

        var weapons = new List<BattleItem>();
        // 1. first try the preferred weapon (player units only... to prevent abuse)
        var isPlayer = unit.Faction == BattleFaction.Player;
        if (isPlayer && WeaponForReactions(unit) is { } preferred) weapons.Add(preferred);
        // 2. then prioritize melee
        if (UtilityWeapon(unit, RuntimeBattleItem.MeleeBattleType) is { } melee) weapons.Add(melee);
        // 3. then the rest (AI: quickest weapon, Player: last selected/main weapon)
        if (MainHandWeapon(unit, quickest: !isPlayer, needAmmo: true, reactions: true) is { } other) weapons.Add(other);

        var direction = unit.Direction;
        // temporarily face the target to allow melee reactions when attacked from any side, not just from the front
        if (RequiredRules.Campaign.ExtendedMeleeReactions == 2) direction = BattleGeometry.DirectionTo(unit.RequiredPosition, target.RequiredPosition);

        BattleItem? disabledLeft = null, disabledRight = null;
        // player units only... to prevent abuse
        if (isPlayer)
        {
            var leftHand = HandWeapon(unit, LeftHand);
            var rightHand = HandWeapon(unit, RightHand);
            var emptyHand = (leftHand is null && unit.ReactionsDisabledForLeftHand) || (rightHand is null && unit.ReactionsDisabledForRightHand)
                ? EmptyHandWeapon(unit)
                : null;
            disabledLeft = unit.ReactionsDisabledForLeftHand ? leftHand ?? emptyHand : null;
            disabledRight = unit.ReactionsDisabledForRightHand ? rightHand ?? emptyHand : null;
        }

        foreach (var weapon in weapons)
        {
            // the player doesn't want to react with this weapon
            if (weapon == disabledLeft || weapon == disabledRight) continue;

            if (CanUseWeapon(ResearchGate, weapon, unit, false, BattleActionType.Hit).Usable &&
                // has a weapon capable of melee and is in melee range
                ValidMeleeRange(unit.RequiredPosition, direction, unit, target, out _) &&
                AmmoForActionWithMessage(weapon, MeleeActionIndex, out _, out _) is not null &&
                BattleActionCost.For(RequiredRules, BattleActionType.Hit, unit, weapon).HaveTimeUnits())
                return Set(BattleActionType.Hit, weapon);

            if (CanUseWeapon(ResearchGate, weapon, unit, false, BattleActionType.SnapShot).Usable &&
                // has a gun capable of snap shot with ammo
                ItemRuleOf(weapon.ItemType).BattleType == RuntimeBattleItem.FirearmBattleType &&
                !IsOutOfRange(weapon, DistanceSquaredBetween(unit.RequiredPosition, target.RequiredPosition)) &&
                AmmoForActionWithMessage(weapon, SnapActionIndex, out _, out _) is not null &&
                BattleActionCost.For(RequiredRules, BattleActionType.SnapShot, unit, weapon).HaveTimeUnits())
                return Set(BattleActionType.SnapShot, weapon);
        }
        return reaction;
    }

    /// <summary>
    /// TileEngine::getReactor: the spotter with the highest reaction score that is not out or respawning, if it reaches the mover's score. An X-COM
    /// unit that gets to react (or would) is trained in reactions, each time it is asked, and that is a change of the battle.
    /// </summary>
    internal BattleReaction? GetReactor(IReadOnlyList<BattleReaction> spotters, BattleUnit unit)
    {
        BattleReaction? best = null;
        foreach (var spotter in spotters)
            if (!spotter.Unit.IsOut && !spotter.Unit.Respawn && (best is null || spotter.Score > best.Score)) best = spotter;
        if (best is null || unit.ReactionScore > best.Score) return null;
        if (best.Unit.OriginalFaction == BattleFaction.Player)
        {
            AddExperience(best.Unit, TrainingKind.Reactions);
            MarkChanged();
        }
        return best;
    }
}
