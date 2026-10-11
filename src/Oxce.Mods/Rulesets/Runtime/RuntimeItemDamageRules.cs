using Oxce.Mods.Rulesets.Items;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>Float fields owned by each RuleItem damage copy; script queries do not apply tactical damage.</summary>
public sealed record RuntimeItemDamageRule(int FixedRadius, int RandomType, int ResistType,
    int ArmorIgnore, float ArmorEffectiveness, RuntimeDamageConversions Conversions);

public sealed record RuntimeDamageConversions(float ArmorPre, float Armor, float Energy, float Health,
    float Item, float Mana, float Morale, float Stun, float Tile, float Time, float Wound);

internal static class RuntimeItemDamageProjection
{
    internal static RuntimeItemDamageRule Create(ItemDamageRule data)
    {
        if (data.PredefinedType is < 0 or >= 20)
            throw new InvalidDataException("Predefined item damage type is outside the reference table.");
        var type = data.PredefinedType;
        var area = type is 2 or 3 or 6 or 9;
        var random = type switch { 0 or 9 => 5, 2 => 4, 3 => 9, _ => 8 };
        var armor = type is 2 or 9 ? 0f : 1f;
        var conversions = new RuntimeDamageConversions(0, 0.1f, 0, 1, 0, 0, 0, 0.25f, 0.5f, 0, 1);
        if (type is 6 or 9) conversions = conversions with { Health = 0, Armor = 0, Wound = 0, Item = 0, Tile = 0, Stun = 1 };
        if (type == 2) conversions = conversions with { Armor = 0, Wound = 0, Item = 0, Tile = 0, Stun = 0 };
        if (type == 3) conversions = conversions with { Item = 1 };
        conversions = new(Real("ToArmorPre", conversions.ArmorPre), Real("ToArmor", conversions.Armor),
            Real("ToEnergy", conversions.Energy), Real("ToHealth", conversions.Health),
            Real("ToItem", conversions.Item), Real("ToMana", conversions.Mana),
            Real("ToMorale", conversions.Morale), Real("ToStun", conversions.Stun),
            Real("ToTile", conversions.Tile), Real("ToTime", conversions.Time), Real("ToWound", conversions.Wound));
        return new(data.Integers.GetValueOrDefault("FixRadius", area ? -1 : 0),
            data.Integers.GetValueOrDefault("RandomType", random),
            data.Integers.GetValueOrDefault("ResistType", type ?? 0),
            data.Integers.GetValueOrDefault("ArmorIgnore"), Real("ArmorEffectiveness", armor), conversions);

        float Real(string key, float fallback) => data.Reals.TryGetValue(key, out var value) ? (float)value : fallback;
    }
}
