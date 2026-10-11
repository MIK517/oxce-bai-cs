using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornGroundStartTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void OverlappingGroundStartsNeverMoveOrRechargeOnTheirBirthTick(int timer)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var lift = Assert.Single(TestFixtures.Rows(oracle.RootElement, "normalLiftRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 0);
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false,
            firstAltitude: 0, groundTimer: timer, points: 7);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(3, after.World.Ufos.Count);
            Assert.Equal(18 + (timer == 1 ? 3 * lift[6].GetInt32() : 0), after.Regions[0].ActivityAlien[^1]);
            var expectedRandom = new SplitMix64RandomSource(seed);
            for (var draw = 0; draw < 23; draw++) expectedRandom.NextUnit();
            Assert.Equal(expectedRandom.State, after.RandomState);
            Assert.All(after.World.Ufos, ufo =>
            {
                Assert.Equal(0, ufo.TrajectoryPoint);
                Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), ufo.Position);
                Assert.Equal(ufo.Position, ufo.Destination!.Position);
                Assert.Equal(timer == 1 ? UfoStatus.Flying : UfoStatus.Landed, ufo.Status);
                Assert.Equal(timer == 1 ? WorldAltitudes.VeryLow : WorldAltitudes.Ground, ufo.Altitude);
                Assert.Equal((timer - 1) * 5, ufo.SecondsRemaining);
                Assert.Equal(-1, ufo.Shield);
                Assert.Equal(0, ufo.LandId);
            });
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-ground-start.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, timer);
            Assert.All(campaign.Capture().World.Ufos, ufo =>
            { Assert.Equal(1, ufo.TrajectoryPoint); Assert.Equal(10, ufo.Shield); });
        }
    }

    [Fact]
    public void GroundBirthDetectionAllocatesMarkersBeforeTakeoffAndPreservesThem()
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false,
            firstAltitude: 0, groundTimer: 1, points: 7, alerts: true);
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17).WithFacility("RADAR_HYPER_TEST");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(3, result.Events.OfType<UfoContactDetected>().Count());
        var after = campaign.Capture();
        Assert.All(after.World.Ufos, ufo =>
        {
            Assert.True(ufo.Detected);
            Assert.True(ufo.Id > 0);
            Assert.True(ufo.LandId > 0);
            Assert.Equal(UfoStatus.Flying, ufo.Status);
            Assert.Equal(0, ufo.TrajectoryPoint);
        });
        campaign = Reload(content, campaign, 17, "newborn-ground-markers.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(0, ufo.LandId));
        initial = initial.WithNextId("STR_LANDING_SITE", int.MaxValue - 2);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO landing marker IDs are exhausted or collide with a saved UFO.");
    }

    [Fact]
    public void GroundBirthScoreAndFirstTickLiftReserveOneCombinedEnvelope()
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false, firstAltitude: 0, groundTimer: 1, points: 7);
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17);
        initial = initial with { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 38] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO alien activity exceeds the supported range.");
        initial = initial with { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 39] }] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RegionalGroundStartsComposeWithBothGenerationCallers(int generation)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = StrategicWorldNewbornRegionalArrivalTests.Content(false, generation, 0, 1);
        var initial = StrategicWorldNewbornRegionalArrivalTests.Snapshot(content, 17, generation);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(generation == 3 ? 6 : 3, after.World.Ufos.Count);
        Assert.All(after.World.Ufos, ufo =>
        { Assert.Equal(UfoStatus.Flying, ufo.Status); Assert.Equal(0, ufo.TrajectoryPoint); Assert.Equal(-1, ufo.Shield); });
        Assert.Equal(generation is 1 or 3 ? 18 : 0, after.Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 17, "generated-ground-start.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(1, ufo.TrajectoryPoint));
    }
}
