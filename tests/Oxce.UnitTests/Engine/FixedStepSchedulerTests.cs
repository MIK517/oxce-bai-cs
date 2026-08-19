using Oxce.Engine.Timing;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class FixedStepSchedulerTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void ElapsedTimeAccumulatesIntoExactFixedSteps()
    {
        var scheduler = new FixedStepScheduler(Step, maximumCatchUpSteps: 8);
        var ticks = new List<TimeSpan>();

        var first = scheduler.AdvanceBy(TimeSpan.FromMilliseconds(7), ticks.Add);
        var second = scheduler.AdvanceBy(TimeSpan.FromMilliseconds(25), ticks.Add);

        Assert.Equal(0, first.ExecutedSteps);
        Assert.Equal(3, second.ExecutedSteps);
        Assert.Equal([Step, Step, Step], ticks);
        Assert.Equal(TimeSpan.FromMilliseconds(2), scheduler.AccumulatedTime);
    }

    [Fact]
    public void CatchUpLimitDropsWholeBacklogStepsButRetainsRemainder()
    {
        var scheduler = new FixedStepScheduler(Step, maximumCatchUpSteps: 3);
        var tickCount = 0;

        var result = scheduler.AdvanceBy(
            TimeSpan.FromMilliseconds(57),
            _ => tickCount++);

        Assert.Equal(3, result.ExecutedSteps);
        Assert.Equal(TimeSpan.FromMilliseconds(20), result.DroppedTime);
        Assert.Equal(3, tickCount);
        Assert.Equal(TimeSpan.FromMilliseconds(7), scheduler.AccumulatedTime);
    }

    [Fact]
    public void TimestampAdvancementRequiresResetAndMonotonicValues()
    {
        var scheduler = new FixedStepScheduler(Step, maximumCatchUpSteps: 8);

        Assert.Throws<InvalidOperationException>(() => scheduler.AdvanceTo(Step, _ => { }));

        scheduler.Reset(TimeSpan.FromMilliseconds(100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.AdvanceTo(TimeSpan.FromMilliseconds(99), _ => { }));
    }

    [Fact]
    public void ExplicitElapsedAdvancementSupportsDeterministicHeadlessExecution()
    {
        var scheduler = new FixedStepScheduler(Step, maximumCatchUpSteps: 8);
        var simulated = TimeSpan.Zero;

        scheduler.AdvanceBy(Step * 4, step => simulated += step);

        Assert.Equal(TimeSpan.FromMilliseconds(40), simulated);
    }
}
