using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Items;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// The projection of a <c>RuleDamageType</c> (<c>RuleDamageType.cpp</c>, the predefined types of <c>Mod::Mod</c> and an item's
/// <c>damageType</c>, <c>blastRadius</c> and <c>damageAlter</c> or <c>meleeType</c> and <c>meleeAlter</c>). The script queries of the strategic
/// side read it, and the battle's damage and explosions do. <see cref="FixedRadius"/> is <c>FixRadius</c>: 0 for a single target, -1 for a
/// radius from the power, above 0 fixed.
/// </summary>
public sealed record RuntimeItemDamageRule(int FixedRadius, int RandomType, int ResistType,
    int ArmorIgnore, float ArmorEffectiveness, RuntimeDamageConversions Conversions)
{
    /// <summary>The random types of <c>ItemDamageRandomType</c>.</summary>
    public const int RandomDefault = 0;
    public const int RandomUfo = 1;
    public const int RandomTftd = 2;
    public const int RandomFlat = 3;
    public const int RandomFire = 4;
    public const int RandomNone = 5;
    public const int RandomUfoWithTwoDice = 6;
    public const int RandomEasy = 7;
    public const int RandomStandard = 8;
    public const int RandomExplosion = 9;
    public const int RandomUfoWithFourDice = 10;

    /// <summary>The damage types <c>ItemDamageType</c> the resist type names.</summary>
    public const int DamageNone = 0;
    public const int DamageIncendiary = 2;
    public const int DamageHighExplosive = 3;
    public const int DamageSmoke = 9;

    public bool FireBlastCalc { get; init; }
    public bool IgnoreDirection { get; init; }
    public bool IgnoreSelfDestruct { get; init; }
    public bool IgnorePainImmunity { get; init; }
    public bool IgnoreNormalMoraleLose { get; init; }
    public bool IgnoreOverKill { get; init; }
    public float RadiusEffectiveness { get; init; }
    public float RadiusReduction { get; init; } = 10;
    public int FireThreshold { get; init; } = 1000;
    public int SmokeThreshold { get; init; } = 1000;
    public bool RandomHealth { get; init; }
    public bool RandomMana { get; init; }
    public bool RandomArmor { get; init; }
    public bool RandomArmorPre { get; init; }
    public bool RandomWound { get; init; } = true;

    /// <summary><c>ItemWoundRandomType</c>: 0 vanilla, 1 spread.</summary>
    public int RandomWoundType { get; init; }
    public bool RandomItem { get; init; }
    public bool RandomTile { get; init; }
    public bool RandomStun { get; init; } = true;
    public bool RandomEnergy { get; init; }
    public bool RandomTime { get; init; }
    public bool RandomMorale { get; init; }

    /// <summary>1: 50% to 150% of the power; 2: the damage rolled.</summary>
    public int TileDamageMethod { get; init; } = 1;

    /// <summary>The most damage a tile takes, -1 for no limit.</summary>
    public int TileDamageLimit { get; init; } = -1;

    /// <summary>RuleDamageType::isDirect.</summary>
    public bool IsDirect => FixedRadius == 0;

    /// <summary>RuleDamageType::getRandomDamage(power): a roll of the random type in the mod's damage ranges.</summary>
    public int RandomDamage(int power, CampaignInterceptionSettings ranges, IRandomSource random) =>
        RandomDamage(power, ranges, new Roller(random));

    /// <summary>RuleDamageType::getRandomDamage(power, mode): 0 a roll, 1 the lowest damage, 2 the highest.</summary>
    public int RandomDamage(int power, CampaignInterceptionSettings ranges, int mode, IRandomSource? random = null) => mode switch
    {
        0 => RandomDamage(power, ranges, random ?? throw new ArgumentNullException(nameof(random))),
        1 => RandomDamage(power, ranges, default(Lowest)),
        _ => RandomDamage(power, ranges, default(Highest)),
    };

    /// <summary>
    /// RuleDamageType::getRandomDamage with a roll function, which the draws go through in the reference's order: the dice types roll
    /// several times, the default type picks its range from the resist type, and the fire type ignores the power.
    /// </summary>
    public int RandomDamage<TRoll>(int power, CampaignInterceptionSettings ranges, TRoll roll) where TRoll : struct, IDamageRoll
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var randomType = RandomType;
        switch (randomType)
        {
            case RandomUfoWithFourDice:
                return (roll.Pick(0, power) + roll.Pick(0, power) + roll.Pick(0, power) + roll.Pick(0, power)) / 2;
            case RandomUfoWithTwoDice:
                return roll.Pick(0, power) + roll.Pick(0, power);
            case RandomEasy:
                return roll.Pick(power / 2, power * 2);
        }
        if (randomType == RandomDefault)
            randomType = ResistType switch
            {
                DamageNone => RandomNone,
                DamageIncendiary => RandomFire,
                DamageHighExplosive => RandomExplosion,
                DamageSmoke => RandomNone,
                _ => RandomStandard,
            };
        int range;
        switch (randomType)
        {
            case RandomUfo: range = 100; break;
            case RandomTftd: range = 50; break;
            case RandomStandard: range = ranges.DamageRange; break;
            case RandomExplosion: range = ranges.ExplosiveDamageRange; break;
            case RandomFlat: range = 0; break;
            case RandomFire: return roll.Pick(ranges.FireDamageRange[0], ranges.FireDamageRange[1]);
            default: return 0;
        }
        return roll.Pick(power * (100 - range) / 100, power * (100 + range) / 100);
    }

    /// <summary>RuleDamageType::getRandomDamageForTile: a roll in 50% to 150% of the power for method 1, else the damage.</summary>
    public int RandomDamageForTile(int power, int damage, IRandomSource random) =>
        TileDamageMethod == 1 ? Roller.Draw(random, power / 2, 3 * power / 2) : damage;

    /// <summary>RuleDamageType::getHealthFinalDamage.</summary>
    public int HealthFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomHealth, Conversions.Health, damage, random);

    /// <summary>RuleDamageType::getManaFinalDamage.</summary>
    public int ManaFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomMana, Conversions.Mana, damage, random);

    /// <summary>RuleDamageType::getArmorFinalDamage.</summary>
    public int ArmorFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomArmor, Conversions.Armor, damage, random);

    /// <summary>RuleDamageType::getArmorPreFinalDamage.</summary>
    public int ArmorPreFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomArmorPre, Conversions.ArmorPre, damage, random);

    /// <summary>RuleDamageType::getItemFinalDamage.</summary>
    public int ItemFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomItem, Conversions.Item, damage, random);

    /// <summary>RuleDamageType::getStunFinalDamage.</summary>
    public int StunFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomStun, Conversions.Stun, damage, random);

    /// <summary>RuleDamageType::getEnergyFinalDamage.</summary>
    public int EnergyFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomEnergy, Conversions.Energy, damage, random);

    /// <summary>RuleDamageType::getTimeFinalDamage.</summary>
    public int TimeFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomTime, Conversions.Time, damage, random);

    /// <summary>RuleDamageType::getMoraleFinalDamage.</summary>
    public int MoraleFinalDamage(int damage, IRandomSource random) => FinalDamage(RandomMorale, Conversions.Morale, damage, random);

    /// <summary>RuleDamageType::getTileFinalDamage: the tile damage, kept to the limit.</summary>
    public int TileFinalDamage(int damage, IRandomSource random)
    {
        var result = FinalDamage(RandomTile, Conversions.Tile, damage, random);
        return TileDamageLimit >= 0 && result > TileDamageLimit ? TileDamageLimit : result;
    }

    /// <summary>
    /// RuleDamageType::getWoundFinalDamage: the vanilla form wounds with the chance of the potential out of ten for one to three wounds, the
    /// spread form rolls up to the potential; without <see cref="RandomWound"/> the potential is the number of wounds.
    /// </summary>
    public int WoundFinalDamage(int damage, IRandomSource random)
    {
        if (damage <= 0) return 0;
        var potential = Round(damage * Conversions.Wound);
        if (!RandomWound) return potential;
        switch (RandomWoundType)
        {
            case 0:
                if (Roller.Draw(random, 0, 10) < potential) return Roller.Draw(random, 1, 3);
                break;
            case 1:
                return Roller.Draw(random, 0, potential);
        }
        return 0;
    }

    private static int FinalDamage(bool random, float multiplier, int damage, IRandomSource source)
    {
        if (damage <= 0) return 0;
        return random ? Round(Roller.Draw(source, 0, damage) * multiplier) : Round(damage * multiplier);
    }

    // (int)std::round of a float: halves away from zero.
    private static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>The base damage type of <c>Mod::Mod</c> for an <c>ItemDamageType</c> (the default <c>RuleDamageType</c> where it sets nothing).</summary>
    public static RuntimeItemDamageRule Predefined(int type)
    {
        if (type is < 0 or >= 20)
            throw new InvalidDataException("Predefined item damage type is outside the reference table.");
        var conversions = new RuntimeDamageConversions(0, 0.1f, 0, 1, 0, 0, 0, 0.25f, 0.5f, 0, 1);
        var rule = new RuntimeItemDamageRule(0, RuntimeItemDamageRule.RandomStandard, type, 0, 1, conversions) { IgnoreOverKill = type != 0 };
        return type switch
        {
            0 => rule with { RandomType = RuntimeItemDamageRule.RandomNone },
            2 => rule with
            {
                RandomType = RuntimeItemDamageRule.RandomFire,
                FixedRadius = -1,
                FireBlastCalc = true,
                IgnoreDirection = true,
                IgnoreSelfDestruct = true,
                ArmorEffectiveness = 0,
                RadiusEffectiveness = 0.03f,
                FireThreshold = 0,
                TileDamageMethod = 2,
                Conversions = conversions with { Armor = 0, Wound = 0, Item = 0, Tile = 0, Stun = 0 },
            },
            3 => rule with
            {
                RandomType = RuntimeItemDamageRule.RandomExplosion,
                FixedRadius = -1,
                IgnoreSelfDestruct = true,
                RadiusEffectiveness = 0.05f,
                TileDamageMethod = 2,
                Conversions = conversions with { Item = 1 },
            },
            6 => rule with
            {
                FixedRadius = -1,
                IgnoreSelfDestruct = true,
                IgnorePainImmunity = true,
                RadiusEffectiveness = 0.05f,
                RandomStun = false,
                TileDamageMethod = 2,
                Conversions = conversions with { Health = 0, Armor = 0, Wound = 0, Item = 0, Tile = 0, Stun = 1 },
            },
            7 => rule with { IgnoreSelfDestruct = true },
            9 => rule with
            {
                RandomType = RuntimeItemDamageRule.RandomNone,
                FixedRadius = -1,
                IgnoreDirection = true,
                ArmorEffectiveness = 0,
                RadiusEffectiveness = 0.05f,
                SmokeThreshold = 0,
                TileDamageMethod = 2,
                Conversions = conversions with { Health = 0, Armor = 0, Wound = 0, Item = 0, Tile = 0, Stun = 1 },
            },
            _ => rule,
        };
    }

    /// <summary>A way to pick the number between two bounds: a draw, the lowest, or the highest.</summary>
    public interface IDamageRoll
    {
        int Pick(int minimum, int maximum);
    }

    private readonly struct Roller(IRandomSource random) : IDamageRoll
    {
        public int Pick(int minimum, int maximum) => Draw(random, minimum, maximum);

        /// <summary>RNG::generate(min, max); a maximum below the minimum is undefined in the reference and gives the minimum.</summary>
        internal static int Draw(IRandomSource random, int minimum, int maximum) =>
            maximum < minimum ? minimum : random.NextInclusive(minimum, maximum);
    }

    private readonly struct Lowest : IDamageRoll
    {
        public int Pick(int minimum, int maximum) => minimum;
    }

    private readonly struct Highest : IDamageRoll
    {
        public int Pick(int minimum, int maximum) => maximum;
    }
}

public sealed record RuntimeDamageConversions(float ArmorPre, float Armor, float Energy, float Health,
    float Item, float Mana, float Morale, float Stun, float Tile, float Time, float Wound);

internal static class RuntimeItemDamageProjection
{
    /// <summary>The damage type of an item: the predefined type (or the default <c>RuleDamageType</c>) with the keys <c>RuleDamageType::load</c> read over it.</summary>
    internal static RuntimeItemDamageRule Create(ItemDamageRule data)
    {
        var rule = data.PredefinedType is { } type ? RuntimeItemDamageRule.Predefined(type) : new RuntimeItemDamageRule(0, RuntimeItemDamageRule.RandomStandard, 0, 0, 1,
            new(0, 0.1f, 0, 1, 0, 0, 0, 0.25f, 0.5f, 0, 1));
        var conversions = rule.Conversions;
        conversions = new(Real("ToArmorPre", conversions.ArmorPre), Real("ToArmor", conversions.Armor),
            Real("ToEnergy", conversions.Energy), Real("ToHealth", conversions.Health),
            Real("ToItem", conversions.Item), Real("ToMana", conversions.Mana),
            Real("ToMorale", conversions.Morale), Real("ToStun", conversions.Stun),
            Real("ToTile", conversions.Tile), Real("ToTime", conversions.Time), Real("ToWound", conversions.Wound));
        return rule with
        {
            FixedRadius = Integer("FixRadius", rule.FixedRadius),
            RandomType = Integer("RandomType", rule.RandomType),
            ResistType = Integer("ResistType", rule.ResistType),
            ArmorIgnore = Integer("ArmorIgnore", rule.ArmorIgnore),
            ArmorEffectiveness = Real("ArmorEffectiveness", rule.ArmorEffectiveness),
            Conversions = conversions,
            FireBlastCalc = Flag("FireBlastCalc", rule.FireBlastCalc),
            IgnoreDirection = Flag("IgnoreDirection", rule.IgnoreDirection),
            IgnoreSelfDestruct = Flag("IgnoreSelfDestruct", rule.IgnoreSelfDestruct),
            IgnorePainImmunity = Flag("IgnorePainImmunity", rule.IgnorePainImmunity),
            IgnoreNormalMoraleLose = Flag("IgnoreNormalMoraleLose", rule.IgnoreNormalMoraleLose),
            IgnoreOverKill = Flag("IgnoreOverKill", rule.IgnoreOverKill),
            RadiusEffectiveness = Real("RadiusEffectiveness", rule.RadiusEffectiveness),
            RadiusReduction = Real("RadiusReduction", rule.RadiusReduction),
            FireThreshold = Integer("FireThreshold", rule.FireThreshold),
            SmokeThreshold = Integer("SmokeThreshold", rule.SmokeThreshold),
            RandomHealth = Flag("RandomHealth", rule.RandomHealth),
            RandomMana = Flag("RandomMana", rule.RandomMana),
            RandomArmor = Flag("RandomArmor", rule.RandomArmor),
            RandomArmorPre = Flag("RandomArmorPre", rule.RandomArmorPre),
            RandomWound = Flag("RandomWound", rule.RandomWound),
            RandomWoundType = Integer("RandomWoundType", rule.RandomWoundType),
            RandomItem = Flag("RandomItem", rule.RandomItem),
            RandomTile = Flag("RandomTile", rule.RandomTile),
            RandomStun = Flag("RandomStun", rule.RandomStun),
            RandomEnergy = Flag("RandomEnergy", rule.RandomEnergy),
            RandomTime = Flag("RandomTime", rule.RandomTime),
            RandomMorale = Flag("RandomMorale", rule.RandomMorale),
            TileDamageMethod = Integer("TileDamageMethod", rule.TileDamageMethod),
            TileDamageLimit = Integer("TileDamageLimit", rule.TileDamageLimit),
        };

        float Real(string key, float fallback) => data.Reals.TryGetValue(key, out var value) ? (float)value : fallback;
        int Integer(string key, int fallback) => data.Integers.GetValueOrDefault(key, fallback);
        bool Flag(string key, bool fallback) => data.Booleans.GetValueOrDefault(key, fallback);
    }
}
