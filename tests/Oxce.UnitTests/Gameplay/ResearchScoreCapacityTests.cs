using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class ResearchScoreCapacityTests
{
    [Theory]
    [InlineData(int.MaxValue, 1, false)]
    [InlineData(int.MinValue, -1, false)]
    [InlineData(int.MinValue, int.MaxValue, true)]
    [InlineData(int.MaxValue, int.MinValue, true)]
    public void SignedHeadroomUsesWiderArithmetic(int score, int points, bool expected)
    {
        var capacity = new ResearchScoreCapacity(score);
        Assert.Equal(expected, capacity.Reserve(points, points));
    }

    [Fact]
    public void OppositeSignsCannotHideAnIntermediateOverflow()
    {
        var capacity = new ResearchScoreCapacity(int.MaxValue - 7);
        Assert.True(capacity.Reserve(-7, -7));
        Assert.True(capacity.Reserve(7, 7));
        Assert.False(capacity.Reserve(1, 1));
    }

    [Fact]
    public void GuaranteedPrefixesAllowOnlyTheirFinalHeadroomAndFailuresDoNotMutateIt()
    {
        var positive = new ResearchScoreCapacity(int.MaxValue - 3);
        Assert.True(positive.AdvanceGuaranteed(-2, 3, -2, -1));
        Assert.True(positive.Reserve(1, 1, 4));
        Assert.False(positive.Reserve(1, 1));
        var negative = new ResearchScoreCapacity(int.MinValue + 3);
        Assert.True(negative.AdvanceGuaranteed(-3, 2, 1, 2));
        Assert.True(negative.Reserve(-1, -1, 4));
        Assert.False(negative.Reserve(-1, -1));
        var refused = new ResearchScoreCapacity(int.MaxValue - 2);
        Assert.False(refused.AdvanceGuaranteed(-2, 3, -2, -1));
        Assert.True(refused.Reserve(2, 2));
    }

    [Fact]
    public void OptionalCompletionsCannotCreditTheGuaranteedDailyBaseline()
    {
        var capacity = new ResearchScoreCapacity(int.MaxValue - 3);
        Assert.True(capacity.AdvanceGuaranteed(-2, 3, -2, -1));
        Assert.True(capacity.Reserve(-1, -1, 4));
        Assert.False(capacity.Reserve(1, 1, 6));
        Assert.Throws<ArgumentException>(() => capacity.AdvanceGuaranteed(1, 2, 1, 2));
        Assert.Throws<ArgumentException>(() => capacity.AdvanceGuaranteed(-2, -1, -2, -1));
        Assert.Throws<ArgumentException>(() => capacity.AdvanceGuaranteed(-2, 3, 2, 1));
        Assert.Throws<ArgumentException>(() => capacity.AdvanceGuaranteed(-2, 3, -3, 1));
        Assert.Throws<ArgumentException>(() => capacity.AdvanceGuaranteed(-2, 3, -2, 4));
        Assert.Throws<ArgumentException>(() => capacity.AdvanceGuaranteed(long.MinValue, long.MaxValue, long.MaxValue, long.MinValue));
    }

    [Fact]
    public void LargeCountsAndMalformedBoundsAreHandledIntentionally()
    {
        var capacity = new ResearchScoreCapacity(0);
        Assert.True(capacity.Reserve(long.MinValue, long.MaxValue, 0));
        Assert.False(capacity.Reserve(long.MinValue, long.MaxValue, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => capacity.Reserve(0, 0, -1));
        Assert.Throws<ArgumentException>(() => capacity.Reserve(1, 0));
    }
}
