using Xunit;

namespace Oxce.UnitTests;

// Tests that assert exactly zero bytes with GC.GetAllocatedBytesForCurrentThread run alone, after the parallel tests.
// Runtime work driven by other tests in the process (GCs, tier-up) can land a few hundred bytes on the measuring
// thread; running alone removes that noise without loosening the assertion.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurement
{
    public const string Name = "Allocation measurement";
}
