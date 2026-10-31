using Oxce.Gameplay.Campaigns;

namespace Oxce.Gameplay.Sessions;

/// <summary>The outcome of one host tick of the geoscape clock.</summary>
public readonly record struct SessionClockStep(int Ticks, IReadOnlyList<ICampaignEvent> Events, string? BlockedReason)
{
    public static SessionClockStep None { get; } = new(0, [], null);
}

/// <summary>
/// GeoscapeState::timeAdvance driven by the host clock. Every <c>geoClockSpeed</c> (80 ms) the
/// selected speed owes 1, 12, 60, 360, 720 or 17,280 five-second ticks; they run as
/// <see cref="AdvanceCampaignTime"/> chunks carrying the selected speed, spread over host frames
/// when a frame's budget runs out. A tick that raises notifications pauses, as a popup does in the
/// reference: the rest of that step is dropped and the clock waits for the next step. A refused
/// tick (a battle exists, for one) halts the clock until the player selects a speed again.
/// The clock is host state (ADR 0037 rule 5): clients only report that they cover the geoscape
/// (<see cref="SetCovered"/>), request a speed and interrupt a step; the clock does not run while any attached
/// client reports a cover, so a popup, a destination choice or a screen above the geoscape pauses everyone.
/// </summary>
public sealed class SessionClock
{
    /// <summary>Options::geoClockSpeed default.</summary>
    public static readonly TimeSpan GeoClock = TimeSpan.FromMilliseconds(80);
    private static readonly int[] TicksPerStep = [1, 12, 12 * 5, 12 * 5 * 6, 12 * 5 * 6 * 2, 12 * 5 * 6 * 2 * 24];
    private readonly ICampaignCommandTarget _commands;
    private readonly Func<TimeSpan> _stopwatch;
    private readonly TimeSpan _frameBudget;
    private TimeSpan _accumulated;
    private int _owed;
    private int _chunk = 720;
    private readonly HashSet<PlayerId> _covering = [];

    /// <param name="commands">Where the clock's time advances go; the session passes its own broadcasting path.</param>
    /// <param name="stopwatch">Monotonic elapsed time used to keep each host frame within <paramref name="frameBudget"/>.</param>
    public SessionClock(ICampaignCommandTarget commands, Func<TimeSpan> stopwatch, TimeSpan? frameBudget = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(stopwatch);
        _commands = commands;
        _stopwatch = stopwatch;
        _frameBudget = frameBudget ?? TimeSpan.FromMilliseconds(12);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_frameBudget, TimeSpan.Zero);
    }

    /// <summary>True while any client reports a cover (a popup, a screen above the geoscape, the world not yet entered).</summary>
    public bool Covered => _covering.Count != 0;

    /// <summary>A client reports whether it covers the geoscape; only the host clock decides what that pauses.</summary>
    public void SetCovered(PlayerId player, bool covered)
    {
        if (covered) _covering.Add(player);
        else _covering.Remove(player);
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

    /// <summary>
    /// A manual advance (a client's "advance time" key) under the same host policy as the clock: refused while a
    /// client covers the geoscape or the clock is halted, and a refused advance halts the clock.
    /// </summary>
    public CampaignCommandResult Advance(int ticks)
    {
        if (Covered) return new([CampaignActionBlocked.From(SessionReasons.Covered())]);
        if (!Running) return new([CampaignActionBlocked.From(SessionReasons.Halted(HaltReason))]);
        var result = _commands.Execute(new AdvanceCampaignTime(ticks));
        if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked) Halt(blocked.Reason);
        return result;
    }

    public SessionClockStep Tick(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (!Running || Covered) return SessionClockStep.None;
        if (_owed == 0)
        {
            // A slow host does not bank more than one extra step.
            _accumulated = TimeSpan.FromTicks(Math.Min((_accumulated + elapsed).Ticks, GeoClock.Ticks * 2));
            if (_accumulated < GeoClock) return SessionClockStep.None;
            _accumulated -= GeoClock;
            _owed = StepTicks(Speed);
        }
        var start = _stopwatch();
        var done = 0;
        while (_owed > 0)
        {
            var requested = Math.Min(_owed, _chunk);
            var chunkStart = _stopwatch();
            var result = _commands.Execute(new AdvanceCampaignTime(requested) { SelectedSpeed = Speed });
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
