using Oxce.Core.Diagnostics;
using Oxce.FixtureSupport;
using Oxce.Mods;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Items;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

/// <summary>
/// Phase 7 branch 5 S1: <c>RuleStatBonus</c> (<c>src/Mod/RuleStatBonus.cpp</c>): the defaults of <c>RuleItem</c> and <c>Armor</c>, the
/// load of a coefficient mapping or script code through an item's keys, and the evaluation of the script the reference generates (the polynomial
/// of each statistic over 1000, truncated after every term, then rounded to the nearest and divided by 1000).
/// </summary>
public sealed class RuleStatBonusTests
{
    private sealed class FakeUnit : IStatBonusUnit
    {
        public Dictionary<StatBonusBaseStat, int> Stats { get; } = [];

        public int BaseStat(StatBonusBaseStat stat) => Stats.GetValueOrDefault(stat);
        public int TimeUnits { get; init; }
        public int Health { get; init; }
        public int Mana { get; init; }
        public int Energy { get; init; }
        public int Morale { get; init; } = 100;
        public int StunLevel { get; init; }
        public int FatalWoundCount { get; init; }
        public int RankInt { get; init; }
        public int RankIntUnified { get; init; }
        public int EnergyRegeneration { get; init; }
    }

    private static FakeUnit Unit(int firing = 0, int strength = 0, int melee = 0, int reactions = 0, int psiSkill = 0, int psiStrength = 0) =>
        new()
        {
            Stats =
            {
                [StatBonusBaseStat.Firing] = firing, [StatBonusBaseStat.Strength] = strength, [StatBonusBaseStat.Melee] = melee,
                [StatBonusBaseStat.Reactions] = reactions, [StatBonusBaseStat.PsiSkill] = psiSkill, [StatBonusBaseStat.PsiStrength] = psiStrength,
            },
        };

    /// <summary>The stat bonuses of an item written with the given body (one indented key per line, at four spaces).</summary>
    private static ItemStatBonusRules Item(string body)
    {
        using var fixture = new TemporaryModFixture(("fixture.rul", "items:\n  - type: T\n" + body));
        return Assert.Single(ItemRuleCatalog.Load(TestFixtures.CreatePlan(fixture.Root)).Items.Rules).Value.StatBonuses;
    }

    /// <summary>The damage bonus of an item whose <c>damageBonus</c> key is the given YAML.</summary>
    private static RuleStatBonus Damage(string value) => Item("    damageBonus: " + value + "\n").Damage;

    private static IEnumerable<(string Stat, float Coefficient)> Terms(RuleStatBonus bonus) =>
        bonus.Terms.Select(static term => (term.Stat, term.Coefficients.Single()));

    [Fact]
    public void TheDefaultsAreTheReferencesSetters()
    {
        Assert.Equal([("firing", 1f)], Terms(RuleStatBonus.Firing));
        Assert.Equal([("melee", 1f)], Terms(RuleStatBonus.MeleeSkill));
        Assert.Equal([("throwing", 1f)], Terms(RuleStatBonus.Throwing));
        Assert.Equal([("melee", 0.5f), ("reactions", 0.5f)], Terms(RuleStatBonus.CloseQuarters));
        Assert.Equal([("psi", 0.02f)], Terms(RuleStatBonus.PsiAttack));
        Assert.Equal([("psiStrength", 1f), ("psiSkill", 0.2f)], Terms(RuleStatBonus.PsiDefense));
        Assert.Equal([("flatHundred", 1f)], Terms(RuleStatBonus.FlatHundred));
        Assert.Equal([("strength", 1f)], Terms(RuleStatBonus.Strength));
        Assert.Empty(RuleStatBonus.Empty.Terms);
        Assert.All([RuleStatBonus.Firing, RuleStatBonus.Empty, RuleStatBonus.PsiDefense], static bonus => Assert.False(bonus.Modded || bonus.IsScript));
    }

    [Fact]
    public void AnItemStartsWithTheDefaultsOfRuleItem()
    {
        var bonuses = Item("    battleType: 1\n");
        Assert.Empty(bonuses.Damage.Terms);
        Assert.Empty(bonuses.Melee.Terms);
        Assert.Equal([("firing", 1f)], Terms(bonuses.Accuracy));
        Assert.Equal([("melee", 1f)], Terms(bonuses.MeleeMultiplier));
        Assert.Equal([("throwing", 1f)], Terms(bonuses.Throw));
        Assert.Equal([("melee", 0.5f), ("reactions", 0.5f)], Terms(bonuses.CloseQuarters));
        Assert.False(bonuses.AnyScript);
        // A psi amp aims with psi (setPsiAttack runs when battleType is read).
        Assert.Equal([("psi", 0.02f)], Terms(Item("    battleType: 9\n").Accuracy));
    }

    [Fact]
    public void ADefaultBonusIsThePolynomialOfTheStatisticRoundedToTheNearest()
    {
        // setFiring: 1.0 * firing, so a firing of 60 is 60; a flat hundred is 100 whoever holds the weapon; setCloseQuarters sums two halves.
        Assert.Equal(60, RuleStatBonus.Firing.Evaluate(Unit(firing: 60)));
        Assert.Equal(100, RuleStatBonus.FlatHundred.Evaluate(Unit()));
        Assert.Equal(0, RuleStatBonus.FlatHundred.Evaluate(null)); // no unit, no terms
        // 0.5 * 40 + 0.5 * 33 = 36.5, each term a whole number of thousandths, rounded half up.
        Assert.Equal(37, RuleStatBonus.CloseQuarters.Evaluate(Unit(melee: 40, reactions: 33)));
        // setPsiAttack: psiSkill * psiStrength * 0.02.
        Assert.Equal(42, RuleStatBonus.PsiAttack.Evaluate(Unit(psiSkill: 70, psiStrength: 30)));
    }

    [Fact]
    public void AMappingListsTermsAndTheRoundingGoesAwayFromZeroAtAHalf()
    {
        // Half a point of 61 firing is 30.5: it rounds up to 31, and the negative bonus down to -31.
        var half = Damage("{firing: 0.5}");
        Assert.Equal(31, half.Evaluate(Unit(firing: 61)));
        Assert.Equal(30, half.Evaluate(Unit(firing: 60)));
        Assert.Equal(-31, Damage("{firing: -0.5}").Evaluate(Unit(firing: 61)));
        // The external bonus (the item's power for a damage bonus) is the start: 40 + 0.5 * 61 = 70.5 -> 71.
        Assert.Equal(71, half.Evaluate(Unit(firing: 61), 40));
        // A bonus with no term is the external bonus unchanged, with or without a unit.
        Assert.Equal(40, Damage("{}").Evaluate(Unit(firing: 61), 40));
        Assert.Equal(40, RuleStatBonus.Empty.Evaluate(null, 40));
        // With a term but no unit (no attacker) the terms add nothing.
        Assert.Equal(40, half.Evaluate(null, 40));
        Assert.True(half.Modded);
    }

    [Fact]
    public void ACoefficientListIsAPolynomialAndEachTermIsTruncatedBeforeTheNext()
    {
        // pow1 * s + pow2 * s^2 + pow3 * s^3 + pow4 * s^4: 1 * 10 + 0.1 * 100 + 0.01 * 1000 + 0.001 * 10000 = 40.
        Assert.Equal(40, Damage("{strength: [1, 0.1, 0.01, 0.001]}").Evaluate(Unit(strength: 10)));
        // Only the first four coefficients count.
        Assert.Equal(10, Damage("{strength: [1, 0, 0, 0, 5]}").Evaluate(Unit(strength: 10)));
        // The terms run in the order of the reference's statistics (flatOne before strength before firing), not the file's.
        Assert.Equal(["flatOne", "strength", "firing"], Damage("{firing: 1, strength: 1, flatOne: 1}").Terms.Select(static term => term.Stat));
        // A term adds a whole number of thousandths: 0.0004 * 10 is 4, below one unit.
        Assert.Equal(0, Damage("{strength: 0.0004}").Evaluate(Unit(strength: 10)));
        // A statistic scaled by 100 is read as a hundredth: strengthScaled 50 -> 0.5 -> 100 * 0.5 = 50.
        Assert.Equal(50, Damage("{strengthScaled: 100}").Evaluate(Unit(strength: 50)));
        Assert.Equal(25, Damage("{strengthMeleeScaled: 100}").Evaluate(Unit(strength: 50, melee: 50)));
    }

    [Fact]
    public void TheCurrentAndNormalizedStatisticsReadTheUnit()
    {
        var unit = new FakeUnit { Health = 30, TimeUnits = 20, Morale = 50, RankInt = 3, EnergyRegeneration = 12, FatalWoundCount = 2, StunLevel = 10 };
        unit.Stats[StatBonusBaseStat.Health] = 60;
        unit.Stats[StatBonusBaseStat.TimeUnits] = 80;
        Assert.Equal(30, Damage("{healthCurrent: 1}").Evaluate(unit));
        Assert.Equal(50, Damage("{healthNormalized: 100}").Evaluate(unit));
        Assert.Equal(25, Damage("{tuNormalized: 100}").Evaluate(unit));
        Assert.Equal(50, Damage("{moraleNormalized: 100}").Evaluate(unit));
        // A normalized value with no maximum is 0 (the unit has no mana).
        Assert.Equal(0, Damage("{manaNormalized: 100}").Evaluate(unit));
        Assert.Equal(3, Damage("{rank: 1}").Evaluate(unit));
        Assert.Equal(12, Damage("{energyRegen: 1}").Evaluate(unit));
        Assert.Equal(2, Damage("{fatalWounds: 1}").Evaluate(unit));
        Assert.Equal(33, Damage("{stunNormalized: 100}").Evaluate(unit));
    }

    [Fact]
    public void UnknownTermsAreDroppedAndAScalarIsCodeThatOnlyTheBindingsRun()
    {
        var mixed = Item("    accuracyMultiplier: {nonsense: 3, firing: 1}\n").Accuracy;
        Assert.Equal(["firing"], mixed.Terms.Select(static term => term.Stat));
        Assert.True(mixed.Modded);
        var code = Item("    accuracyMultiplier: return bonus;\n");
        Assert.True(code.Accuracy.IsScript);
        Assert.True(code.Accuracy.Modded);
        Assert.True(code.AnyScript);
        Assert.Throws<NotSupportedException>(() => code.Accuracy.Evaluate(Unit(firing: 60)));
        // A key that is neither a mapping nor code empties the bonus (RuleStatBonus::load clears the terms), and marks it modded.
        var emptied = Item("    accuracyMultiplier: [1, 2]\n").Accuracy;
        Assert.Empty(emptied.Terms);
        Assert.False(emptied.IsScript);
        Assert.True(emptied.Modded);
        Assert.Equal(7, emptied.Evaluate(Unit(firing: 60), 7));
    }

    [Fact]
    public void StrengthAndSkillAppliedSetTheDefaultsBeforeTheKeysAndSetterReplacesEarlierCode()
    {
        // strengthApplied makes the damage bonus the strength, modded; skillApplied false makes the melee multiplier a flat hundred, modded.
        var applied = Item("    strengthApplied: true\n    skillApplied: false\n");
        Assert.Equal([("strength", 1f)], Terms(applied.Damage));
        Assert.True(applied.Damage.Modded);
        Assert.Equal([("flatHundred", 1f)], Terms(applied.MeleeMultiplier));
        Assert.True(applied.MeleeMultiplier.Modded);
        // skillApplied true is setMelee: not modded.
        Assert.False(Item("    skillApplied: true\n").MeleeMultiplier.Modded);
        // The key of the bonus comes after: it wins over strengthApplied.
        Assert.Equal(["flatOne"], Item("    strengthApplied: true\n    damageBonus: {flatOne: 1}\n").Damage.Terms.Select(static term => term.Stat));
        // An item that inherits code and then becomes a psi amp takes the psi default back (setValues rebuilds the script from the terms).
        var inherited = Item("    refNode: {accuracyMultiplier: 'return bonus;'}\n    battleType: 9\n");
        Assert.False(inherited.Accuracy.IsScript);
        Assert.Equal([("psi", 0.02f)], Terms(inherited.Accuracy));
        // Code written after the default stays.
        Assert.True(Item("    battleType: 9\n    accuracyMultiplier: 'return bonus;'\n").Accuracy.IsScript);
    }

    [Fact]
    public void EachOfTheSixItemKeysLoadsItsOwnBonusAndCodeInAnyIsReported()
    {
        var bonuses = Item("    damageBonus: {flatOne: 1}\n    meleeBonus: {flatOne: 2}\n    accuracyMultiplier: {flatOne: 3}\n    meleeMultiplier: {flatOne: 4}\n" +
            "    throwMultiplier: {flatOne: 5}\n    closeQuartersMultiplier: {flatOne: 6}\n");
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], new[] { bonuses.Damage, bonuses.Melee, bonuses.Accuracy, bonuses.MeleeMultiplier, bonuses.Throw, bonuses.CloseQuarters }
            .Select(static bonus => Assert.Single(bonus.Terms).Coefficients.Single()));
        foreach (var key in new[] { "damageBonus", "meleeBonus", "accuracyMultiplier", "meleeMultiplier", "throwMultiplier", "closeQuartersMultiplier" })
        {
            var coded = Item("    " + key + ": return bonus;\n");
            Assert.True(coded.AnyScript, key);
            Assert.Equal(1, new[] { coded.Damage, coded.Melee, coded.Accuracy, coded.MeleeMultiplier, coded.Throw, coded.CloseQuarters }.Count(static bonus => bonus.IsScript));
        }
    }

    [Fact]
    public void AnArmorsPsiDefenceAndMeleeDodgeStartAsTheReferenceSetsThem()
    {
        using var fixture = new TemporaryModFixture(("fixture.rul", "armors:\n  - type: PLAIN\n  - type: MAPPED\n    psiDefence: {psiStrength: 2}\n    meleeDodge: {flatHundred: 0.5}\n  - type: CODED\n    meleeDodge: 'return bonus;'\n"));
        var catalog = Oxce.Mods.Rulesets.PersonnelTactical.PersonnelTacticalRuleCatalog.Load(TestFixtures.CreatePlan(fixture.Root));
        var armors = catalog.Armors.Rules.ToDictionary(static rule => rule.Id, static rule => rule.Value.StatBonuses);
        Assert.Equal([("psiStrength", 1f), ("psiSkill", 0.2f)], Terms(armors["PLAIN"].PsiDefence));
        Assert.Empty(armors["PLAIN"].MeleeDodge.Terms);
        Assert.Equal([("psiStrength", 2f)], Terms(armors["MAPPED"].PsiDefence));
        Assert.Equal([("flatHundred", 0.5f)], Terms(armors["MAPPED"].MeleeDodge));
        Assert.True(armors["CODED"].MeleeDodge.IsScript);
        Assert.False(armors["CODED"].PsiDefence.IsScript);
    }

    [Fact]
    public void ATermMappingIsLoadedWithoutADeferralAndOnlyCodeIsDeferred()
    {
        // RuleItem::load and Armor::load read these keys; only a scalar (script code) waits for script compilation.
        using var fixture = new TemporaryModFixture(("fixture.rul",
            "items:\n  - type: MAPPED\n    damageBonus: {strength: 1}\n    closeQuartersMultiplier: {flatOne: 1}\n  - type: CODED\n    meleeBonus: 'return bonus;'\n" +
            "armors:\n  - type: MAPPED\n    psiDefence: {psiStrength: 2}\n    meleeDodge: {flatHundred: 0.5}\n  - type: CODED\n    meleeDodge: 'return bonus;'\n"));
        var diagnostics = new DiagnosticCollector();
        ItemRuleCatalog.Load(TestFixtures.CreatePlan(fixture.Root), diagnostics);
        Oxce.Mods.Rulesets.PersonnelTactical.PersonnelTacticalRuleCatalog.Load(TestFixtures.CreatePlan(fixture.Root), diagnostics);
        var deferred = diagnostics.Snapshot().Where(static item => item.Code == ModDiagnosticCodes.DeferredRuleProperty)
            .Select(static item => (item.Context.RuleId, item.Message.Split('\'')[1])).ToList();
        Assert.Equal([("CODED", "meleeBonus"), ("CODED", "meleeDodge")], deferred);
    }
}
