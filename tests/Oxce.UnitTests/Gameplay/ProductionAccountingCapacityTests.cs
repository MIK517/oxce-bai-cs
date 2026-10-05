using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class ProductionAccountingCapacityTests
{
    [Theory]
    [InlineData(long.MaxValue, 1, false)]
    [InlineData(long.MinValue, -1, false)]
    [InlineData(long.MinValue, long.MaxValue, true)]
    [InlineData(long.MaxValue, -long.MaxValue, true)]
    [InlineData(long.MaxValue, long.MinValue, false)]
    public void SignedFundHeadroomDoesNotOverflow(long funds, long delta, bool expected)
    {
        var capacity = new ProductionAccountingCapacity(funds, 0, 0);
        Assert.Equal(expected, capacity.Reserve(delta, delta, 1));
    }

    [Fact]
    public void OppositeSignsStillConsumeSeparateLedgerCapacity()
    {
        var capacity = new ProductionAccountingCapacity(0, long.MaxValue - 7, long.MaxValue - 7);
        Assert.True(capacity.Reserve(7, 7, 1));
        Assert.True(capacity.Reserve(-7, -7, 1));
        Assert.False(capacity.Reserve(1, 1, 1));
        var spending = new ProductionAccountingCapacity(0, 0, long.MaxValue);
        Assert.False(spending.Reserve(-1, -1, 1));
    }

    [Fact]
    public void LargeCountsAndUncertainScriptPricesCannotWrapTheBudget()
    {
        var capacity = new ProductionAccountingCapacity(0, 0, 0);
        Assert.False(capacity.Reserve(-long.MaxValue, long.MaxValue, int.MaxValue));
        var zero = new ProductionAccountingCapacity(long.MaxValue, long.MaxValue, long.MaxValue);
        Assert.True(zero.Reserve(long.MinValue, long.MaxValue, 0));
        var uncertain = new ProductionAccountingCapacity(0, 0, 0);
        Assert.True(uncertain.Reserve(int.MinValue, int.MaxValue, 1));
    }

    [Fact]
    public void InvalidBoundsAndCountsFailIntentionally()
    {
        var capacity = new ProductionAccountingCapacity(0, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => capacity.Reserve(0, 0, -1));
        Assert.Throws<ArgumentException>(() => capacity.Reserve(1, 0, 1));
    }
}
