using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;

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
        "hitUnit", "damageUnit", "damageSpecialUnit", "tryMeleeAttackUnit", "reactionUnitAction", "reactionUnitReaction", "aiCalculateTargetWeight", "awardExperience",
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
    /// The armors and item types a command of the battle can bring into play, computed from the rules: those of the units that are not removed and of the
    /// items held, and from each, transitively, what a command can create: an item's <c>spawnUnit</c>, <c>spawnItem</c> and <c>zombieUnit</c> (with the zombie
    /// units by armor and by type), a unit type's <c>spawnUnit</c>, armor, built-in weapons, melee and psi weapons, an armor's corpse items, built-in, special and
    /// self-destruct items, and a firearm's compatible ammunition. The scripts of a unit or an item that appears during a command run for it as for one that was there
    /// (<c>ConvertUnit</c>, <c>SpawnNewUnit</c>, <c>SpawnNewItem</c>, the corpses), so the checks that refuse a script look at all of them.
    /// </summary>
    internal (HashSet<string> Armors, HashSet<string> Items) ReachableRules(RuntimeRuleCatalog rules)
    {
        var armors = new HashSet<string>(StringComparer.Ordinal);
        var items = new HashSet<string>(StringComparer.Ordinal);
        var unitTypes = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(char Kind, string Id)>();
        void Armor(string id)
        {
            if (id.Length != 0 && armors.Add(id)) pending.Push(('a', id));
        }
        void Item(string id)
        {
            if (id.Length != 0 && items.Add(id)) pending.Push(('i', id));
        }
        void UnitType(string id)
        {
            if (id.Length != 0 && unitTypes.Add(id)) pending.Push(('u', id));
        }

        foreach (var unit in _units)
            if (unit.Status != BattleUnitStatus.IgnoreMe) Armor(unit.ArmorId);
        foreach (var item in _items) Item(item.ItemType);
        while (pending.Count != 0)
        {
            var (kind, id) = pending.Pop();
            switch (kind)
            {
                case 'a':
                    if (rules.Armors.TryGet(id, out var armorHandle))
                        foreach (var corpse in rules.Armors[armorHandle].Value.Script.CorpseBattle)
                            if (corpse is { } corpseItem) Item(rules.Items.GetExternalId(corpseItem));
                    if (rules.Battle.Equipment.Armors.TryGetValue(id, out var armor))
                    {
                        foreach (var builtIn in armor.BuiltInWeapons) Item(builtIn);
                        Item(armor.SpecialWeapon);
                        Item(armor.SelfDestructItem);
                    }
                    break;
                case 'i':
                    if (!rules.Battle.Equipment.Items.TryGetValue(id, out var rule)) break;
                    Item(rule.SpawnItem);
                    UnitType(rule.SpawnUnit);
                    UnitType(rule.Combat.ZombieUnit);
                    foreach (var named in rule.Combat.ZombieUnitsByMaleArmor.Values.Concat(rule.Combat.ZombieUnitsByFemaleArmor.Values).Concat(rule.Combat.ZombieUnitsByType.Values))
                        UnitType(named);
                    foreach (var ammo in rule.Combat.CompatibleAmmo.SelectMany(static slot => slot)) Item(ammo);
                    break;
                default:
                    if (rules.Battle.Equipment.Units.TryGetValue(id, out var unitRule))
                    {
                        UnitType(unitRule.SpawnUnit);
                        Item(unitRule.MeleeWeapon);
                        Item(unitRule.PsiWeapon);
                        foreach (var set in unitRule.BuiltInWeaponSets)
                            foreach (var weapon in set) Item(weapon);
                        foreach (var set in unitRule.WeightedBuiltInWeaponSets)
                            foreach (var weapon in set.Keys) Item(weapon);
                    }
                    if (rules.Units.TryGet(id, out var unitHandle) && rules.Units[unitHandle].Value.Armor is { } unitArmor)
                        Armor(rules.Armors.GetExternalId(unitArmor));
                    break;
            }
        }
        return (armors, items);
    }

    /// <summary>
    /// A combat script or a stat bonus written as code that the battle's units or items would run, or a global event of either: refused with
    /// <c>battle.combat-script-unsupported</c> or <c>battle.stat-bonus-script-unsupported</c> (null when none). Armors and items are those of the battle and
    /// everything a command can create from them (<see cref="ReachableRules"/>). A firearm in play that takes a psi amplifier as ammunition is refused
    /// with <c>battle.psi-unsupported</c>: its shot would be a psi attack, which is branch 6's, and the refusal must come before the shot's changes.
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
        var (armorIds, itemTypes) = ReachableRules(rules);
        foreach (var armorId in armorIds)
        {
            if (armorsWithHooks.Contains(armorId)) return BattleFlowReasons.CombatScriptUnsupported;
            armorBonus |= rules.Battle.Equipment.Armors.TryGetValue(armorId, out var armor) &&
                (armor.StatBonuses.PsiDefence.IsScript || armor.StatBonuses.MeleeDodge.IsScript);
        }
        foreach (var itemType in itemTypes)
        {
            if (itemsWithHooks.Contains(itemType)) return BattleFlowReasons.CombatScriptUnsupported;
            itemBonus |= rules.Battle.Equipment.Items.TryGetValue(itemType, out var rule) && rule.Combat.StatBonuses.AnyScript;
        }
        if (armorBonus || itemBonus) return BattleFlowReasons.StatBonusScriptUnsupported;
        foreach (var itemType in itemTypes)
            if (rules.Items.TryGet(itemType, out var handle) && rules.Items[handle].Value.BattleType == RuntimeBattleItem.FirearmBattleType &&
                rules.Battle.Equipment.Items.TryGetValue(itemType, out var firearm) &&
                firearm.Combat.CompatibleAmmo.SelectMany(static slot => slot).Any(ammo => rules.Items.TryGet(ammo, out var ammoHandle) &&
                    rules.Items[ammoHandle].Value.BattleType == RuntimeBattleItem.PsiAmpBattleType))
                return BattleFlowReasons.PsiUnsupported;
        return null;
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
