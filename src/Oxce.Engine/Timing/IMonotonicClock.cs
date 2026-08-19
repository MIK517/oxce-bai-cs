using System.Diagnostics;

namespace Oxce.Engine.Timing;

public interface IMonotonicClock
{
    TimeSpan Elapsed { get; }
}

public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
}
