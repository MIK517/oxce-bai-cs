using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Conservative signed intervals for native scalar arithmetic; uncertain wrap widens.</summary>
internal static class WorldDetectionScalarRanges
{
    internal static readonly (int Min, int Max) Full = (int.MinValue, int.MaxValue);

    internal static (int Min, int Max) Apply(CoreScriptOperation operation,
        (int Min, int Max) a, (int Min, int Max) b, (int Min, int Max) c) => operation switch
    {
        CoreScriptOperation.Add => Add(a, b),
        CoreScriptOperation.Subtract => Wrap((long)a.Min - b.Max, (long)a.Max - b.Min),
        CoreScriptOperation.Multiply => Multiply(a, b),
        CoreScriptOperation.Aggregate => Add(a, Multiply(b, c)),
        CoreScriptOperation.Offset => Add(Multiply(a, b), c),
        CoreScriptOperation.Limit => Maximum(Minimum(a, c), b),
        CoreScriptOperation.LimitUpper => Minimum(a, b),
        CoreScriptOperation.LimitLower => Maximum(a, b),
        CoreScriptOperation.Absolute => a == (int.MinValue, int.MinValue) ? a : a.Min == int.MinValue ? Full :
            (a.Min <= 0 && a.Max >= 0 ? 0 : Math.Min(Math.Abs(a.Min), Math.Abs(a.Max)),
             Math.Max(Math.Abs(a.Min), Math.Abs(a.Max))),
        CoreScriptOperation.BitCount => (0, 32),
        CoreScriptOperation.SquareRoot => ((int)Math.Sqrt(Math.Max(0, a.Min)), (int)Math.Sqrt(Math.Max(0, a.Max))),
        _ => Full,
    };

    private static (int Min, int Max) Add((int Min, int Max) a, (int Min, int Max) b) =>
        Wrap((long)a.Min + b.Min, (long)a.Max + b.Max);

    private static (int Min, int Max) Minimum((int Min, int Max) a, (int Min, int Max) b) =>
        (Math.Min(a.Min, b.Min), Math.Min(a.Max, b.Max));

    private static (int Min, int Max) Maximum((int Min, int Max) a, (int Min, int Max) b) =>
        (Math.Max(a.Min, b.Min), Math.Max(a.Max, b.Max));

    private static (int Min, int Max) Multiply((int Min, int Max) a, (int Min, int Max) b)
    {
        var first = (long)a.Min * b.Min;
        var second = (long)a.Min * b.Max;
        var third = (long)a.Max * b.Min;
        var fourth = (long)a.Max * b.Max;
        return Wrap(Math.Min(Math.Min(first, second), Math.Min(third, fourth)),
            Math.Max(Math.Max(first, second), Math.Max(third, fourth)));
    }

    private static (int Min, int Max) Wrap(long min, long max) => min == max
        ? (unchecked((int)min), unchecked((int)min))
        : min < int.MinValue || max > int.MaxValue ? Full : ((int)min, (int)max);
}
