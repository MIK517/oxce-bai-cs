using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record CraftWeaponSnapshot(string RuleId, int Ammo, bool Rearming = false, bool Disabled = false);
public sealed record CraftVehicleSnapshot(string RuleId, int Ammo);
public sealed record CraftLogisticsState(int Fuel, int Damage, string Status,
    IReadOnlyList<CraftWeaponSnapshot?> Weapons, IReadOnlyDictionary<string, int> Items,
    IReadOnlyList<CraftVehicleSnapshot> Vehicles)
{
    public string Name { get; init; } = string.Empty;
    public double Longitude { get; init; }
    public double Latitude { get; init; }
    public bool LowFuel { get; init; }
    public int ExcessFuel { get; init; }
    public bool IsAutoPatrolling { get; init; }
}

public static class CraftLogistics
{
    public static CraftLogisticsState LoadStarting(RuntimeCraftRule rule, RuntimeCraftTemplate? template)
    {
        if (rule.WeaponSlots is < 0 or > 4) throw new InvalidDataException("Craft weapon slot count is outside the reference range.");
        var weapons = new CraftWeaponSnapshot?[rule.WeaponSlots];
        for (var i = 0; i < weapons.Length && i < (template?.Weapons.Count ?? 0); i++)
            if (template!.Weapons[i] is { } weapon)
                weapons[i] = new(weapon.RuleId, weapon.Ammo, weapon.Rearming, weapon.Disabled);
        return new(template?.Fuel ?? 0, template?.Damage ?? 0, template?.Status ?? "STR_READY",
            Array.AsReadOnly(weapons), new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(
                template?.Items ?? new Dictionary<string, int>(), StringComparer.Ordinal)),
            Array.AsReadOnly(template?.Vehicles.Select(v => new CraftVehicleSnapshot(v.RuleId, v.Ammo)).ToArray() ?? []))
        { Name = template?.Name ?? string.Empty, ExcessFuel = template?.ExcessFuel ?? 0, LowFuel = template?.LowFuel ?? false };
    }

    public static (CraftLogisticsState State, int FuelItemChange, bool MissingFuel) Refuel(
        CraftLogisticsState state, RuntimeCraftRule rule, RuntimeRuleCatalog rules, int availableFuelItems)
    {
        var maximum = checked(rule.FuelMaximum + state.Weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
            rules.CraftWeapons[rules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("fuelMax")));
        if (maximum < 0) throw new InvalidDataException("Craft maximum fuel cannot be negative.");
        var itemChange = 0;
        var missing = false;
        if (state.Fuel < maximum)
        {
            if (rule.RefuelItem is null || availableFuelItems > 0)
            {
                if (rule.RefuelItem is not null) itemChange--;
                var fuel = checked(state.Fuel + rule.RefuelRate);
                var excess = state.ExcessFuel;
                if (fuel > maximum && rule.RefuelItem is not null)
                {
                    var overflow = checked(fuel - maximum + excess);
                    if (overflow > 0)
                    {
                        if (rule.RefuelRate <= 0) throw new InvalidDataException("Item refuelling requires a positive rate.");
                        itemChange = checked(itemChange + overflow / rule.RefuelRate);
                        excess = overflow % rule.RefuelRate;
                    }
                }
                state = state with { Fuel = Math.Clamp(fuel, 0, maximum), ExcessFuel = excess, LowFuel = rule.RefuelItem is null && state.LowFuel };
            }
            else if (!state.LowFuel)
            {
                missing = true;
                state = state.Fuel > 0 ? state with { Status = "STR_READY" } : state with { LowFuel = true };
            }
        }
        if (state.Fuel >= maximum)
            state = state with { Status = state.Weapons.Any(w => w is { Rearming: true }) ? "STR_REARMING" : "STR_READY" };
        return (state, itemChange, missing);
    }

    public static CraftLogisticsState Purchase(RuntimeCraftRule rule, RuntimeRuleCatalog rules, double longitude, double latitude)
    {
        if (rule.WeaponSlots is < 0 or > 4) throw new InvalidDataException("Craft weapon slot count is outside the reference range.");
        var weapons = new CraftWeaponSnapshot?[rule.WeaponSlots];
        for (var i = 0; i < weapons.Length && i < rule.FixedWeaponSlots.Count; i++)
            if (rule.FixedWeaponSlots[i].Length != 0) weapons[i] = new(rule.FixedWeaponSlots[i], 0);
        var soldiers = rule.SoldierCapacity;
        var vehicles = rule.VehicleCapacity;
        foreach (var weapon in weapons.OfType<CraftWeaponSnapshot>())
        {
            var bonus = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value.BonusStats;
            soldiers = checked(soldiers + bonus.GetValueOrDefault("soldiers"));
            vehicles = checked(vehicles + bonus.GetValueOrDefault("vehicles"));
        }
        if (soldiers < 0 || vehicles < 0) Array.Clear(weapons);
        return new(0, 0, "STR_REFUELLING", Array.AsReadOnly(weapons),
            new ReadOnlyDictionary<string, int>(new Dictionary<string, int>()), [])
        { Longitude = longitude, Latitude = latitude };
    }

    public static CraftLogisticsState Arrive(CraftLogisticsState state, RuntimeCraftRule rule, RuntimeRuleCatalog rules,
        double longitude, double latitude)
    {
        var weapons = state.Weapons.ToArray();
        var allFull = true;
        var fuelMaximum = rule.FuelMaximum;
        for (var i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] is not { } weapon) continue;
            var weaponRule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            fuelMaximum = checked(fuelMaximum + weaponRule.BonusStats.GetValueOrDefault("fuelMax"));
            if (weapon.Ammo >= weaponRule.AmmoMaximum || weapon.Disabled) continue;
            weapons[i] = weapon with { Rearming = true };
            allFull = false;
        }
        return state with
        {
            Longitude = longitude, Latitude = latitude, Weapons = Array.AsReadOnly(weapons),
            Status = state.Damage > 0 ? "STR_REPAIRS" : !allFull ? "STR_REARMING" : state.Fuel < fuelMaximum ? "STR_REFUELLING" : "STR_READY",
        };
    }
}
