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
    public void LargeCountsAndMalformedBoundsAreHandledIntentionally()
    {
        var capacity = new ResearchScoreCapacity(0);
        Assert.True(capacity.Reserve(long.MinValue, long.MaxValue, 0));
        Assert.False(capacity.Reserve(long.MinValue, long.MaxValue, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => capacity.Reserve(0, 0, -1));
        Assert.Throws<ArgumentException>(() => capacity.Reserve(1, 0));
    }
}
