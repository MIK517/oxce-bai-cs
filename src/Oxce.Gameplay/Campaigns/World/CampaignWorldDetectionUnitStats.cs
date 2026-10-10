using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Read-only UnitStats fields registered on RuleSoldier and Armor.</summary>
internal sealed partial class CampaignWorld
{
    internal static (int Group, string? Key) DetectionUnitRuleStat(string name) => name switch
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
        "RuleArmor.Stats.getTimeUnits" => (4, "tu"),
        "RuleArmor.Stats.getStamina" => (4, "stamina"),
        "RuleArmor.Stats.getHealth" => (4, "health"),
        "RuleArmor.Stats.getBravery" => (4, "bravery"),
        "RuleArmor.Stats.getReactions" => (4, "reactions"),
        "RuleArmor.Stats.getFiring" => (4, "firing"),
        "RuleArmor.Stats.getThrowing" => (4, "throwing"),
        "RuleArmor.Stats.getStrength" => (4, "strength"),
        "RuleArmor.Stats.getPsiStrength" => (4, "psiStrength"),
        "RuleArmor.Stats.getPsiSkill" => (4, "psiSkill"),
        "RuleArmor.Stats.getMelee" => (4, "melee"),
        "RuleArmor.Stats.getManaPool" => (4, "mana"),
        _ => (0, null),
    };

    internal static int DetectionSoldierStat(string name, RuntimeSoldierRule rule)
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

    /// <summary>Armor::getArmor through getArmorValueScript: sides outside 0..SIDE_MAX-1 read zero.</summary>
    internal static int DetectionArmorSide(RuntimeArmorRule? rule, int side) =>
        rule is not null && (uint)side < (uint)rule.Script.Sides.Count ? rule.Script.Sides[side] : 0;

    internal static int DetectionArmorValue(string name, RuntimeArmorRule rule)
    {
        if (DetectionUnitRuleStat(name) is (4, { } key)) return rule.Stats.GetValueOrDefault(key);
        return name switch
        {
            "RuleArmor.getSize" => rule.Size,
            "RuleArmor.getVisibilityAtDark" => rule.Vision.VisibilityAtDark,
            "RuleArmor.getVisibilityAtDay" => rule.Vision.VisibilityAtDay,
            "RuleArmor.getCamouflageAtDark" => rule.Vision.CamouflageAtDark,
            "RuleArmor.getCamouflageAtDay" => rule.Vision.CamouflageAtDay,
            "RuleArmor.getAntiCamouflageAtDark" => rule.Vision.AntiCamouflageAtDark,
            "RuleArmor.getAntiCamouflageAtDay" => rule.Vision.AntiCamouflageAtDay,
            "RuleArmor.getHeatVision" => rule.Vision.HeatVision,
            "RuleArmor.getVisibilityThroughFire" => rule.Vision.VisibilityThroughFire,
            "RuleArmor.getPersonalLight" => rule.Vision.PersonalLight,
            "RuleArmor.getPersonalLightHostile" => rule.Vision.PersonalLightHostile,
            "RuleArmor.getPersonalLightNeutral" => rule.Vision.PersonalLightNeutral,
            "RuleArmor.getDrawingRoutine" => rule.Script.DrawingRoutine,
            "RuleArmor.getDrawBubbles" => rule.Script.DrawBubbles ? 1 : 0,
            "RuleArmor.getDeathFrames" => rule.Script.DeathFrames,
            "RuleArmor.getConstantAnimation" => rule.Script.ConstantAnimation ? 1 : 0,
            "RuleArmor.MoveCost.getBaseTimePercent" => rule.Script.MoveCostBase.Time,
            "RuleArmor.MoveCost.getBaseEnergyPercent" => rule.Script.MoveCostBase.Energy,
            "RuleArmor.MoveCost.getBaseNormalTimePercent" => rule.Script.MoveCostBaseNormal.Time,
            "RuleArmor.MoveCost.getBaseNormalEnergyPercent" => rule.Script.MoveCostBaseNormal.Energy,
            "RuleArmor.MoveCost.getBaseFlyTimePercent" => rule.Script.MoveCostBaseFly.Time,
            "RuleArmor.MoveCost.getBaseFlyEnergyPercent" => rule.Script.MoveCostBaseFly.Energy,
            _ => throw new InvalidOperationException("Detection armor provider was not preflighted."),
        };
    }

}
