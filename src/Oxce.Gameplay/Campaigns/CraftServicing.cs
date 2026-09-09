using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Hourly maintenance from Craft.cpp and CraftWeapon.cpp at reference 4df3a5e.</summary>
public static class CraftServicing
{
    public static CraftLogisticsState ReuseItem(CraftLogisticsState state, RuntimeCraftRule craftRule,
        RuntimeRuleCatalog rules, RuleHandle<ItemRuleFamily> item)
    {
        if (state.Status is not ("STR_READY" or "STR_REFUELLING")) return state;
        var weapons = state.Weapons.ToArray();
        var rearming = false;
        for (var slot = 0; slot < weapons.Length; slot++)
        {
            if (weapons[slot] is not { } weapon || weapon.Disabled) continue;
            var weaponRule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            if (weapon.Ammo >= weaponRule.AmmoMaximum || weaponRule.Clip.Length == 0 ||
                rules.Items.GetRequired(weaponRule.Clip) != item) continue;
            weapons[slot] = weapon with { Rearming = true };
            rearming = true;
        }
        if (rearming) return state with { Status = "STR_REARMING", Weapons = Array.AsReadOnly(weapons) };
        if (state.Status == "STR_READY" && craftRule.RefuelItem == item)
        {
            var fuelMaximum = CraftLogistics.EffectiveStats(craftRule, state.Weapons, rules).FuelMaximum;
            if (state.Fuel < Math.Max(0, fuelMaximum)) return state with { Status = "STR_REFUELLING" };
        }
        return state;
    }

    public static CraftLogisticsState Repair(CraftLogisticsState state, RuntimeCraftRule rule)
    {
        var damage = Math.Max(0, checked(state.Damage - rule.RepairRate));
        return state with { Damage = damage, Status = damage == 0 ? "STR_REARMING" : state.Status };
    }

    public static (CraftWeaponSnapshot Weapon, int ClipsUsed) RearmWeapon(CraftWeaponSnapshot weapon,
        RuntimeCraftWeaponRule rule, int available, int clipSize, IRandomSource random)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(available);
        if (rule.RearmRate < 0 || rule.AmmoMaximum < 0 || weapon.Ammo < 0)
            throw new InvalidDataException("Invalid craft weapon maintenance values.");
        var ammoUsed = rule.RearmRate;
        var saved = 0;
        if (clipSize > 0)
        {
            var needed = Math.Min(rule.RearmRate, checked(rule.AmmoMaximum - weapon.Ammo + clipSize - 1)) / clipSize;
            ammoUsed = checked(Math.Min(available, needed) * clipSize);
            var overused = checked(weapon.Ammo + ammoUsed - rule.AmmoMaximum);
            if (clipSize > 1 && rule.StatisticalBulletSaving && overused > 0 && random.NextInclusive(0, clipSize - 1) < overused)
                saved = 1;
        }
        var ammo = Math.Clamp(checked(weapon.Ammo + ammoUsed), 0, rule.AmmoMaximum);
        return (weapon with { Ammo = ammo, Rearming = ammo < rule.AmmoMaximum }, clipSize <= 0 ? 0 : ammoUsed / clipSize - saved);
    }

    public static (CraftLogisticsState State, string Clip, int ClipsUsed, bool MissingAmmo) Rearm(
        CraftLogisticsState state, RuntimeRuleCatalog rules, IReadOnlyDictionary<string, int> stores, IRandomSource random)
    {
        for (var slot = 0; slot < state.Weapons.Count; slot++)
        {
            if (state.Weapons[slot] is not { Rearming: true } weapon) continue;
            var rule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            var available = stores.GetValueOrDefault(rule.Clip);
            var result = rule.Clip.Length == 0 || available > 0
                ? RearmWeapon(weapon, rule, available, rule.Clip.Length == 0 ? 0 : rules.Items[rules.Items.GetRequired(rule.Clip)].Value.ClipSize, random)
                : (Weapon: weapon, ClipsUsed: 0);
            var missing = rule.Clip.Length != 0 && (available == 0 || result.ClipsUsed == available && result.Weapon.Rearming);
            var weapons = state.Weapons.ToArray();
            weapons[slot] = missing ? result.Weapon with { Rearming = false } : result.Weapon;
            return (state with { Weapons = Array.AsReadOnly(weapons) }, rule.Clip, result.ClipsUsed, missing);
        }
        return (state with { Status = "STR_REFUELLING" }, "", 0, false);
    }
}
