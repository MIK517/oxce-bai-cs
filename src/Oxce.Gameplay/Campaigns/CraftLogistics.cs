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
    int FuelMaximum, int ShieldMaximum);
public sealed record CraftUnitCapacities(int SoldierCapacity, int VehicleCapacity);
public sealed record CraftServiceCapacities(int FuelMaximum, int ShieldMaximum);

public static class CraftLogistics
{
    public static CraftEffectiveStats EffectiveStats(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
    {
        var raw = RawEffectiveStats(rule, weapons, rules);
        return new(
            UnitCapacity(raw.Soldiers, rule.EffectiveMaximumUnits),
            UnitCapacity(raw.Vehicles, rule.EffectiveMaximumVehiclesAndLargeSoldiers),
            SupportedValue(raw.Fuel), SupportedValue(raw.Shield));
    }

    public static CraftUnitCapacities EffectiveUnitCapacities(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
    {
        var raw = RawEffectiveStats(rule, weapons, rules);
        return new(UnitCapacity(raw.Soldiers, rule.EffectiveMaximumUnits),
            UnitCapacity(raw.Vehicles, rule.EffectiveMaximumVehiclesAndLargeSoldiers));
    }

    public static CraftServiceCapacities EffectiveServiceCapacities(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
    {
        var raw = RawEffectiveStats(rule, weapons, rules);
        return new(SupportedValue(raw.Fuel), SupportedValue(raw.Shield));
    }

    public static int EffectiveSoldierCapacity(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
    {
        var raw = RawEffectiveStats(rule, weapons, rules);
        return UnitCapacity(raw.Soldiers, rule.EffectiveMaximumUnits);
    }

    public static int EffectiveFuelMaximum(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
        => SupportedValue(RawEffectiveStats(rule, weapons, rules).Fuel);

    public static int EffectiveShieldMaximum(RuntimeCraftRule rule,
        IEnumerable<CraftWeaponSnapshot?> weapons, RuntimeRuleCatalog rules)
        => SupportedValue(RawEffectiveStats(rule, weapons, rules).Shield);

    public static bool TryEffectiveServiceCapacities(RuntimeCraftRule rule, IEnumerable<CraftWeaponSnapshot?> weapons,
        RuntimeRuleCatalog rules, out CraftServiceCapacities capacities)
    {
        var raw = RawEffectiveStats(rule, weapons, rules);
        capacities = new(ClampSupportedValue(raw.Fuel), ClampSupportedValue(raw.Shield));
        return IsSupportedValue(raw.Fuel) && IsSupportedValue(raw.Shield);
    }

    public static bool TryEffectiveUnitCapacities(RuntimeCraftRule rule, IEnumerable<CraftWeaponSnapshot?> weapons,
        RuntimeRuleCatalog rules, out CraftUnitCapacities capacities)
    {
        try { capacities = EffectiveUnitCapacities(rule, weapons, rules); return true; }
        catch (OverflowException) { capacities = null!; return false; }
    }

    public static IReadOnlyDictionary<string, int> UnloadedWeaponItems(CraftLogisticsState state, RuntimeRuleCatalog rules)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        AddUnloadedWeaponItems(items, state, rules);
        return new ReadOnlyDictionary<string, int>(items);
    }

    public static IReadOnlyDictionary<string, int> UnloadedItems(CraftLogisticsState state, RuntimeRuleCatalog rules)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        AddUnloadedWeaponItems(items, state, rules);
        foreach (var pair in state.Items) AddItem(items, pair.Key, pair.Value);
        foreach (var vehicle in state.Vehicles)
        {
            AddItem(items, vehicle.RuleId, 1);
            var ammunition = VehicleAmmunition(vehicle, rules);
            AddItem(items, ammunition.Id, ammunition.Count);
        }
        return new ReadOnlyDictionary<string, int>(items);
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
        => HasNegativeCapacity(rule, state.Weapons, rules);

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
        var maximum = EffectiveFuelMaximum(rule, state.Weapons, rules);
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
        if (HasNegativeCapacity(rule, weapons, rules)) Array.Clear(weapons);
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
        var fuelMaximum = EffectiveFuelMaximum(rule, weapons, rules);
        return state with
        {
            Longitude = longitude,
            Latitude = latitude,
            Weapons = Array.AsReadOnly(weapons),
            Status = state.Damage > 0 ? "STR_REPAIRS" : !allFull ? "STR_REARMING" : state.Fuel < fuelMaximum ? "STR_REFUELLING" : "STR_READY",
        };
    }

    private static RawCraftEffectiveStats RawEffectiveStats(RuntimeCraftRule rule,
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
        return new(soldiers, vehicles, fuel, shield);
    }

    private static void AddUnloadedWeaponItems(Dictionary<string, int> items, CraftLogisticsState state,
        RuntimeRuleCatalog rules)
    {
        foreach (var weapon in state.Weapons.OfType<CraftWeaponSnapshot>())
        {
            var rule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            AddItem(items, rule.Launcher, 1);
            AddItem(items, rule.Clip, WeaponClipCount(weapon, rules));
        }
    }

    private static void AddItem(Dictionary<string, int> items, string id, int count)
    {
        if (count > 0 && id.Length != 0) items[id] = checked(items.GetValueOrDefault(id) + count);
    }

    private static bool HasNegativeCapacity(RuntimeCraftRule rule, IEnumerable<CraftWeaponSnapshot?> weapons,
        RuntimeRuleCatalog rules)
    {
        var raw = RawEffectiveStats(rule, weapons, rules);
        _ = SupportedValue(raw.Soldiers);
        _ = SupportedValue(raw.Vehicles);
        return raw.Soldiers < 0 || raw.Vehicles < 0;
    }

    private static int UnitCapacity(long value, int maximum)
        => Math.Min(Math.Max(0, SupportedValue(value)), maximum);

    private static int SupportedValue(long value)
    {
        if (!IsSupportedValue(value))
            throw new OverflowException("Craft weapon bonuses exceed the supported capacity range.");
        return (int)value;
    }

    private static bool IsSupportedValue(long value) => value is >= int.MinValue and <= int.MaxValue;
    private static int ClampSupportedValue(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private readonly record struct RawCraftEffectiveStats(long Soldiers, long Vehicles, long Fuel, long Shield);
}
