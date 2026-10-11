using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldEventTimingTests
{
    [Theory]
    [InlineData(0, -1)]
    [InlineData(int.MaxValue, 1)]
    public void InvalidTimersRejectBeforeRandomness(int timer, int randomMaximum)
    {
        var random = new SplitMix64RandomSource(17);
        Assert.False(WorldEventTiming.IsValid(timer, randomMaximum));
        Assert.Throws<InvalidDataException>(() => WorldEventTiming.SpawnCountdown(timer, randomMaximum, random));
        Assert.Equal(17UL, random.State);
    }

    [Fact]
    public void FinishedEventDoesNotAdvanceAndBoundaryKeepsLastCountdown()
    {
        var scheduled = new GeoscapeEventSnapshot("EVENT", 31);
        scheduled = WorldEventTiming.Think(scheduled);
        Assert.Equal(1, scheduled.SpawnCountdown);
        Assert.False(scheduled.Over);
        scheduled = WorldEventTiming.Think(scheduled);
        Assert.True(scheduled.Over);
        Assert.Equal(1, scheduled.SpawnCountdown);
        Assert.Same(scheduled, WorldEventTiming.Think(scheduled));
    }
}
