using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Read-only UnitStats fields registered on RuleSoldier and Armor.</summary>
internal sealed partial class CampaignWorld
{
    private static (int Group, string? Key) DetectionUnitRuleStat(string name) => name switch
    {
        "RuleSoldier.StatsMin.getTimeUnits" => (1, "tu"),
        "RuleSoldier.StatsMin.getStamina" => (1, "stamina"),
        "RuleSoldier.StatsMin.getHealth" => (1, "health"),
        "RuleSoldier.StatsMin.getBravery" => (1, "bravery"),
        "RuleSoldier.StatsMin.getReactions" => (1, "reactions"),
        "RuleSoldier.StatsMin.getFiring" => (1, "firing"),
        "RuleSoldier.StatsMin.getThrowing" => (1, "throwing"),
        "RuleSoldier.StatsMin.getStrength" => (1, "strength"),
        "RuleSoldier.StatsMin.getPsiStrength" => (1, "psiStrength"),
        "RuleSoldier.StatsMin.getPsiSkill" => (1, "psiSkill"),
        "RuleSoldier.StatsMin.getMelee" => (1, "melee"),
        "RuleSoldier.StatsMin.getManaPool" => (1, "mana"),
        "RuleSoldier.StatsMax.getTimeUnits" => (2, "tu"),
        "RuleSoldier.StatsMax.getStamina" => (2, "stamina"),
        "RuleSoldier.StatsMax.getHealth" => (2, "health"),
        "RuleSoldier.StatsMax.getBravery" => (2, "bravery"),
        "RuleSoldier.StatsMax.getReactions" => (2, "reactions"),
        "RuleSoldier.StatsMax.getFiring" => (2, "firing"),
        "RuleSoldier.StatsMax.getThrowing" => (2, "throwing"),
        "RuleSoldier.StatsMax.getStrength" => (2, "strength"),
        "RuleSoldier.StatsMax.getPsiStrength" => (2, "psiStrength"),
        "RuleSoldier.StatsMax.getPsiSkill" => (2, "psiSkill"),
        "RuleSoldier.StatsMax.getMelee" => (2, "melee"),
        "RuleSoldier.StatsMax.getManaPool" => (2, "mana"),
        "RuleSoldier.StatsCap.getTimeUnits" => (3, "tu"),
        "RuleSoldier.StatsCap.getStamina" => (3, "stamina"),
        "RuleSoldier.StatsCap.getHealth" => (3, "health"),
        "RuleSoldier.StatsCap.getBravery" => (3, "bravery"),
        "RuleSoldier.StatsCap.getReactions" => (3, "reactions"),
        "RuleSoldier.StatsCap.getFiring" => (3, "firing"),
        "RuleSoldier.StatsCap.getThrowing" => (3, "throwing"),
        "RuleSoldier.StatsCap.getStrength" => (3, "strength"),
        "RuleSoldier.StatsCap.getPsiStrength" => (3, "psiStrength"),
        "RuleSoldier.StatsCap.getPsiSkill" => (3, "psiSkill"),
        "RuleSoldier.StatsCap.getMelee" => (3, "melee"),
        "RuleSoldier.StatsCap.getManaPool" => (3, "mana"),
        _ => (0, null),
    };

    private static int DetectionSoldierStat(string name, RuntimeSoldierRule rule)
    {
        var (group, key) = DetectionUnitRuleStat(name);
        var values = group switch
        {
            1 => rule.MinimumStats,
            2 => rule.MaximumStats,
            3 => rule.StatCaps,
            _ => throw new InvalidOperationException("Detection soldier provider was not preflighted."),
        };
        return values.GetValueOrDefault(key!);
    }
}
