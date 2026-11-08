using Oxce.Core.Random;

namespace Oxce.TestSupport;

/// <summary>
/// A random source whose inclusive draws return the scripted numbers in order (kept inside the asked range; zero once the script runs out),
/// recording each call as (minimum, maximum, result). Combat tests use it to force a hit, a miss or a damage roll.
/// </summary>
internal sealed class ScriptedDraws(params int[] results) : IRandomSource
{
    private int _next;

    public List<(int Minimum, int Maximum, int Result)> Calls { get; } = [];

    public int NextInclusive(int minimum, int maximum)
    {
        var scripted = _next < results.Length ? results[_next] : 0;
        _next++;
        var result = Math.Clamp(scripted, minimum, maximum);
        Calls.Add((minimum, maximum, result));
        return result;
    }

    public int NextExclusive(int exclusiveMaximum) => NextInclusive(0, exclusiveMaximum - 1);

    public double NextUnit() => throw new InvalidOperationException("The scripted draws are integers.");
}
