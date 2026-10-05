using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class ProductionProgressTests
{
    [Theory]
    [InlineData(-1, 0, 0, 1)]
    [InlineData(1, -1, 0, 1)]
    [InlineData(1, 0, -1, 1)]
    [InlineData(1, 0, 0, -1)]
    public void NegativeProgressIsRejected(int time, int spent, int assigned, int amount) =>
        Assert.Throws<InvalidDataException>(() => ProductionProgress.Requested(time, spent, assigned, amount, false));

    [Fact]
    public void ProgressOverflowIsRejected() =>
        Assert.Throws<OverflowException>(() => ProductionProgress.Requested(10, int.MaxValue, 1, 1, false));

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void FiniteProductionCapsItsCompletionCount(bool infinite, int expected) =>
        Assert.Equal(expected, ProductionProgress.Requested(4, 0, 12, 1, infinite));
}
