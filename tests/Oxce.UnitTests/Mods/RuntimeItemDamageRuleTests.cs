using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

/// <summary>
/// Phase 7 branch 5 S1: <c>RuleDamageType</c> (<c>src/Mod/RuleDamageType.cpp</c> and the base types of <c>Mod::Mod</c>): <c>getRandomDamage</c> for every
/// random type, the <c>get*FinalDamage</c> conversions with their random forms, the wounds and the tile damage.
/// </summary>
public sealed class RuntimeItemDamageRuleTests
{
    private static readonly CampaignInterceptionSettings Ranges = CampaignInterceptionSettings.Default;

    private static RuntimeItemDamageRule Plain(int randomType, int resistType = 1) =>
        RuntimeItemDamageRule.Predefined(resistType) with { RandomType = randomType };

    private static (int Low, int High) Span(RuntimeItemDamageRule type, int power, CampaignInterceptionSettings? ranges = null) =>
        (type.RandomDamage(power, ranges ?? Ranges, 1), type.RandomDamage(power, ranges ?? Ranges, 2));

    [Theory]
    [InlineData(RuntimeItemDamageRule.RandomUfo, 0, 120)]
    [InlineData(RuntimeItemDamageRule.RandomTftd, 30, 90)]
    [InlineData(RuntimeItemDamageRule.RandomFlat, 60, 60)]
    [InlineData(RuntimeItemDamageRule.RandomFire, 5, 10)]
    [InlineData(RuntimeItemDamageRule.RandomNone, 0, 0)]
    [InlineData(RuntimeItemDamageRule.RandomUfoWithTwoDice, 0, 120)]
    [InlineData(RuntimeItemDamageRule.RandomEasy, 30, 120)]
    [InlineData(RuntimeItemDamageRule.RandomStandard, 0, 120)]
    [InlineData(RuntimeItemDamageRule.RandomExplosion, 30, 90)]
    [InlineData(RuntimeItemDamageRule.RandomUfoWithFourDice, 0, 120)]
    [InlineData(11, 0, 0)]
    public void EachRandomTypeRollsItsRangeOfThePower(int randomType, int low, int high) =>
        Assert.Equal((low, high), Span(Plain(randomType), 60));

    [Fact]
    public void TheDefaultRandomTypeFollowsTheResistType()
    {
        // DT_NONE and DT_SMOKE roll nothing, DT_IN rolls the fire range, DT_HE the explosion range, anything else the standard one.
        Assert.Equal((0, 0), Span(Plain(RuntimeItemDamageRule.RandomDefault, 0), 60));
        Assert.Equal((0, 0), Span(Plain(RuntimeItemDamageRule.RandomDefault, 9), 60));
        Assert.Equal((5, 10), Span(Plain(RuntimeItemDamageRule.RandomDefault, 2), 60));
        Assert.Equal((30, 90), Span(Plain(RuntimeItemDamageRule.RandomDefault, 3), 60));
        Assert.Equal((0, 120), Span(Plain(RuntimeItemDamageRule.RandomDefault, 1), 60));
        Assert.Equal((0, 120), Span(Plain(RuntimeItemDamageRule.RandomDefault, 19), 60));
    }

    [Fact]
    public void TheModsRangesChangeTheStandardExplosiveAndFireRolls()
    {
        var mod = CampaignInterceptionSettings.Default with
        {
            DamageRange = 25,
            ExplosiveDamageRange = 10,
            FireDamageRange = Array.AsReadOnly([1, 2]),
        };
        Assert.Equal((45, 75), Span(Plain(RuntimeItemDamageRule.RandomStandard), 60, mod));
        Assert.Equal((54, 66), Span(Plain(RuntimeItemDamageRule.RandomExplosion), 60, mod));
        Assert.Equal((1, 2), Span(Plain(RuntimeItemDamageRule.RandomFire), 60, mod));
    }

    [Fact]
    public void ARollDrawsOncePerDieAndTheFireTypeIgnoresThePower()
    {
        var draws = new ScriptedDraws(10, 20, 30, 40);
        Assert.Equal(50, Plain(RuntimeItemDamageRule.RandomUfoWithFourDice).RandomDamage(60, Ranges, draws));
        Assert.Equal([(0, 60, 10), (0, 60, 20), (0, 60, 30), (0, 60, 40)], draws.Calls);
        var two = new ScriptedDraws(10, 20);
        Assert.Equal(30, Plain(RuntimeItemDamageRule.RandomUfoWithTwoDice).RandomDamage(60, Ranges, two));
        var fire = new ScriptedDraws(7);
        Assert.Equal(7, Plain(RuntimeItemDamageRule.RandomFire).RandomDamage(1000, Ranges, fire));
        Assert.Equal([(5, 10, 7)], fire.Calls);
        var none = new ScriptedDraws(7);
        Assert.Equal(0, Plain(RuntimeItemDamageRule.RandomNone).RandomDamage(60, Ranges, none));
        Assert.Empty(none.Calls);
        // Mode 0 is a roll; a power so low that the range runs backwards (undefined in the reference) gives its lower bound.
        Assert.Equal(15, Plain(RuntimeItemDamageRule.RandomEasy).RandomDamage(20, Ranges, 0, new ScriptedDraws(15)));
        Assert.Equal(0, Plain(RuntimeItemDamageRule.RandomStandard).RandomDamage(-10, Ranges, new ScriptedDraws(3)));
    }

    [Fact]
    public void TheBaseTypesAreTheOnesOfTheModConstructor()
    {
        var none = RuntimeItemDamageRule.Predefined(0);
        Assert.Equal((0, RuntimeItemDamageRule.RandomNone, 0, false), (none.FixedRadius, none.RandomType, none.ResistType, none.IgnoreOverKill));
        var ap = RuntimeItemDamageRule.Predefined(1);
        Assert.Equal((0, RuntimeItemDamageRule.RandomStandard, 1, true, false, 1f), (ap.FixedRadius, ap.RandomType, ap.ResistType, ap.IgnoreOverKill,
            ap.IgnoreSelfDestruct, ap.ArmorEffectiveness));

        var incendiary = RuntimeItemDamageRule.Predefined(2);
        Assert.Equal((-1, RuntimeItemDamageRule.RandomFire, true, true, true, true), (incendiary.FixedRadius, incendiary.RandomType, incendiary.FireBlastCalc,
            incendiary.IgnoreDirection, incendiary.IgnoreSelfDestruct, incendiary.IgnoreOverKill));
        Assert.Equal((0f, 0.03f, 0, 2), (incendiary.ArmorEffectiveness, incendiary.RadiusEffectiveness, incendiary.FireThreshold, incendiary.TileDamageMethod));
        Assert.Equal(new RuntimeDamageConversions(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0), incendiary.Conversions);

        var explosive = RuntimeItemDamageRule.Predefined(3);
        Assert.Equal((-1, RuntimeItemDamageRule.RandomExplosion, 0.05f, 2, true), (explosive.FixedRadius, explosive.RandomType, explosive.RadiusEffectiveness,
            explosive.TileDamageMethod, explosive.IgnoreSelfDestruct));
        Assert.Equal(1f, explosive.Conversions.Item);

        Assert.True(RuntimeItemDamageRule.Predefined(4).IgnoreOverKill && RuntimeItemDamageRule.Predefined(5).IgnoreOverKill);

        var stun = RuntimeItemDamageRule.Predefined(6);
        Assert.Equal((-1, true, true, false), (stun.FixedRadius, stun.IgnoreSelfDestruct, stun.IgnorePainImmunity, stun.RandomStun));
        Assert.Equal(new RuntimeDamageConversions(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), stun.Conversions);

        var melee = RuntimeItemDamageRule.Predefined(7);
        Assert.Equal((0, true, true), (melee.FixedRadius, melee.IgnoreOverKill, melee.IgnoreSelfDestruct));
        Assert.True(RuntimeItemDamageRule.Predefined(8).IgnoreOverKill);

        var smoke = RuntimeItemDamageRule.Predefined(9);
        Assert.Equal((-1, RuntimeItemDamageRule.RandomNone, true, 0f, 0), (smoke.FixedRadius, smoke.RandomType, smoke.IgnoreDirection, smoke.ArmorEffectiveness,
            smoke.SmokeThreshold));
        Assert.Equal(1f, smoke.Conversions.Stun);

        for (var type = 10; type < 20; type++)
        {
            var spare = RuntimeItemDamageRule.Predefined(type);
            Assert.Equal((0, RuntimeItemDamageRule.RandomStandard, type, true), (spare.FixedRadius, spare.RandomType, spare.ResistType, spare.IgnoreOverKill));
        }
        Assert.Throws<InvalidDataException>(() => RuntimeItemDamageRule.Predefined(20));
        Assert.Throws<InvalidDataException>(() => RuntimeItemDamageRule.Predefined(-1));
        Assert.True(ap.IsDirect);
        Assert.False(explosive.IsDirect);
    }

    [Fact]
    public void TheConversionsScaleTheDamageAndRoundHalvesAwayFromZero()
    {
        var type = RuntimeItemDamageRule.Predefined(1);
        var none = new ScriptedDraws();
        // AP: health 1, armor 0.1, stun 0.25, tile 0.5, wound 1 (no wound draw is made when the damage is not positive).
        Assert.Equal(40, type.HealthFinalDamage(40, none));
        Assert.Equal(4, type.ArmorFinalDamage(40, none));
        Assert.Equal(3, type.ArmorFinalDamage(25, none)); // 2.5 -> 3
        Assert.Equal(10, (type with { RandomStun = false }).StunFinalDamage(40, none)); // AP rolls its stun by default
        Assert.Equal(0, type.StunFinalDamage(0, none));
        Assert.Equal(0, type.ManaFinalDamage(40, none));
        Assert.Equal(0, type.ItemFinalDamage(40, none));
        Assert.Equal(0, type.EnergyFinalDamage(40, none));
        Assert.Equal(0, type.TimeFinalDamage(40, none));
        Assert.Equal(0, type.MoraleFinalDamage(40, none));
        Assert.Equal(0, type.ArmorPreFinalDamage(40, none));
        Assert.Equal(20, type.TileFinalDamage(40, none));
        Assert.Empty(none.Calls);
        Assert.Equal(0, type.HealthFinalDamage(-5, none));
        // A negative multiplier rounds away from zero as well.
        Assert.Equal(-3, (type with { Conversions = type.Conversions with { Health = -0.5f } }).HealthFinalDamage(5, none));
    }

    [Fact]
    public void ARandomConversionRollsUpToTheDamageBeforeTheMultiplier()
    {
        var type = RuntimeItemDamageRule.Predefined(1) with { RandomHealth = true, RandomStun = true, RandomTile = true, RandomItem = true };
        var draws = new ScriptedDraws(30, 7, 20);
        Assert.Equal(30, type.HealthFinalDamage(40, draws));
        Assert.Equal(2, type.StunFinalDamage(40, draws)); // 7 * 0.25 = 1.75
        Assert.Equal(10, type.TileFinalDamage(40, draws));
        Assert.Equal([(0, 40, 30), (0, 40, 7), (0, 40, 20)], draws.Calls);
        // The tile damage is kept to the limit, whatever it rolled.
        Assert.Equal(3, (type with { TileDamageLimit = 3 }).TileFinalDamage(40, new ScriptedDraws(40)));
    }

    [Fact]
    public void WoundsFollowTheVanillaChanceTheSpreadOrAFixedShare()
    {
        var type = RuntimeItemDamageRule.Predefined(1);
        // Vanilla: a draw of 0 to 10 under the potential (damage * ToWound) wounds for one to three.
        var hit = new ScriptedDraws(4, 2);
        Assert.Equal(2, type.WoundFinalDamage(5, hit));
        Assert.Equal([(0, 10, 4), (1, 3, 2)], hit.Calls);
        var miss = new ScriptedDraws(5, 2);
        Assert.Equal(0, type.WoundFinalDamage(5, miss));
        Assert.Single(miss.Calls);
        // Spread: a roll from 0 up to the potential.
        var spread = new ScriptedDraws(3);
        Assert.Equal(3, (type with { RandomWoundType = 1 }).WoundFinalDamage(5, spread));
        Assert.Equal([(0, 5, 3)], spread.Calls);
        // Without random wounds the potential itself, rounded.
        var fixedShare = type with { RandomWound = false, Conversions = type.Conversions with { Wound = 0.5f } };
        Assert.Equal(3, fixedShare.WoundFinalDamage(5, new ScriptedDraws()));
        // A damage of zero or less wounds no one and draws nothing.
        var none = new ScriptedDraws();
        Assert.Equal(0, type.WoundFinalDamage(0, none));
        Assert.Empty(none.Calls);
    }

    [Fact]
    public void TheTileDamageOfMethodOneRollsHalfToOneAndAHalfOfThePower()
    {
        var one = RuntimeItemDamageRule.Predefined(1);
        var draws = new ScriptedDraws(37);
        Assert.Equal(37, one.RandomDamageForTile(50, 12, draws));
        Assert.Equal([(25, 75, 37)], draws.Calls);
        // Method 2 takes the damage rolled for the units.
        Assert.Equal(12, RuntimeItemDamageRule.Predefined(3).RandomDamageForTile(50, 12, new ScriptedDraws()));
    }
}
