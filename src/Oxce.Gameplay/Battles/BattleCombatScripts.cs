using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// What the combat code (Phase 7 branch 5) would run scripts for. The stat bonuses of items and armors are evaluated natively as
/// <see cref="RuleStatBonus"/> when written as coefficients; the combat hooks and any bonus written as code are tactical script bindings
/// (Phase 7 branch 8), so a battle that binds one is refused where <c>battle.visibility-script-unsupported</c> is: at the briefing, at the first
/// turn and for every command of a running battle (a loaded battle still loads).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>The unit-side combat hooks, owned by armors: <c>ModScript</c>'s hit, damage, melee, reaction and experience parsers.</summary>
    private static readonly string[] CombatUnitHooks =
    [
        "hitUnit", "damageUnit", "damageSpecialUnit", "tryMeleeAttackUnit", "reactionUnitAction", "reactionUnitReaction", "awardExperience",
    ];

    /// <summary>The item-side combat hooks, owned by items: the ammunition forms of the hit and damage hooks, the melee attack and the reaction weapon.</summary>
    private static readonly string[] CombatItemHooks =
    [
        "hitUnitAmmo", "damageUnitAmmo", "damageSpecialUnitAmmo", "tryMeleeAttackItem", "reactionWeaponAction",
    ];

    /// <summary>The stat bonus parsers of an item (<c>RuleItem</c>) and of an armor (<c>psiDefence</c> and <c>meleeDodge</c>).</summary>
    private static readonly string[] ItemBonusParsers =
    [
        "damageBonusBonusStats", "meleeBonusBonusStats", "accuracyMultiplierBonusStats", "meleeMultiplierBonusStats", "throwMultiplierBonusStats",
        "closeQuartersMultiplierBonusStats",
    ];

    private static readonly string[] ArmorBonusParsers = ["psiDefenceBonusStats", "meleeDodgeBonusStats"];

    /// <summary>
    /// A combat script or a stat bonus written as code that the battle's units or items would run, or a global event of either: refused with
    /// <c>battle.combat-script-unsupported</c> or <c>battle.stat-bonus-script-unsupported</c> (null when none). Armors are those of the battle's units
    /// that are not removed, items those it holds.
    /// </summary>
    internal CampaignReason? CombatScriptRefusal(RuntimeContent content)
    {
        if (CombatUnitHooks.Concat(CombatItemHooks).Any(parser => HasEventPlan(content, parser))) return BattleFlowReasons.CombatScriptUnsupported;
        if (ItemBonusParsers.Concat(ArmorBonusParsers).Any(parser => HasEventPlan(content, parser))) return BattleFlowReasons.StatBonusScriptUnsupported;
        var unitHooks = new HashSet<string>(CombatUnitHooks, StringComparer.Ordinal);
        var itemHooks = new HashSet<string>(CombatItemHooks, StringComparer.Ordinal);
        var armorsWithHooks = new HashSet<string>(StringComparer.Ordinal);
        var itemsWithHooks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in content.Scripts)
        {
            if (artifact.OwnerSection == "armors" && unitHooks.Contains(artifact.ParserName)) armorsWithHooks.Add(artifact.OwnerId);
            else if (artifact.OwnerSection == "items" && itemHooks.Contains(artifact.ParserName)) itemsWithHooks.Add(artifact.OwnerId);
        }
        var rules = content.RuntimeRules;
        var armorBonus = false;
        var itemBonus = false;
        foreach (var unit in _units)
        {
            if (unit.Status == BattleUnitStatus.IgnoreMe) continue;
            if (armorsWithHooks.Contains(unit.ArmorId)) return BattleFlowReasons.CombatScriptUnsupported;
            armorBonus |= rules.Battle.Equipment.Armors.TryGetValue(unit.ArmorId, out var armor) &&
                (armor.StatBonuses.PsiDefence.IsScript || armor.StatBonuses.MeleeDodge.IsScript);
        }
        foreach (var item in _items)
        {
            if (itemsWithHooks.Contains(item.ItemType)) return BattleFlowReasons.CombatScriptUnsupported;
            itemBonus |= rules.Battle.Equipment.Items.TryGetValue(item.ItemType, out var rule) && rule.Combat.StatBonuses.AnyScript;
        }
        return armorBonus || itemBonus ? BattleFlowReasons.StatBonusScriptUnsupported : null;
    }
}

/// <summary>A unit as the stat bonuses read it (<c>statDataMap</c>'s getters on a <c>BattleUnit</c>).</summary>
internal sealed partial class BattleUnit : IStatBonusUnit
{
    public int BaseStat(StatBonusBaseStat stat) => stat switch
    {
        StatBonusBaseStat.TimeUnits => Stats.TimeUnits,
        StatBonusBaseStat.Stamina => Stats.Stamina,
        StatBonusBaseStat.Health => Stats.Health,
        StatBonusBaseStat.Bravery => Stats.Bravery,
        StatBonusBaseStat.Reactions => Stats.Reactions,
        StatBonusBaseStat.Firing => Stats.Firing,
        StatBonusBaseStat.Throwing => Stats.Throwing,
        StatBonusBaseStat.Strength => Stats.Strength,
        StatBonusBaseStat.PsiStrength => Stats.PsiStrength,
        StatBonusBaseStat.PsiSkill => Stats.PsiSkill,
        StatBonusBaseStat.Melee => Stats.Melee,
        StatBonusBaseStat.Mana => Stats.Mana,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    /// <summary>BattleUnit::getFatalWounds: the wounds of every body part.</summary>
    public int FatalWoundCount => FatalWounds.Sum();

    /// <summary>BattleUnit::getRankInt.</summary>
    public int RankInt => Rank;
}
