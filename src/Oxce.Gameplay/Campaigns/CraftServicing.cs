using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Hourly maintenance from Craft.cpp and CraftWeapon.cpp at reference 4df3a5e.</summary>
public static class CraftServicing
{
    /// <summary>Uses the execution transition without selecting an ammunition-saving outcome.</summary>
    public static bool TryForecastRearm(CraftLogisticsState state, RuntimeRuleCatalog rules,
        IReadOnlyDictionary<string, int> stores,
        out (CraftLogisticsState State, string Clip, int ClipsUsed, bool MissingAmmo) result)
    {
        try { result = Rearm(state, rules, stores, ForecastRandom.Instance); return true; }
        catch (UncertainRearmException) { result = (state, "", 0, false); return false; }
    }

    /// <summary>Bounds native clip savings while retaining an exact craft transition, without campaign RNG.</summary>
    public static bool TryForecastRearmRange(CraftLogisticsState state, RuntimeRuleCatalog rules,
        IReadOnlyDictionary<string, int> stores,
        out (CraftLogisticsState State, string Clip, int MinimumUsed, int MaximumUsed, bool MissingAmmo) result)
    {
        if (TryForecastRearm(state, rules, stores, out var exact))
        {
            result = (exact.State, exact.Clip, exact.ClipsUsed, exact.ClipsUsed, exact.MissingAmmo);
            return true;
        }
        var low = Rearm(state, rules, stores, RearmEndpointRandom.Minimum);
        var high = Rearm(state, rules, stores, RearmEndpointRandom.Maximum);
        result = (low.State, low.Clip, Math.Min(low.ClipsUsed, high.ClipsUsed), Math.Max(low.ClipsUsed, high.ClipsUsed), low.MissingAmmo);
        return low.Clip == high.Clip && low.MissingAmmo == high.MissingAmmo &&
            low.State.Weapons.SequenceEqual(high.State.Weapons) && (low.State with { Weapons = high.State.Weapons }) == high.State;
    }

    private sealed class RearmEndpointRandom(bool maximum) : IRandomSource
    {
        internal static readonly RearmEndpointRandom Minimum = new(false);
        internal static readonly RearmEndpointRandom Maximum = new(true);
        public int NextInclusive(int minimum, int maximumValue) => maximum ? maximumValue : minimum;
        public int NextExclusive(int exclusiveMaximum) => throw new UncertainRearmException();
        public double NextUnit() => throw new UncertainRearmException();
    }

    private sealed class UncertainRearmException : Exception;

    private sealed class ForecastRandom : IRandomSource
    {
        internal static readonly ForecastRandom Instance = new();
        public int NextExclusive(int exclusiveMaximum) => throw new UncertainRearmException();
        public int NextInclusive(int minimum, int maximum) => throw new UncertainRearmException();
        public double NextUnit() => throw new UncertainRearmException();
    }

    /// <summary>GeoscapeState::time5Seconds and Craft::setShield, after craft movement.</summary>
    public static CraftLogisticsState RechargeGeoscapeShield(CraftLogisticsState state,
        int capacity, int recharge, IRandomSource random)
    {
        if (state.Shield >= capacity || recharge != -1 && recharge <= 0) return state;
        var shield = capacity;
        if (recharge > 0)
        {
            var total = recharge / 100;
            // The reference draws even when the fractional chance is zero.
            if (RandomChance.Percent(random, recharge % 100)) total++;
            // Avoid the reference's signed overflow for extreme values before clamping.
            shield = (int)Math.Clamp((long)state.Shield + total, 0, capacity);
        }
        return shield == state.Shield ? state : state with { Shield = shield };
    }

    public static CraftLogisticsState ReuseItem(CraftLogisticsState state, RuntimeCraftRule craftRule,
        RuntimeRuleCatalog rules, RuleHandle<ItemRuleFamily> item)
    {
        if (state.Status is not ("STR_READY" or "STR_REFUELLING")) return state;
        var weapons = state.Weapons.ToArray();
        var rearming = false;
        for (var slot = 0; slot < weapons.Length; slot++)
        {
            if (weapons[slot] is not { } weapon || weapon.Disabled) continue;
            var weaponRule = rules.CraftWeapons.GetRequiredValue(weapon.RuleId);
            if (weapon.Ammo >= weaponRule.AmmoMaximum || weaponRule.Clip.Length == 0 ||
                rules.Items.GetRequired(weaponRule.Clip) != item) continue;
            weapons[slot] = weapon with { Rearming = true };
            rearming = true;
        }
        if (rearming) return state with { Status = "STR_REARMING", Weapons = Array.AsReadOnly(weapons) };
        if (state.Status == "STR_READY" && craftRule.RefuelItem == item)
        {
            var fuelMaximum = CraftLogistics.EffectiveFuelMaximum(craftRule, state.Weapons, rules);
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
            var rule = rules.CraftWeapons.GetRequiredValue(weapon.RuleId);
            var available = stores.GetValueOrDefault(rule.Clip);
            var result = rule.Clip.Length == 0 || available > 0
                ? RearmWeapon(weapon, rule, available, rule.Clip.Length == 0 ? 0 : rules.Items.GetRequiredValue(rule.Clip).ClipSize, random)
                : (Weapon: weapon, ClipsUsed: 0);
            var missing = rule.Clip.Length != 0 && (available == 0 || result.ClipsUsed == available && result.Weapon.Rearming);
            var weapons = state.Weapons.ToArray();
            weapons[slot] = missing ? result.Weapon with { Rearming = false } : result.Weapon;
            return (state with { Weapons = Array.AsReadOnly(weapons) }, rule.Clip, result.ClipsUsed, missing);
        }
        return (state with { Status = "STR_REFUELLING" }, "", 0, false);
    }
}
