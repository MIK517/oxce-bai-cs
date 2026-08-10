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
