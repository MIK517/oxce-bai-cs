using Oxce.Engine.Input;
using Oxce.Rendering;

namespace Oxce.Engine;

/// <summary>An input to replay once the scripted run reaches its time.</summary>
public readonly record struct ScriptedInput(TimeSpan At, GameInputEvent Input);

/// <summary>
/// Drives another client with timed inputs and ends the run after a duration, for unattended
/// smoke runs of a real window host.
/// </summary>
public sealed class ScriptedLoopClient : IIndexedLoopClient
{
    private readonly IIndexedLoopClient _inner;
    private readonly ScriptedInput[] _script;
    private readonly TimeSpan _duration;
    private int _next;

    public ScriptedLoopClient(IIndexedLoopClient inner, IEnumerable<ScriptedInput> script, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        _inner = inner;
        _script = [.. script.OrderBy(static input => input.At)];
        _duration = duration;
    }

    public IndexedSurface Frame => _inner.Frame;
    public IndexedPalette Palette => _inner.Palette;
    public long PresentationRevision => _inner.PresentationRevision;
    public bool ExitRequested => _inner.ExitRequested || Elapsed >= _duration;

    /// <summary>Simulated time so far.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Scripted inputs replayed so far.</summary>
    public int Replayed => _next;

    public void HandleInput(in GameInputEvent input) => _inner.HandleInput(input);

    public void Tick(TimeSpan fixedInterval)
    {
        Elapsed += fixedInterval;
        while (_next < _script.Length && _script[_next].At <= Elapsed)
            _inner.HandleInput(_script[_next++].Input);
        _inner.Tick(fixedInterval);
    }
}
