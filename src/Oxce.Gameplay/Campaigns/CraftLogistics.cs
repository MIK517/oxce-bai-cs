using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record CraftWeaponSnapshot(string RuleId, int Ammo, bool Rearming = false, bool Disabled = false);
public sealed record CraftVehicleSnapshot(string RuleId, int Ammo)
{
    public int? Size { get; init; }
    public int? SpaceOccupied { get; init; }
    public string PreservationKey { get; init; } = string.Empty;
}
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
    public int Shield { get; init; }
}

public sealed record CraftEffectiveStats(int SoldierCapacity, int VehicleCapacity,
    int FuelMaximum, int ShieldMaximum, bool HasNegativeUnitCapacity);

public static class CraftLogistics
{
    public static CraftEffectiveStats EffectiveStats(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
    {
        long soldiers = rule.SoldierCapacity;
        long vehicles = rule.VehicleCapacity;
        long fuel = rule.FuelMaximum;
        long shield = rule.ShieldCapacity;
        foreach (var weapon in weapons.OfType<CraftWeaponSnapshot>())
        {
            var bonus = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value.BonusStats;
            soldiers += bonus.GetValueOrDefault("soldiers");
            vehicles += bonus.GetValueOrDefault("vehicles");
            fuel += bonus.GetValueOrDefault("fuelMax");
            shield += bonus.GetValueOrDefault("shieldCapacity");
        }
        if (soldiers is < int.MinValue or > int.MaxValue || vehicles is < int.MinValue or > int.MaxValue ||
            fuel is < int.MinValue or > int.MaxValue || shield is < int.MinValue or > int.MaxValue)
            throw new OverflowException("Craft weapon bonuses exceed the supported capacity range.");
        return new(
            Math.Min(Math.Max(0, (int)soldiers), rule.EffectiveMaximumUnits),
            Math.Min(Math.Max(0, (int)vehicles), rule.EffectiveMaximumVehiclesAndLargeSoldiers),
            (int)fuel,
            (int)shield,
            soldiers < 0 || vehicles < 0);
    }

    public static bool TryEffectiveStats(RuntimeCraftRule rule, IEnumerable<CraftWeaponSnapshot?> weapons,
        RuntimeRuleCatalog rules, out CraftEffectiveStats? stats)
    {
        try { stats = EffectiveStats(rule, weapons, rules); return true; }
        catch (OverflowException) { stats = null; return false; }
    }

    public static IReadOnlyDictionary<string, int> UnloadedWeaponItems(CraftLogisticsState state, RuntimeRuleCatalog rules)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var weapon in state.Weapons.OfType<CraftWeaponSnapshot>())
        {
            var rule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            Add(rule.Launcher, 1);
            Add(rule.Clip, WeaponClipCount(weapon, rules));
        }
        return new ReadOnlyDictionary<string, int>(items);

        void Add(string id, int count)
        {
            if (count > 0 && id.Length != 0) items[id] = checked(items.GetValueOrDefault(id) + count);
        }
    }

    public static IReadOnlyDictionary<string, int> UnloadedItems(CraftLogisticsState state, RuntimeRuleCatalog rules)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in UnloadedWeaponItems(state, rules)) Add(pair.Key, pair.Value);
        foreach (var pair in state.Items) Add(pair.Key, pair.Value);
        foreach (var vehicle in state.Vehicles)
        {
            Add(vehicle.RuleId, 1);
            var ammunition = VehicleAmmunition(vehicle, rules);
            Add(ammunition.Id, ammunition.Count);
        }
        return new ReadOnlyDictionary<string, int>(items);

        void Add(string id, int count)
        {
            if (count > 0 && id.Length != 0) items[id] = checked(items.GetValueOrDefault(id) + count);
        }
    }

    public static double StoredSize(CraftLogisticsState state, RuntimeRuleCatalog rules)
    {
        var size = state.Items.Sum(pair => rules.Items[rules.Items.GetRequired(pair.Key)].Value.Size * pair.Value);
        foreach (var vehicle in state.Vehicles)
        {
            size += rules.Items[rules.Items.GetRequired(vehicle.RuleId)].Value.Size;
            var ammunition = VehicleAmmunition(vehicle, rules);
            if (ammunition.Id.Length != 0)
                size += rules.Items[rules.Items.GetRequired(ammunition.Id)].Value.Size * ammunition.Count;
        }
        foreach (var weapon in state.Weapons.OfType<CraftWeaponSnapshot>())
        {
            var rule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            size += rules.Items[rules.Items.GetRequired(rule.Launcher)].Value.Size;
            if (rule.Clip.Length != 0)
                size += rules.Items[rules.Items.GetRequired(rule.Clip)].Value.Size * WeaponClipCount(weapon, rules);
        }
        return size;
    }

    internal static int WeaponClipCount(CraftWeaponSnapshot weapon, RuntimeRuleCatalog rules)
    {
        var rule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
        if (rule.Clip.Length == 0) return 0;
        var clip = rules.Items[rules.Items.GetRequired(rule.Clip)].Value;
        var divisor = clip.ClipSize > 0 ? clip.ClipSize : rule.RearmRate;
        if (divisor <= 0) throw new InvalidDataException("Craft weapon clip divisor must be positive.");
        return checked((int)Math.Floor((double)weapon.Ammo / divisor));
    }

    internal static (string Id, int Count) VehicleAmmunition(CraftVehicleSnapshot vehicle, RuntimeRuleCatalog rules)
    {
        var rule = rules.Items[rules.Items.GetRequired(vehicle.RuleId)].Value;
        if (rule.VehicleFixedAmmoSlot < 0) return ("", 0);
        if (rule.VehicleFixedAmmoSlot >= rule.CompatibleAmmo.Count)
            throw new InvalidDataException("Vehicle ammunition slot is outside the reference range.");
        var ammunition = rule.CompatibleAmmo[rule.VehicleFixedAmmoSlot];
        if (ammunition.Count == 0) return ("", 0);
        var ammo = rules.Items[ammunition[0]].Value;
        return (rules.Items.GetExternalId(ammunition[0]),
            rule.ClipSize > 0 && ammo.ClipSize > 0 ? rule.ClipSize / ammo.ClipSize : ammo.ClipSize);
    }

    public static bool HasNegativeCapacity(CraftLogisticsState state, RuntimeCraftRule rule, RuntimeRuleCatalog rules)
        => EffectiveStats(rule, state.Weapons, rules).HasNegativeUnitCapacity;

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
            Array.AsReadOnly(template?.Vehicles.Select(v => new CraftVehicleSnapshot(v.RuleId, v.Ammo)
            { Size = v.Size, SpaceOccupied = v.SpaceOccupied }).ToArray() ?? []))
        {
            Name = template?.Name ?? string.Empty,
            ExcessFuel = template?.ExcessFuel ?? 0,
            LowFuel = template?.LowFuel ?? false,
            Shield = template?.Shield ?? 0
        };
    }

    public static (CraftLogisticsState State, int FuelItemChange, bool MissingFuel) Refuel(
        CraftLogisticsState state, RuntimeCraftRule rule, RuntimeRuleCatalog rules, int availableFuelItems)
    {
        var maximum = EffectiveStats(rule, state.Weapons, rules).FuelMaximum;
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
        if (EffectiveStats(rule, weapons, rules).HasNegativeUnitCapacity) Array.Clear(weapons);
        return new(0, 0, "STR_REFUELLING", Array.AsReadOnly(weapons),
            new ReadOnlyDictionary<string, int>(new Dictionary<string, int>()), [])
        { Longitude = longitude, Latitude = latitude };
    }

    public static CraftLogisticsState Arrive(CraftLogisticsState state, RuntimeCraftRule rule, RuntimeRuleCatalog rules,
        double longitude, double latitude)
    {
        var weapons = state.Weapons.ToArray();
        var allFull = true;
        for (var i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] is not { } weapon) continue;
            var weaponRule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            if (weapon.Ammo >= weaponRule.AmmoMaximum || weapon.Disabled) continue;
            weapons[i] = weapon with { Rearming = true };
            allFull = false;
        }
        var fuelMaximum = EffectiveStats(rule, weapons, rules).FuelMaximum;
        return state with
        {
            Longitude = longitude,
            Latitude = latitude,
            Weapons = Array.AsReadOnly(weapons),
            Status = state.Damage > 0 ? "STR_REPAIRS" : !allFull ? "STR_REARMING" : state.Fuel < fuelMaximum ? "STR_REFUELLING" : "STR_READY",
        };
    }
}
