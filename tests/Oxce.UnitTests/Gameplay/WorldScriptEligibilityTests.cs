using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldScriptEligibilityTests
{
    private static readonly RuntimeScriptCriteria Criteria = new(0, -1, 100, 0, 4,
        20, 30, 5000000000L, 6000000000L, "", "", 0, -1, [], [], [], [], [], [], []);
    private static readonly WorldScriptContext Context = new(1, 2, 20, 5000000000L, false,
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
        new Dictionary<string, int>(), new Dictionary<string, int>());

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ScoreAndFundsConstraintsBeginOnlyAfterMonthZero(int month, bool matches)
    {
        Assert.Equal(matches, WorldScriptEligibility.Matches(Criteria with { FirstMonth = -1 },
            Context with { Month = month, Score = int.MinValue, Funds = long.MinValue }));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, -2, false)]
    public void CounterBoundsHaveReferenceSentinels(int minimum, int maximum, bool matches)
    {
        Assert.Equal(matches, WorldScriptEligibility.Matches(Criteria with
        { MissionVariable = "RUNS", CounterMinimum = minimum, CounterMaximum = maximum }, Context));
        // Empty names suppress checks, even for otherwise impossible bounds.
        Assert.True(WorldScriptEligibility.Matches(Criteria with { CounterMinimum = minimum, CounterMaximum = maximum }, Context));
    }

    [Theory]
    [InlineData(-1, false)] // absent key
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void MarkerCounterUsesGetLastIdFloorAndAbsence(int next, bool matches)
    {
        var state = Context with { NextIds = next == -1 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["MARKER"] = next } };
        Assert.Equal(matches, WorldScriptEligibility.Matches(Criteria with
        { MissionMarker = "MARKER", CounterMinimum = 1, CounterMaximum = 1 }, state));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DebugModeMakesEveryResearchTriggerTrue(bool debug, bool matches)
    {
        Assert.Equal(matches, WorldScriptEligibility.Matches(Criteria with
        { Research = [new(new("MISSING", null), true)] }, Context with { DebugResearch = debug }));
    }

    [Fact]
    public void InclusiveMonthDifficultyScoreAndFundsBoundsArePreserved()
    {
        var bounded = Criteria with { FirstMonth = 1, LastMonth = 1, MinimumDifficulty = 2, MaximumDifficulty = 2 };
        Assert.True(WorldScriptEligibility.Matches(bounded, Context));
        Assert.True(WorldScriptEligibility.Matches(bounded, Context with { Score = 30, Funds = 6000000000L }));
        Assert.False(WorldScriptEligibility.Matches(bounded, Context with { Month = 0 }));
        Assert.False(WorldScriptEligibility.Matches(bounded, Context with { Month = 2 }));
        Assert.False(WorldScriptEligibility.Matches(bounded, Context with { Difficulty = 1 }));
        Assert.False(WorldScriptEligibility.Matches(bounded, Context with { Difficulty = 3 }));
        Assert.False(WorldScriptEligibility.Matches(bounded, Context with { Score = 31 }));
        Assert.False(WorldScriptEligibility.Matches(bounded, Context with { Funds = 6000000001L }));
    }
}
