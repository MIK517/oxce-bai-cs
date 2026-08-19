namespace Oxce.Platform.Sdl;

public sealed record SdlWindowOptions(string Title)
{
    public int Scale { get; init; } = 1;

    public int TargetFrameRate { get; init; } = 60;

    public TimeSpan SimulationStep { get; init; } = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    public int MaximumCatchUpSteps { get; init; } = 8;

    public bool Resizable { get; init; } = true;

    public bool ExitOnEscape { get; init; } = true;

    public bool EnableTextInput { get; init; }

    public TimeSpan? MaximumRunTime { get; init; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Scale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TargetFrameRate);
        if (TargetFrameRate > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetFrameRate));
        }

        if (SimulationStep <= TimeSpan.Zero || SimulationStep > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(SimulationStep));
        }

        if (MaximumCatchUpSteps is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCatchUpSteps));
        }

        if (MaximumRunTime is { } maximumRunTime)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumRunTime, TimeSpan.Zero, nameof(MaximumRunTime));
        }
    }
}
