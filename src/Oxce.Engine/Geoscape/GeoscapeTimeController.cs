using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Geoscape;

/// <summary>The outcome of one host tick of the geoscape clock.</summary>
public readonly record struct GeoscapeTimeStep(int Ticks, IReadOnlyList<ICampaignEvent> Events, string? BlockedReason)
{
    public static GeoscapeTimeStep None { get; } = new(0, [], null);
}

/// <summary>
/// GeoscapeState::timeAdvance driven by the host clock. Every <c>geoClockSpeed</c> (80 ms) the
/// selected speed owes 1, 12, 60, 360, 720 or 17,280 five-second ticks; they run as
/// <see cref="AdvanceCampaignTime"/> chunks carrying the selected speed, spread over host frames
/// when a frame's budget runs out. A tick that raises notifications pauses, as a popup does in the
/// reference: the rest of that step is dropped and the clock waits for the next step. A refused
/// tick halts the clock until the player selects a speed again.
/// </summary>
public sealed class GeoscapeTimeController
{
    /// <summary>Options::geoClockSpeed default.</summary>
    public static readonly TimeSpan GeoClock = TimeSpan.FromMilliseconds(80);
    private static readonly int[] TicksPerStep = [1, 12, 12 * 5, 12 * 5 * 6, 12 * 5 * 6 * 2, 12 * 5 * 6 * 2 * 24];
    private readonly Func<TimeSpan> _stopwatch;
    private readonly TimeSpan _frameBudget;
    private TimeSpan _accumulated;
    private int _owed;
    private int _chunk = 720;

    /// <param name="stopwatch">Monotonic elapsed time used to keep each host frame within <paramref name="frameBudget"/>.</param>
    public GeoscapeTimeController(Func<TimeSpan> stopwatch, TimeSpan? frameBudget = null)
    {
        ArgumentNullException.ThrowIfNull(stopwatch);
        _stopwatch = stopwatch;
        _frameBudget = frameBudget ?? TimeSpan.FromMilliseconds(12);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_frameBudget, TimeSpan.Zero);
    }

    public CampaignTimeSpeed Speed { get; private set; } = CampaignTimeSpeed.FiveSeconds;

    /// <summary>False after a refused tick, until a speed is selected again.</summary>
    public bool Running { get; private set; } = true;

    public string? HaltReason { get; private set; }

    /// <summary>Ticks still owed for the current clock step.</summary>
    public int Owed => _owed;

    public static int StepTicks(CampaignTimeSpeed speed) => TicksPerStep[(int)speed];

    /// <summary>A speed button: selects the speed and resumes a halted clock.</summary>
    public void Select(CampaignTimeSpeed speed)
    {
        if (!Enum.IsDefined(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        if (speed != Speed) _owed = 0;
        Speed = speed;
        Running = true;
        HaltReason = null;
    }

    /// <summary>GeoscapeState::timerReset: back to five-second speed; a halted clock stays halted.</summary>
    public void Reset()
    {
        if (Speed != CampaignTimeSpeed.FiveSeconds) _owed = 0;
        Speed = CampaignTimeSpeed.FiveSeconds;
    }

    /// <summary>Stops the clock after a failure outside the guarded transitions.</summary>
    public void Halt(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Running = false;
        HaltReason = reason;
        _owed = 0;
    }

    /// <summary>Stops owing the rest of a step, as the reference does when a popup opens.</summary>
    public void Interrupt()
    {
        _owed = 0;
        _accumulated = TimeSpan.Zero;
    }

    public GeoscapeTimeStep Tick(TimeSpan elapsed, ICampaignCommandTarget commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (!Running) return GeoscapeTimeStep.None;
        if (_owed == 0)
        {
            // A slow host does not bank more than one extra step.
            _accumulated = TimeSpan.FromTicks(Math.Min((_accumulated + elapsed).Ticks, GeoClock.Ticks * 2));
            if (_accumulated < GeoClock) return GeoscapeTimeStep.None;
            _accumulated -= GeoClock;
            _owed = StepTicks(Speed);
        }
        var start = _stopwatch();
        var done = 0;
        while (_owed > 0)
        {
            var requested = Math.Min(_owed, _chunk);
            var chunkStart = _stopwatch();
            var result = commands.Execute(new AdvanceCampaignTime(requested) { SelectedSpeed = Speed });
            var advanced = result.Events.OfType<CampaignTimeAdvanced>().FirstOrDefault();
            var completed = advanced?.Summary.TickCount ?? 0;
            done += completed;
            _owed -= completed;
            var notifications = result.Events.Where(static e => e is not CampaignTimeAdvanced).ToArray();
            if (notifications.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
            {
                Running = false;
                HaltReason = blocked.Reason;
                _owed = 0;
                return new(done, notifications, blocked.Reason);
            }
            if (notifications.Length != 0 || completed < requested)
            {
                Interrupt();
                return new(done, notifications, null);
            }
            Adapt(_stopwatch() - chunkStart, requested);
            if (_stopwatch() - start >= _frameBudget) break;
        }
        return new(done, [], null);
    }

    /// <summary>Keeps one chunk near a quarter of the frame budget.</summary>
    private void Adapt(TimeSpan spent, int ticks)
    {
        var target = _frameBudget.Ticks / 4;
        if (spent.Ticks > target && _chunk > 12) _chunk = Math.Max(12, _chunk / 2);
        else if (spent.Ticks < target / 2 && ticks == _chunk && _chunk < CampaignState.MaximumCommandTicks / 2)
            _chunk *= 2;
    }
}
