namespace Oxce.Core.Random;

/// <summary>
/// Supplies gameplay randomness. Exact parity with the C++ generator is not a product
/// requirement, but injected sources make simulations reproducible and testable.
/// </summary>
public interface IRandomSource
{
    int NextExclusive(int exclusiveMaximum);

    int NextInclusive(int minimum, int maximum);

    double NextUnit();
}

/// <summary>
/// A reproducible source whose state can cross the save-neutral gameplay boundary.
/// The state is an implementation detail and is not expected to match OXCE's RNG stream.
/// </summary>
public interface IStatefulRandomSource : IRandomSource
{
    ulong State { get; }

    void Restore(ulong state);
}

/// <summary>
/// Small deterministic generator for campaign simulation and headless scenarios.
/// </summary>
public sealed class SplitMix64RandomSource : IStatefulRandomSource
{
    public SplitMix64RandomSource(ulong seed) => State = seed;

    public ulong State { get; private set; }

    public int NextExclusive(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    public int NextInclusive(int minimum, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, maximum);
        var range = (ulong)((long)maximum - minimum) + 1;
        return checked((int)(minimum + (long)(NextUInt64() % range)));
    }

    public double NextUnit() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public void Restore(ulong state) => State = state;

    private ulong NextUInt64()
    {
        State += 0x9E3779B97F4A7C15UL;
        var value = State;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
