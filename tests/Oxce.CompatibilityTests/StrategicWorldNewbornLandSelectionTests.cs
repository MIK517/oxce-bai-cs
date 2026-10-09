using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornLandSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FirstAirborneArrivalPreservesBothLandRetriesForTheNextWaypoint(bool cache, bool permitted)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "landRectangleOriginRows"), row =>
            row[0].GetInt32() == 1 && row[6].GetInt32() == 304);
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, true, groundTimer: 2, nextAltitude: 0,
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
