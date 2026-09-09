using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public readonly record struct SoldierDailyRecovery(int Mana, int Health, float AbsoluteWoundBonus, float RelativeWoundBonus);

/// <summary>Strategic Soldier.cpp recovery and training rules at reference 4df3a5e.</summary>
public static class SoldierReadiness
{
    private static readonly string[] PhysicalStats = ["firing", "health", "melee", "throwing", "strength", "tu", "stamina"];

    public static bool IsWounded(SoldierPersonalState soldier, int manaThreshold = 200, int healthThreshold = 100) =>
        soldier.Recovery > 0 || soldier.ManaMissing > 0 && soldier.ManaMissing > soldier.CurrentStats.GetValueOrDefault("mana") * manaThreshold / 100 ||
        soldier.HealthMissing > 0 && soldier.HealthMissing > soldier.CurrentStats.GetValueOrDefault("health") * healthThreshold / 100;

    public static SoldierPersonalState Recover(SoldierPersonalState soldier, SoldierDailyRecovery recovery)
    {
        var wounds = soldier.Recovery;
        var mana = soldier.ManaMissing;
        var health = soldier.HealthMissing;
        if (wounds > 0)
        {
            wounds -= 1.0f;
            wounds -= recovery.AbsoluteWoundBonus;
            wounds -= recovery.RelativeWoundBonus * soldier.CurrentStats.GetValueOrDefault("health") * 0.01f;
            wounds = Math.Max(0, wounds);
        }
        else
        {
            if (mana > 0 && recovery.Mana > 0) mana = ReplenishMana(mana, recovery.Mana, soldier);
            if (health > 0 && recovery.Health > 0) health = Math.Max(0, checked(health - recovery.Health));
        }
        if (recovery.Mana < 0) mana = ReplenishMana(mana, recovery.Mana, soldier);
        if (!float.IsFinite(wounds)) throw new InvalidDataException("Wound recovery exceeds its finite range.");
        return soldier with { Recovery = wounds, ManaMissing = mana, HealthMissing = health };
    }

    private static int ReplenishMana(int missing, int rate, SoldierPersonalState soldier) =>
        (int)Math.Clamp((long)missing - rate, 0, Math.Max(100, soldier.CurrentStats.GetValueOrDefault("mana") * 2));

    public static bool IsFullyTrained(SoldierPersonalState soldier, RuntimeSoldierRule rule) =>
        PhysicalStats.All(key => soldier.CurrentStats.GetValueOrDefault(key) >= rule.TrainingStatCaps.GetValueOrDefault(key));

    public static SoldierPersonalState TrainPhysical(SoldierPersonalState soldier, RuntimeSoldierRule rule,
        int factor, IRandomSource random, int manaThreshold = 200, int healthThreshold = 100)
    {
        if (IsWounded(soldier, manaThreshold, healthThreshold)) return soldier;
        var stats = new Dictionary<string, short>(soldier.CurrentStats, StringComparer.Ordinal);
        foreach (var key in PhysicalStats)
            if (stats.GetValueOrDefault(key) < rule.StatCaps.GetValueOrDefault(key) &&
                random.NextInclusive(0, rule.TrainingStatCaps.GetValueOrDefault(key)) > stats.GetValueOrDefault(key) &&
                random.NextInclusive(0, 99) < factor)
                stats[key] = unchecked((short)(stats.GetValueOrDefault(key) + 1));
        return soldier with { CurrentStats = new ReadOnlyDictionary<string, short>(stats) };
    }

    public static bool IsFullyPsiTrained(SoldierPersonalState soldier, RuntimeSoldierRule rule, bool strength) =>
        soldier.CurrentStats.GetValueOrDefault("psiSkill") >= rule.StatCaps.GetValueOrDefault("psiSkill") &&
        (!strength || soldier.CurrentStats.GetValueOrDefault("psiStrength") >= rule.StatCaps.GetValueOrDefault("psiStrength"));

    public static SoldierPersonalState TrainPsiDaily(SoldierPersonalState soldier, RuntimeSoldierRule rule, bool strength, IRandomSource random)
    {
        if (!soldier.PsiTraining) return soldier with { Improvement = 0 };
        var stats = new Dictionary<string, short>(soldier.CurrentStats, StringComparer.Ordinal);
        var skill = stats.GetValueOrDefault("psiSkill");
        var improvement = soldier.Improvement;
        var strengthImprovement = soldier.PsiStrImprovement;
        if (skill > 0)
        {
            if (800 >= skill * random.NextInclusive(1, 100) && skill < rule.StatCaps.GetValueOrDefault("psiSkill"))
            { skill = unchecked((short)(skill + 1)); improvement = unchecked(improvement + 1); }
            if (strength)
            {
                var value = stats.GetValueOrDefault("psiStrength");
                if (800 >= value * random.NextInclusive(1, 100) && value < rule.StatCaps.GetValueOrDefault("psiStrength"))
                { stats["psiStrength"] = unchecked((short)(value + 1)); strengthImprovement = unchecked(strengthImprovement + 1); }
            }
        }
        else if (skill < rule.MinimumStats.GetValueOrDefault("psiSkill"))
        {
            skill = unchecked((short)(skill + 1));
            if (skill == rule.MinimumStats.GetValueOrDefault("psiSkill"))
            {
                improvement = rule.MaximumStats.GetValueOrDefault("psiSkill") + random.NextInclusive(0, rule.MaximumStats.GetValueOrDefault("psiSkill") / 2);
                skill = unchecked((short)improvement);
            }
        }
        else skill = unchecked((short)(skill - random.NextInclusive(30, 60)));
        stats["psiSkill"] = skill;
        return soldier with { CurrentStats = new ReadOnlyDictionary<string, short>(stats), Improvement = improvement, PsiStrImprovement = strengthImprovement };
    }

    public static SoldierPersonalState TrainPsiMonthly(SoldierPersonalState soldier, RuntimeSoldierRule rule, bool strength, IRandomSource random)
    {
        if (!soldier.PsiTraining) return soldier with { Improvement = 0, PsiStrImprovement = 0 };
        var stats = new Dictionary<string, short>(soldier.CurrentStats, StringComparer.Ordinal);
        var skill = stats.GetValueOrDefault("psiSkill");
        var psiStrength = stats.GetValueOrDefault("psiStrength");
        var skillCap = rule.StatCaps.GetValueOrDefault("psiSkill");
        var strengthCap = rule.StatCaps.GetValueOrDefault("psiStrength");
        var improvement = 0;
        var strengthImprovement = 0;
        var minimum = rule.MinimumStats.GetValueOrDefault("psiSkill");
        var maximum = rule.MaximumStats.GetValueOrDefault("psiSkill");
        if (skill < -10 + minimum) skill = minimum;
        else if (skill <= maximum) improvement = random.NextInclusive(maximum, maximum + maximum / 2);
        else
        {
            if (skill <= skillCap / 2) improvement = random.NextInclusive(5, 12);
            else if (skill < skillCap) improvement = random.NextInclusive(1, 3);
            if (strength)
            {
                if (psiStrength <= strengthCap / 2) strengthImprovement = random.NextInclusive(5, 12);
                else if (psiStrength < strengthCap) strengthImprovement = random.NextInclusive(1, 3);
            }
        }
        skill = Math.Max(skill, Math.Min(unchecked((short)(skill + improvement)), skillCap));
        psiStrength = Math.Max(psiStrength, Math.Min(unchecked((short)(psiStrength + strengthImprovement)), strengthCap));
        stats["psiSkill"] = unchecked((short)skill);
        stats["psiStrength"] = unchecked((short)psiStrength);
        return soldier with
        {
            CurrentStats = new ReadOnlyDictionary<string, short>(stats),
            Improvement = improvement,
            PsiStrImprovement = strengthImprovement
        };
    }
}
