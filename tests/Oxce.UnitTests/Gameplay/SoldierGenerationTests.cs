using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class SoldierGenerationTests
{
    [Fact]
    public void FixedStatAndNameFixtureMatchesReferenceConstructorRules()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var rule = Assert.Single(content.RuntimeRules.Soldiers.Rules).Value;
        var pool = Assert.Single(rule.NamePools);
        Assert.Equal(100, pool.GlobalWeight);
        Assert.Equal(pool.MaleFirst, pool.FemaleFirst);
        var generated = SoldierGeneration.Generate(rule, "ARMOR", -1,
            new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(42));
        Assert.Equal("Alex Example", generated.Name);
        Assert.Equal("Comet", generated.Callsign);
        Assert.Equal(1, generated.Gender);
        Assert.Equal(2, generated.Look);
        Assert.InRange(generated.LookVariant, 0, 63);
        Assert.Equal(20, generated.InitialStats["bravery"]); // Integer division precedes the random draw.
        Assert.Equal(0, generated.InitialStats["psiSkill"]); // Always starts at the minimum.
        Assert.Equal(20, generated.InitialStats["mana"]);
        Assert.Equal(generated.InitialStats, generated.CurrentStats);
        Assert.Equal("ARMOR", generated.Armor);
    }

    [Fact]
    public void DuplicateNameRetriesExactlyTenConstructors()
    {
        var rule = Assert.Single(CampaignLogisticsTests.LoadFixture().RuntimeRules.Soldiers.Rules).Value;
        var first = new CountingRandom();
        SoldierGeneration.Generate(rule, "ARMOR", -1, new HashSet<string>(StringComparer.Ordinal), first);
        var duplicate = new CountingRandom();
        var generated = SoldierGeneration.Generate(rule, "ARMOR", -1,
            new HashSet<string>(StringComparer.Ordinal) { "Alex Example" }, duplicate);
        Assert.Equal("Alex Example", generated.Name);
        Assert.Equal(first.Calls * 10, duplicate.Calls);
    }

    [Fact]
    public void RegeneratingWithoutNamePoolsKeepsExistingIdentity()
    {
        var rule = Assert.Single(CampaignLogisticsTests.LoadFixture().RuntimeRules.Soldiers.Rules).Value;
        var personal = SoldierGeneration.Generate(rule, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(7));
        var random = new CountingRandom();
        var regenerated = SoldierGeneration.RegenerateName(personal with { Nationality = 5 }, rule with { NamePools = [] }, random);
        Assert.Equal(personal with { Nationality = 0 }, regenerated);
        Assert.Equal(0, random.Calls);
    }

    private sealed class CountingRandom : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextExclusive(int exclusiveMaximum) { Calls++; return 0; }
        public int NextInclusive(int minimum, int maximum) { Calls++; return minimum; }
        public double NextUnit() { Calls++; return 0; }
    }
}
