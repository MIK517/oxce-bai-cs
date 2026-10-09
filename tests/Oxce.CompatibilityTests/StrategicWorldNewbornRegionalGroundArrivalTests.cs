using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornRegionalGroundArrivalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RegionalOriginsUseLandSelectionBeforeConditionalFirstLanding(bool cache, bool mixed)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicWorldNewbornRegionalArrivalTests.Content(cache, mixed, 0, groundTimer: 2, arrivalAltitude: 0);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("FIRST_REGION")].Value;
        var arrivedSeen = false;
        var flyingSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldNewbornRegionalArrivalTests.Snapshot(content, seed, 0);
            var expectedRandom = new SplitMix64RandomSource(seed);
            var origins = new WorldPosition[3];
            var destinations = new WorldPosition[3];
            for (var index = 0; index < 3; index++)
            {
                origins[index] = WorldGeometry.LandPoint(region, content.RuntimeRules.Campaign.Globe, 5, 0, expectedRandom);
                destinations[index] = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                if (index < 2) WorldTrajectory.SpawnCountdown(0, expectedRandom);
            }
            for (var index = 0; index < 3; index++) expectedRandom.NextInclusive(0, 99);
            var expected = new (WorldPosition Position, WorldPosition Destination, bool Arrived)[3];
            for (var index = 0; index < 3; index++)
            {
                var arrived = WorldGeometry.Distance(origins[index], destinations[index]) <= WorldGeometry.RadianSpeed(2200);
                var position = WorldGeometry.Move(origins[index], destinations[index], WorldGeometry.RadianSpeed(2200));
                if (arrived)
                {
                    _ = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                    destinations[index] = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                }
                expected[index] = (position, destinations[index], arrived);
                arrivedSeen |= arrived;
                flyingSeen |= !arrived;
            }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(expectedRandom.State, after.RandomState);
            Assert.Equal(9, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(3, after.World.Ufos.Count);
            for (var index = 0; index < 3; index++)
            {
                var ufo = after.World.Ufos[index];
                Assert.Equal(expected[index].Position, ufo.Position);
                Assert.Equal(expected[index].Destination, ufo.Destination!.Position);
                Assert.Equal(expected[index].Arrived ? 1 : 0, ufo.TrajectoryPoint);
                Assert.Equal(expected[index].Arrived ? UfoStatus.Landed : UfoStatus.Flying, ufo.Status);
                Assert.Equal(expected[index].Arrived ? 0 : 2200, ufo.Speed);
                Assert.Equal(expected[index].Arrived ? 10 : 0, ufo.SecondsRemaining);
                Assert.Equal(10, ufo.Shield);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-regional-ground.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 4);
            AdvanceUnblocked(campaign, 3);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.True(arrivedSeen);
        Assert.Equal(mixed, flyingSeen);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void PointTerrainDecidesTimerAndLandingIdWithoutResampling(bool cache, int terrain)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "landRectangleOriginRows"), row => row[0].GetInt32() == 1);
        var allowed = terrain is 2 or 3;
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, groundTimer: allowed ? 2 : 0,
            arrivalAltitude: 0, fakeWaterLandingChance: terrain == 2 ? 100 : 0, extraRules: $$$"""
            globe:
              textures:
                - {id: 10, isOcean: {{{(terrain == 0 ? "true" : "false")}}}, fakeUnderwater: {{{(terrain is 1 or 2 ? "true" : "false")}}}}
            """);
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17).WithFacility("RADAR_HYPER_TEST");
        if (!allowed) initial = initial.WithNextId("STR_LANDING_SITE", int.MaxValue);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.All(after.World.Ufos, ufo =>
        {
            Assert.Equal(UfoStatus.Landed, ufo.Status);
            Assert.Equal(allowed ? 10 : 5, ufo.SecondsRemaining);
            Assert.Equal(allowed, ufo.LandId > 0);
            Assert.True(ufo.Detected);
            Assert.Equal(0, ufo.Id);
            Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), ufo.Position);
        });
        campaign = Reload(content, campaign, 17, "newborn-ground-terrain.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, allowed ? 2 : 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(UfoStatus.Flying, ufo.Status));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void BothGenerationCallersPreserveLandingDetectionOrder(bool cache, int generation)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = StrategicWorldNewbornRegionalArrivalTests.Content(cache, false, generation, groundTimer: 2, arrivalAltitude: 0);
        var initial = StrategicWorldNewbornRegionalArrivalTests.Snapshot(content, 17, generation).WithFacility("RADAR_HYPER_TEST");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(generation == 3 ? 6 : 3, after.World.Ufos.Count);
        Assert.Equal(generation is 1 or 3 ? 9 : 0, after.Regions[0].ActivityAlien[^1]);
        Assert.All(after.World.Ufos, ufo =>
        {
            Assert.Equal(1, ufo.TrajectoryPoint);
            Assert.Equal(UfoStatus.Landed, ufo.Status);
            Assert.Equal(10, ufo.SecondsRemaining);
            Assert.Equal(10, ufo.Shield);
        });
        Assert.Equal(generation is 1 or 3 ? 3 : 0, after.World.Ufos.Count(ufo => ufo.LandId > 0));
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "generated-first-ground.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(control, 4);
        AdvanceUnblocked(campaign, 3);
        Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        if (generation is 1 or 3)
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_LANDING_SITE", int.MaxValue - 2), content,
                new SplitMix64RandomSource(17)), "UFO landing marker IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumSafeLandingTimerSurvivesRepeatedRewrites(bool cache)
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, groundTimer: int.MaxValue / 5, arrivalAltitude: 0);
        var campaign = CampaignState.Restore(StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17), content,
            new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.All(after.World.Ufos, ufo => Assert.Equal(2_147_483_645, ufo.SecondsRemaining));
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "newborn-ground-timer-limit.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2_147_483_640, ufo.SecondsRemaining));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewbornLandingPolygonWorkIsBoundedBeforeMutation(bool cache)
    {
        var polygons = string.Join("\n", Enumerable.Repeat("    - [10, 0, -10, 20, -10, 20, 10, 0, 10]", 5000));
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, groundTimer: 2, arrivalAltitude: 0,
            extraRules: "globe:\n  polygons:\n" + polygons, extraRulesAfterOverlays: true);
        Assert.Equal(5000, content.RuntimeRules.Campaign.Globe.Polygons.Count);
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17), content,
            new SplitMix64RandomSource(17)), "Alien mission wave spawning requires world simulation.");
    }
}
