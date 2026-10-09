using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornGroundArrivalTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    public void FirstGroundArrivalStartsItsTimerAfterTheFlyingPass(bool cache, int timer)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var landed = Assert.Single(TestFixtures.Rows(oracle.RootElement, "transitionRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 0 && row[2].GetInt32() == 0 && row[3].GetInt32() == 1);
        var lift = Assert.Single(TestFixtures.Rows(oracle.RootElement, "normalLiftRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 0);
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, groundTimer: timer,
            points: 7, arrivalAltitude: 0);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(3, after.World.Ufos.Count);
            var expectedRandom = new SplitMix64RandomSource(seed);
            // Three point births, two zero timers, one base detection per UFO,
            // then both replacement waypoint selections on each arrival.
            for (var draw = 0; draw < 3 * (6 + 1 + landed[10].GetInt32()) + 2; draw++) expectedRandom.NextUnit();
            Assert.Equal(expectedRandom.State, after.RandomState);
            Assert.Equal(9, after.Regions[0].ActivityAlien[^1]);
            Assert.All(after.World.Ufos, ufo =>
            {
                Assert.Equal(1, ufo.TrajectoryPoint);
                Assert.Equal(UfoStatus.Landed, ufo.Status);
                Assert.Equal(landed[5].GetString(), ufo.Altitude);
                Assert.Equal(landed[8].GetInt32(), ufo.Speed);
                Assert.Equal(timer * 5, ufo.SecondsRemaining);
                Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), ufo.Position);
                Assert.Equal(ufo.Position, ufo.Destination!.Position);
                Assert.Equal(0, ufo.LandId);
                Assert.Equal(10, ufo.Shield);
            });
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-first-ground.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, timer);
            Assert.Equal(9 + 3 * lift[6].GetInt32(), campaign.Capture().Regions[0].ActivityAlien[^1]);
            Assert.All(campaign.Capture().World.Ufos, ufo =>
            {
                Assert.Equal(UfoStatus.Flying, ufo.Status);
                Assert.Equal(1, ufo.TrajectoryPoint);
                Assert.Equal(WorldAltitudes.VeryLow, ufo.Altitude);
                Assert.Equal(1760, ufo.Speed);
                Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), ufo.Position);
            });
            campaign = Reload(content, campaign, seed, "newborn-first-ground-lift.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2, ufo.TrajectoryPoint));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SilentDetectedBirthsStillAllocateLandingIdentity(bool cache, bool alerts)
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, groundTimer: 2,
            arrivalAltitude: 0, alerts: alerts);
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17).WithFacility("RADAR_HYPER_TEST");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(alerts ? 3 : 0, result.Events.OfType<UfoContactDetected>().Count());
        var after = campaign.Capture();
        Assert.All(after.World.Ufos, ufo =>
        {
            Assert.True(ufo.Detected);
            Assert.Equal(alerts, ufo.Id > 0);
            Assert.True(ufo.LandId > 0);
            Assert.Equal(UfoStatus.Landed, ufo.Status);
            Assert.Equal(10, ufo.SecondsRemaining);
        });
        Assert.Equal(3, after.World.Ufos.Select(ufo => ufo.LandId).Distinct().Count());
        campaign = Reload(content, campaign, 17, "newborn-ground-detected.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 2);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.True(ufo.LandId > 0));
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(0, ufo.LandId));
        initial = initial.WithNextId("STR_LANDING_SITE", int.MaxValue - 2);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO landing marker IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    [InlineData(false, 429496730)]
    [InlineData(true, 429496730)]
    public void InvalidNewbornLandingTimerRefusesBeforeWaveAndDetectionMutation(bool cache, int timer)
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, groundTimer: timer, arrivalAltitude: 0);
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17), content,
            new SplitMix64RandomSource(17)), "Alien mission wave spawning requires world simulation.");
    }
}
