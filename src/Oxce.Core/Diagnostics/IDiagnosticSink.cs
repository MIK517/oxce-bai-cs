namespace Oxce.Core.Diagnostics;

public interface IDiagnosticSink
{
    void Report(DiagnosticEvent diagnostic);
}

public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();

    private NullDiagnosticSink()
    {
    }

    public void Report(DiagnosticEvent diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
    }
}

public sealed class DiagnosticCollector : IDiagnosticSink
{
    public const int DefaultMaximumDiagnostics = 10_000;

    private readonly object _sync = new();
    private readonly List<DiagnosticEvent> _diagnostics = [];
    private readonly int _maximumDiagnostics;
    private int _droppedCount;
    private int _reportedCount;
    private int _highestSeverity = -1;

    public DiagnosticCollector(int maximumDiagnostics = DefaultMaximumDiagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDiagnostics);
        _maximumDiagnostics = maximumDiagnostics;
    }

    public int DroppedCount
    {
        get
        {
            lock (_sync)
            {
                return _droppedCount;
            }
        }
    }

    public int ReportedCount
    {
        get
        {
            lock (_sync)
            {
                return _reportedCount;
            }
        }
    }

    public DiagnosticSeverity? HighestSeverity
    {
        get
        {
            lock (_sync)
            {
                return _highestSeverity < 0 ? null : (DiagnosticSeverity)_highestSeverity;
            }
        }
    }

    public bool HasSeverityAtLeast(DiagnosticSeverity severity)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        lock (_sync)
        {
            return _highestSeverity >= (int)severity;
        }
    }

    public void Report(DiagnosticEvent diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (_sync)
        {
            _reportedCount = checked(_reportedCount + 1);
            _highestSeverity = Math.Max(_highestSeverity, (int)diagnostic.Severity);
            if (_diagnostics.Count < _maximumDiagnostics)
            {
                _diagnostics.Add(diagnostic);
            }
            else
            {
                _droppedCount = checked(_droppedCount + 1);
            }
        }
    }

    public IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (_sync)
        {
            return _diagnostics.ToArray();
        }
    }
}
