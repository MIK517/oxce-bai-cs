using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchCounterCapacityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExactOrderedBranchesFitWhereAUnionOfPossibleIncreasesDoesNot(bool cache, bool withSite) =>
        StrategicWorldDailyResearchCounterAlternativeTests.AssertNativeCounterAlternatives(cache, 1, withSite);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsafeIntermediateInAnyCounterBranchRemainsAtomic(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityTests.Content(cache, false, extraResearch:
            "  - {name: EQUIV_A, increaseCounter: [TRACE_COUNTER]}\n  - {name: EQUIV_B, increaseCounter: [TRACE_COUNTER, TRACE_COUNTER], decreaseCounter: [TRACE_COUNTER]}");
        var initial = StrategicWorldDailyResearchCounterAlternativeTests.Snapshot(content, 17, int.MaxValue - 1);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Research custom counters exceed the supported range.");
        AssertTimeBlocked(campaign, "Research custom counters exceed the supported range.");
    }
}
