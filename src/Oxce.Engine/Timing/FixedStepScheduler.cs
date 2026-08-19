namespace Oxce.Engine.Timing;

public readonly record struct FixedStepAdvanceResult(int ExecutedSteps, TimeSpan DroppedTime);

public sealed class FixedStepScheduler
{
    private long _accumulatedTicks;
    private long _previousTimestampTicks;
    private bool _hasTimestamp;

    public FixedStepScheduler(TimeSpan step, int maximumCatchUpSteps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCatchUpSteps);
        Step = step;
        MaximumCatchUpSteps = maximumCatchUpSteps;
    }

    public TimeSpan Step { get; }

    public int MaximumCatchUpSteps { get; }

    public TimeSpan AccumulatedTime => TimeSpan.FromTicks(_accumulatedTicks);

    public void Reset(TimeSpan timestamp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timestamp, TimeSpan.Zero);
        _previousTimestampTicks = timestamp.Ticks;
        _accumulatedTicks = 0;
        _hasTimestamp = true;
    }

    public FixedStepAdvanceResult AdvanceTo(TimeSpan timestamp, Action<TimeSpan> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (!_hasTimestamp)
        {
            throw new InvalidOperationException("The fixed-step scheduler must be reset before advancing by timestamp.");
        }

        if (timestamp.Ticks < _previousTimestampTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "A monotonic timestamp cannot move backwards.");
        }

        var elapsedTicks = timestamp.Ticks - _previousTimestampTicks;
        _previousTimestampTicks = timestamp.Ticks;
        return AdvanceBy(TimeSpan.FromTicks(elapsedTicks), tick);
    }

    public FixedStepAdvanceResult AdvanceBy(TimeSpan elapsed, Action<TimeSpan> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        _accumulatedTicks = checked(_accumulatedTicks + elapsed.Ticks);

        var availableSteps = _accumulatedTicks / Step.Ticks;
        var executedSteps = (int)Math.Min(availableSteps, MaximumCatchUpSteps);
        var droppedSteps = availableSteps - executedSteps;
        var droppedTicks = checked(droppedSteps * Step.Ticks);
        _accumulatedTicks -= droppedTicks;

        for (var index = 0; index < executedSteps; index++)
        {
            _accumulatedTicks -= Step.Ticks;
            tick(Step);
        }

        return new FixedStepAdvanceResult(executedSteps, TimeSpan.FromTicks(droppedTicks));
    }
}
