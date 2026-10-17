using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSharedFlightTests
{
    /// <summary>
    /// ADR 0033: the movement, site-conversion and half-hour steps read one stored flight projection
    /// per UFO, and every test recomputes each stored read. This pins that the store is used, so the
    /// recomputation cannot pass vacuously.
    /// </summary>
    [Fact]
    public void FlyingUfoTicksReadAndVerifyTheSharedProjection()
    {
        Assert.True(CampaignWorld.VerifySharedFlights);
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 5));
        var before = CampaignWorld.VerifiedSharedFlightReads;

        AdvanceUnblocked(campaign, 10);

        // Per tick: the site-conversion step (arrival, landing base, flight end) and the half-hour
        // arrival check reuse the projection stored by the movement step.
        Assert.True(CampaignWorld.VerifiedSharedFlightReads - before >= 10 * 4);
    }
}
