using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The values <c>ModScript::HitUnit</c> may change: the damage, the body part and the side that take the hit.</summary>
internal readonly record struct BattleHitHookValues(int Damage, int BodyPart, int Side);

/// <summary>The values <c>ModScript::DamageUnit</c> may change: what the hit takes from the unit, as <c>BattleUnit::damage</c> accumulates it.</summary>
internal readonly record struct BattleDamageHookValues(int Health, int Armor, int Stun, int Time, int Energy, int Morale, int Wound, int Transform, int Mana);

/// <summary>The values <c>ModScript::DamageSpecialUnit</c> may change: the special effects of a hit (conversion, self-destruction, morale, fire, spotting).</summary>
internal readonly record struct BattleSpecialHookValues(bool Transform, int TransformChance, bool SelfDestruct, int SelfDestructChance, int MoraleLoss, int Fire,
    int AttackerTurnsSinceSpotted, int AttackerTurnsLeftSpottedForSnipers);

/// <summary>Where a stat bonus is read (<c>RuleItem</c>'s <c>*BonusStats</c> parsers); the scripted form of each is a hook in branch 8.</summary>
internal enum BattleStatBonusKind { Damage, Accuracy, MeleeMultiplier, Throw, CloseQuarters, MeleeBonus, MeleeDodge }

/// <summary>
/// Where the battle's rules would run a tactical script (<c>ModScript</c>), as one method per script. Each method gets what the script reads and the
/// values it may change, and returns the values to carry on with; the default returns them unchanged, which is the unscripted behaviour. The
/// reference runs the item's ammunition script (<c>*Ammo</c>) before the armor's, so a binding runs both before it answers. A slice that ports a
/// function calls the hook where the reference calls the script. A battle whose content binds a script is still refused (the combat, stat bonus
/// and visibility script refusals) until branch 6 and 8 bind the hooks.
/// </summary>
internal interface IBattleHooks
{
    /// <summary><c>ModScript::HitUnit</c> (<c>BattleUnit::damage</c>, after the side and the body part are picked).</summary>
    BattleHitHookValues HitUnit(BattleUnit unit, in BattleAttack attack, int originalDamage, RuntimeItemDamageRule type, BattleHitHookValues values) => values;

    /// <summary><c>ModScript::DamageUnit</c> (what the hit does to the unit's stats, before it is applied).</summary>
    BattleDamageHookValues DamageUnit(BattleUnit unit, in BattleAttack attack, int damage, int originalDamage, int bodyPart, int side, RuntimeItemDamageRule type,
        BattleDamageHookValues values) => values;

    /// <summary><c>ModScript::DamageSpecialUnit</c> (the effects that follow the damage, before they are applied).</summary>
    BattleSpecialHookValues DamageSpecialUnit(BattleUnit unit, in BattleAttack attack, int damage, int originalDamage, int bodyPart, int side,
        RuntimeItemDamageRule type, BattleSpecialHookValues values) => values;

    /// <summary><c>ModScript::AwardExperience</c> (<c>TileEngine::awardExperience</c>): the experience multiplier, in percent, for the training mode.</summary>
    int AwardExperience(BattleUnit attacker, BattleUnit target, BattleItem weapon, BattleActionType type, int multiplier, int trainingMode) => multiplier;

    /// <summary><c>ModScript::VisibilityUnit</c> (<c>TileEngine::visible</c> and <c>isTileInLOS</c>): the visibility quality; the unit is seen above 0.</summary>
    int VisibilityUnit(BattleUnit viewer, BattleUnit? target, int tile, int quality) => quality;

    /// <summary>
    /// <c>ModScript::TryMeleeAttackItem</c> (<c>TileEngine::meleeAttackCalculate</c>, run on the weapon's rules): the success of a melee attack, which the default script
    /// works out from the roll (0 to 99 drawn for it), the attacker's accuracy, the victim's dodge and its penalty for an attack from behind; a result above 0 hits.
    /// <paramref name="arc"/> is the attacker's angle to the victim's facing (0 in front to 4 behind).
    /// </summary>
    int TryMeleeAttackItem(in BattleAttack attack, BattleUnit victim, int attackStrength, int defenseStrength, int roll, int arc, int defenseStrengthPenalty, int result) => result;

    /// <summary><c>ModScript::TryMeleeAttackUnit</c> (the victim's armor, run on the result of the item's script).</summary>
    int TryMeleeAttackUnit(in BattleAttack attack, BattleUnit victim, int attackStrength, int defenseStrength, int result) => result;

    /// <summary>A stat bonus (<c>damageBonusBonusStats</c> and the like): the bonus the unit and item give, as the coefficients computed it.</summary>
    int StatBonus(BattleStatBonusKind kind, BattleUnit? unit, BattleItem? item, int value) => value;
}

/// <summary>The hooks of a battle with no script bound: every method answers with the values it was given.</summary>
internal sealed class DefaultBattleHooks : IBattleHooks
{
    public static readonly DefaultBattleHooks Instance = new();

    private DefaultBattleHooks()
    {
    }
}
