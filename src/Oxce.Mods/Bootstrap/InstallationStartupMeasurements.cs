using System.Diagnostics;
using Oxce.Mods.Rulesets;

namespace Oxce.Mods.Bootstrap;

public enum InstallationStartupStage
{
    DiscoveryAndPlanning,
    CacheKey,
    CacheRead,
    ResourceRestoration,
    RuntimeRuleLinking,
    RuntimePublication,
    FreshBuild,
    CacheWrite,
}

public readonly record struct InstallationStartupStageMeasurement(
    InstallationStartupStage Stage,
    ContentBuildStageMeasurement Measurement);

/// <summary>Non-overlapping wall-clock and calling-thread allocation samples for one synchronous load.</summary>
public sealed record InstallationStartupMeasurements(
    ContentBuildStageMeasurement Total,
    IReadOnlyList<InstallationStartupStageMeasurement> Stages)
{
    public static InstallationStartupMeasurements Empty { get; } = new(ContentBuildStageMeasurement.Empty, []);
}

internal sealed class StartupMeasurementCollector
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly long _allocated = GC.GetAllocatedBytesForCurrentThread();
    private readonly List<InstallationStartupStageMeasurement> _stages = [];

    public IDisposable Measure(InstallationStartupStage stage) => new Scope(this, stage);

    public InstallationStartupMeasurements Snapshot() => new(
        new ContentBuildStageMeasurement(Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - _allocated),
        Array.AsReadOnly(_stages.ToArray()));

    private sealed class Scope(StartupMeasurementCollector owner, InstallationStartupStage stage) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly long _allocated = GC.GetAllocatedBytesForCurrentThread();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._stages.Add(new InstallationStartupStageMeasurement(stage,
                new ContentBuildStageMeasurement(Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                    GC.GetAllocatedBytesForCurrentThread() - _allocated)));
        }
    }
}
