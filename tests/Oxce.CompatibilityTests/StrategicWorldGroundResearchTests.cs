using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldGroundResearchTests
{
    [Theory]
    [InlineData(8, "UFO_SCOUT", true)]
    [InlineData(25, "UFO_SCOUT", true)]
    [InlineData(25, "UFO_SPAWN", false)]
    [InlineData(50, "UFO_SCOUT", false)]
    public void SelectedInterruptionPreservesLandWaterTimersAlertsAndFallthrough(double longitude,
        string ruleId, bool landingAllowed)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = StrategicWorldAirborneResearchTests.Content(landing: true);
        var interruptedContent = StrategicWorldAirborneResearchTests.Content("CORE", landing: true);
        var continuedContent = StrategicWorldAirborneResearchTests.Content("", landing: true);
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldAirborneResearchTests.Snapshot(content, seed).WithFacility("RADAR_TEST")
                .WithNextId("STR_LANDING_SITE", 40);
            var first = initial.World.Ufos[0] with
            {
                RuleId = ruleId,
                Longitude = Degrees(longitude),
                Detected = true,
                Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(longitude), Degrees(2))),
            };
            initial = initial with
            {
                Options = initial.Options with { UfoLandingAlert = true },
                Bases = [initial.Bases[0] with { Longitude = first.Longitude, Latitude = first.Latitude }],
                World = initial.World with { Ufos = [first, initial.World.Ufos[1]] },
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var interrupted = Assert.Single(after.World.Missions).Interrupted;
            Assert.Equal(after.CompletedResearch.Contains("BONUS"), interrupted);
            var arrived = after.World.Ufos[0];
            Assert.Equal(interrupted ? UfoStatus.Destroyed : UfoStatus.Landed, arrived.Status);
            Assert.Equal(interrupted ? 0 : landingAllowed ? 60 : 5, arrived.SecondsRemaining);
            Assert.Equal(!interrupted && landingAllowed ? 40 : 0, arrived.LandId);
            Assert.Equal(!interrupted && landingAllowed, result.Events.OfType<UfoLanded>().Any());
            Assert.Equal(interrupted, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
            Assert.Equal(interrupted, after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude ==
                initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude);
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent,
                new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
            for (var pass = 0; pass < 3; pass++)
                campaign = Reload(content, campaign, seed, "ground-research-arrival.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(interrupted ? 1 : 2, campaign.Capture().World.Ufos.Count);
            if (!interrupted)
                Assert.Equal(landingAllowed ? UfoStatus.Landed : UfoStatus.Flying, campaign.Capture().World.Ufos[0].Status);
            Reload(content, campaign, seed, "ground-research-followup.sav");
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }
}
