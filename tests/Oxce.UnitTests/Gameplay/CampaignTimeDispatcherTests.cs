using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignTimeDispatcherTests
{
    [Fact]
    public void PauseFinishesCurrentFallthroughAndDoesNotAdvanceAnotherTick()
    {
        var effects = new RecordingEffects { PauseAt = CampaignTimeTrigger.OneMonth };
        var result = CampaignTimeDispatcher.Advance(new(1, 31, 1, 1999, 23, 59, 55), 20, effects);
        Assert.Equal<int>([5, 4, 3, 2, 1, 0], effects.Trace);
        Assert.Equal(1, result.Summary.TickCount);
        Assert.Equal(1, result.Summary.OneMonth);
        Assert.Equal(0, result.Summary.OneDay);
        Assert.True(result.Paused);
        Assert.Null(result.BlockedReason);
    }

    [Fact]
    public void MissingCapabilityStopsBeforeBoundaryWithNoPartialMutation()
    {
        var effects = new RecordingEffects { BlockAt = CampaignTimeTrigger.OneHour };
        var start = new CampaignTime(1, 1, 1, 1999, 0, 59, 50);
        var result = CampaignTimeDispatcher.Advance(start, 10, effects);
        Assert.Equal<int>([0], effects.Trace);
        Assert.Equal(start with { Second = 55 }, result.Current);
        Assert.Equal(1, result.Summary.TickCount);
        Assert.Equal("unsupported", result.BlockedReason);
        Assert.False(result.Paused);
    }

    [Fact]
    public void RepeatedSmallAdvancesHaveSameOrderedEffectsAsLargeAdvance()
    {
        var start = new CampaignTime(1, 31, 1, 1999, 23, 59, 45);
        var batchEffects = new RecordingEffects();
        var smallEffects = new RecordingEffects();
        var batch = CampaignTimeDispatcher.Advance(start, 40, batchEffects);
        var small = start;
        for (var i = 0; i < 40; i++) small = CampaignTimeDispatcher.Advance(small, 1, smallEffects).Current;
        Assert.Equal(batch.Current, small);
        Assert.Equal(batchEffects.Trace, smallEffects.Trace);
    }

    [Fact]
    public void EmptyMillionTickWorkloadDoesNotAllocatePerTick()
    {
        var start = new CampaignTime(1, 1, 1, 1999, 0, 0, 0);
        var effects = new EmptyEffects();
        CampaignTimeDispatcher.Advance(start, 1, effects);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = CampaignTimeDispatcher.Advance(start, CampaignState.MaximumCommandTicks, effects);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(CampaignState.MaximumCommandTicks, result.Summary.TickCount);
        Assert.InRange(allocated, 0, 16_384);
    }

    [Fact]
    public void AStagePointRechecksTheRestOfTheTickAfterItsLevel()
    {
        // ADR 0035: the month stage point rechecks day to five seconds after the month handlers.
        var effects = new StagingEffects { Stages = 1 << (int)CampaignTimeTrigger.OneMonth | 1 << (int)CampaignTimeTrigger.ThirtyMinutes };
        var result = CampaignTimeDispatcher.Advance(new(1, 31, 1, 1999, 23, 59, 55), 1, effects);
        Assert.Equal(["preflight 5", "stages 5 36", "apply 5", "restage 4", "apply 4", "apply 3", "apply 2", "restage 1",
            "apply 1", "apply 0"], effects.Trace);
        Assert.Equal(1, result.Summary.TickCount);
    }

    [Fact]
    public void AShadowRefusalStopsBeforeTheTickAndStagesAboveItsHighestLevelAreIgnored()
    {
        var start = new CampaignTime(1, 1, 1, 1999, 0, 59, 55);
        var refused = new StagingEffects { Stages = 1 << (int)CampaignTimeTrigger.OneHour, ShadowRefusal = "later stage" };
        var result = CampaignTimeDispatcher.Advance(start, 3, refused);
        Assert.Equal(["preflight 3", "stages 3 8"], refused.Trace);
        Assert.Equal(start, result.Current);
        Assert.Equal(0, result.Summary.TickCount);
        Assert.Equal("later stage", result.BlockedReason);

        // A day stage on an hour tick, and the five-second level, never stage.
        var ignored = new StagingEffects { Stages = 1 << (int)CampaignTimeTrigger.OneDay | 1 };
        CampaignTimeDispatcher.Advance(start, 1, ignored);
        Assert.Equal(["preflight 3", "apply 3", "apply 2", "apply 1", "apply 0"], ignored.Trace);
    }

    private sealed class StagingEffects : ICampaignTimeEffects
    {
        public List<string> Trace { get; } = [];
        public int Stages { get; init; }
        public string? ShadowRefusal { get; init; }
        public string? Preflight(CampaignTime next, CampaignTimeTrigger highestTrigger)
        {
            Trace.Add($"preflight {(int)highestTrigger}");
            return null;
        }
        public bool Apply(CampaignTime current, CampaignTimeTrigger trigger)
        {
            Trace.Add($"apply {(int)trigger}");
            return false;
        }
        public int StagePoints(CampaignTime nextTime, CampaignTimeTrigger highestTrigger) => Stages;
        public string? PreflightStages(CampaignTime nextTime, CampaignTimeTrigger highestTrigger, int stagePoints)
        {
            Trace.Add($"stages {(int)highestTrigger} {stagePoints}");
            return ShadowRefusal;
        }
        public void Restage(CampaignTime current, CampaignTimeTrigger remainingHighestTrigger) =>
            Trace.Add($"restage {(int)remainingHighestTrigger}");
    }

    private sealed class RecordingEffects : ICampaignTimeEffects
    {
        public List<int> Trace { get; } = [];
        public CampaignTimeTrigger? PauseAt { get; init; }
        public CampaignTimeTrigger? BlockAt { get; init; }
        public string? Preflight(CampaignTime next, CampaignTimeTrigger highestTrigger) =>
            highestTrigger == BlockAt ? "unsupported" : null;
        public bool Apply(CampaignTime current, CampaignTimeTrigger trigger)
        {
            Trace.Add((int)trigger);
            return trigger == PauseAt;
        }
    }

    private sealed class EmptyEffects : ICampaignTimeEffects
    {
        public string? Preflight(CampaignTime next, CampaignTimeTrigger highestTrigger) => null;
        public bool Apply(CampaignTime current, CampaignTimeTrigger trigger) => false;
    }
}
