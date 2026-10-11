using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornLandSelectionTests
{
    [Fact]
    public void CityDestinationBypassDoesNotChargePolygonRetries()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var polygons = string.Join("\n", Enumerable.Repeat("    - [10, 0, -10, 20, -10, 20, 10, 0, 10]", 5000));
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false, groundTimer: 2, nextAltitude: 0,
            extraRulesAfterOverlays: true, extraRules: "globe:\n  polygons:\n" + polygons);
        Assert.Equal(5000, content.RuntimeRules.Campaign.Globe.Polygons.Count);
        var campaign = CampaignState.Restore(StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17), content,
            new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo =>
        {
            Assert.Equal(1, ufo.TrajectoryPoint);
            Assert.Equal(UfoStatus.Flying, ufo.Status);
            Assert.Equal(1760, ufo.Speed);
        });
        campaign = Reload(content, campaign, 17, "newborn-city-land-bypass.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(UfoStatus.Landed, ufo.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialDestinationLandWorkIsReservedEvenWithoutFirstArrival(bool supply)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var polygons = string.Join("\n", Enumerable.Repeat("    - [10, 0, -10, 20, -10, 20, 10, 0, 10]", 5000));
        var content = StrategicWorldNewbornFirstArrivalTests.Content(true, groundTimer: 2,
            nextAltitude: supply ? 3 : 0, arrivalAltitude: supply ? 0 : 2, objective: supply ? 5 : 0,
            extraRulesAfterOverlays: true, extraRules: "globe:\n  polygons:\n" + polygons + "\n" + """
            regions:
              - type: LIMIT_REGION
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[22, 22.02, 2, 2.02]]
            """);
        Assert.Equal(5000, content.RuntimeRules.Campaign.Globe.Polygons.Count);
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17);
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "LIMIT_REGION" }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission land selection exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstAirborneArrivalPreservesBothLandRetriesForTheNextWaypoint(bool permitted)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "landRectangleOriginRows"), row =>
            row[0].GetInt32() == 1 && row[6].GetInt32() == 304);
        var content = StrategicWorldNewbornFirstArrivalTests.Content(true, groundTimer: 2, nextAltitude: 0,
            fakeWaterLandingChance: permitted ? 100 : 0, extraRules: """
            regions:
              - type: RETRY_REGION
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22.02, 2, 2.02]]
            """);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("RETRY_REGION")].Value;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
            initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "RETRY_REGION" }] } };
            var random = new SplitMix64RandomSource(seed);
            var positions = new WorldPosition[3];
            for (var index = 0; index < 3; index++)
            {
                _ = WorldGeometry.RandomPoint(region, 5, -1, random);
                positions[index] = WorldGeometry.LandPoint(region, content.RuntimeRules.Campaign.Globe, 6,
                    permitted ? 100 : 0, random);
                if (index < 2) WorldTrajectory.SpawnCountdown(0, random);
            }
            for (var index = 0; index < 3; index++) random.NextInclusive(0, 99);
            var destinations = new WorldPosition[3];
            for (var index = 0; index < 3; index++)
            {
                _ = WorldGeometry.LandPoint(region, content.RuntimeRules.Campaign.Globe, 6, permitted ? 100 : 0, random);
                destinations[index] = WorldGeometry.LandPoint(region, content.RuntimeRules.Campaign.Globe, 6, permitted ? 100 : 0, random);
            }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(random.State, after.RandomState);
            Assert.Equal(3, after.World.Ufos.Count);
            for (var index = 0; index < 3; index++)
            {
                var ufo = after.World.Ufos[index];
                Assert.Equal(positions[index], ufo.Position);
                Assert.Equal(destinations[index], ufo.Destination!.Position);
                Assert.Equal(1, ufo.TrajectoryPoint);
                Assert.Equal(UfoStatus.Flying, ufo.Status);
                Assert.Equal(1760, ufo.Speed);
                Assert.Equal(10, ufo.Shield);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-land-retries.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.All(campaign.Capture().World.Ufos, ufo =>
            {
                Assert.Equal(2, ufo.TrajectoryPoint);
                Assert.Equal(UfoStatus.Landed, ufo.Status);
                Assert.Equal(permitted ? 10 : 5, ufo.SecondsRemaining);
            });
        }
    }
}
