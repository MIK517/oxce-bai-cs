using System.Numerics;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Conservative signed intervals for native scalar arithmetic; uncertain wrap widens.</summary>
internal static class WorldDetectionScalarRanges
{
    internal static readonly (int Min, int Max) Full = (int.MinValue, int.MaxValue);

    internal static (int Min, int Max) Apply(CoreScriptOperation operation,
        (int Min, int Max) a, (int Min, int Max) b, (int Min, int Max) c, (int Min, int Max)? d = null) => operation switch
        {
            CoreScriptOperation.Add => Add(a, b),
            CoreScriptOperation.Subtract => Wrap((long)a.Min - b.Max, (long)a.Max - b.Min),
            CoreScriptOperation.Multiply => Multiply(a, b),
            CoreScriptOperation.Aggregate => Add(a, Multiply(b, c)),
            CoreScriptOperation.Offset => Add(Multiply(a, b), c),
            CoreScriptOperation.Divide => Divide((a.Min, a.Max), b),
            CoreScriptOperation.Modulo => Modulo(a, b),
            CoreScriptOperation.MultiplyDivide => Divide(Product(a, b), c),
            CoreScriptOperation.OffsetModulo => OffsetModulo(a, b, c, d ?? Full),
            CoreScriptOperation.Limit => Maximum(Minimum(a, c), b),
            CoreScriptOperation.LimitUpper => Minimum(a, b),
            CoreScriptOperation.LimitLower => Maximum(a, b),
            CoreScriptOperation.Absolute => a == (int.MinValue, int.MinValue) ? a : a.Min == int.MinValue ? Full :
                (a.Min <= 0 && a.Max >= 0 ? 0 : Math.Min(Math.Abs(a.Min), Math.Abs(a.Max)),
                 Math.Max(Math.Abs(a.Min), Math.Abs(a.Max))),
            CoreScriptOperation.BitNot => (~a.Max, ~a.Min),
            CoreScriptOperation.BitAnd or CoreScriptOperation.BitOr or CoreScriptOperation.BitXor => Bitwise(operation, a, b),
            CoreScriptOperation.ShiftLeft or CoreScriptOperation.ShiftRight => Shift(operation, a, b),
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
        var product = Product(a, b);
        return Wrap(product.Min, product.Max);
    }

    private static (long Min, long Max) Product((int Min, int Max) a, (int Min, int Max) b)
    {
        var first = (long)a.Min * b.Min;
        var second = (long)a.Min * b.Max;
        var third = (long)a.Max * b.Min;
        var fourth = (long)a.Max * b.Max;
        return (Math.Min(Math.Min(first, second), Math.Min(third, fourth)),
            Math.Max(Math.Max(first, second), Math.Max(third, fourth)));
    }

    private static (int Min, int Max) Divide((long Min, long Max) a, (int Min, int Max) b)
    {
        if (b.Min <= 0 && b.Max >= 0) return Full;
        var first = a.Min / b.Min;
        var second = a.Min / b.Max;
        var third = a.Max / b.Min;
        var fourth = a.Max / b.Max;
        return Wrap(Math.Min(Math.Min(first, second), Math.Min(third, fourth)),
            Math.Max(Math.Max(first, second), Math.Max(third, fourth)));
    }

    private static (int Min, int Max) Modulo((int Min, int Max) a, (int Min, int Max) b)
    {
        if (b.Min <= 0 && b.Max >= 0) return Full;
        if (a.Min == a.Max && b.Min == b.Max)
        {
            var value = (int)((long)a.Min % b.Min);
            return (value, value);
        }
        var maximum = Math.Max(Math.Abs((long)b.Min), Math.Abs((long)b.Max)) - 1;
        return (a.Min < 0 ? (int)Math.Max(a.Min, -maximum) : 0,
            a.Max > 0 ? (int)Math.Min(a.Max, maximum) : 0);
    }

    private static (int Min, int Max) OffsetModulo((int Min, int Max) a, (int Min, int Max) b,
        (int Min, int Max) c, (int Min, int Max) d)
    {
        if (d.Min <= 0 && d.Max >= 0) return Full;
        if (a.Min == a.Max && b.Min == b.Max && c.Min == c.Max && d.Min == d.Max)
        {
            var numerator = (long)a.Min * b.Min + c.Min;
            var value = (int)((numerator % d.Min + d.Min) % d.Min);
            return (value, value);
        }
        return d.Min > 0 ? (0, d.Max - 1) : (d.Min + 1, 0);
    }

    private static (int Min, int Max) Shift(CoreScriptOperation operation, (int Min, int Max) a, (int Min, int Max) b)
    {
        if (b.Min != b.Max || b.Min is < 0 or > 31) return Full;
        return operation == CoreScriptOperation.ShiftRight ? (a.Min >> b.Min, a.Max >> b.Min)
            : Wrap((long)a.Min << b.Min, (long)a.Max << b.Min);
    }

    private static (int Min, int Max) Bitwise(CoreScriptOperation operation, (int Min, int Max) a, (int Min, int Max) b)
    {
        if (a.Min == a.Max && b.Min == b.Max)
        {
            var value = operation switch
            {
                CoreScriptOperation.BitAnd => a.Min & b.Min,
                CoreScriptOperation.BitOr => a.Min | b.Min,
                _ => a.Min ^ b.Min,
            };
            return (value, value);
        }
        if (operation == CoreScriptOperation.BitAnd)
            return a == (-1, -1) ? b : b == (-1, -1) ? a :
                a.Min >= 0 ? (0, b.Min >= 0 ? Math.Min(a.Max, b.Max) : a.Max) :
                b.Min >= 0 ? (0, b.Max) : Full;
        if (operation == CoreScriptOperation.BitOr && (a == (-1, -1) || b == (-1, -1))) return (-1, -1);
        if (a == (0, 0)) return b;
        if (b == (0, 0)) return a;
        if (operation == CoreScriptOperation.BitXor)
        {
            if (a == (-1, -1)) return (~b.Max, ~b.Min);
            if (b == (-1, -1)) return (~a.Max, ~a.Min);
        }
        if (a.Min < 0 || b.Min < 0) return Full;
        var highest = (uint)(a.Max | b.Max);
        var maximum = highest == 0 ? 0 : (int)((1L << (32 - BitOperations.LeadingZeroCount(highest))) - 1);
        return (operation == CoreScriptOperation.BitOr ? Math.Max(a.Min, b.Min) : 0, maximum);
    }

    private static (int Min, int Max) Wrap(long min, long max) => min == max
        ? (unchecked((int)min), unchecked((int)min))
        : min < int.MinValue || max > int.MaxValue ? Full : ((int)min, (int)max);
}
