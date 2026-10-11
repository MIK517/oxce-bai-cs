using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSelectedSiteWaypointTests
{
    [Fact]
    public void SelectedSiteAreaPreservesDestinationAndConvertsOnArrival()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-selected-site-waypoints.rul"]);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "selectedSiteRows"))
        {
            var landing = row[0].GetInt32() != 0;
            var area = row[1].GetInt32();
            var initial = CreateSnapshot(content, "Selected area");
            var rule = landing ? "SELECTED_SITE_LAND" : "SELECTED_SITE";
            var snapshot = initial with
            {
                RandomState = 17,
                Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                World = initial.World with { Missions = [new AlienMissionSnapshot(4, rule, "REGION", "RACE_A", 0, 0, 0, 0, area)] },
            };
            var random = new SplitMix64RandomSource(17);
            var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
            var origin = WorldGeometry.RandomPoint(region, 4, -1, random);
            if (landing)
            {
                var fake = random.NextInclusive(0, 99) < 20;
                var attempts = fake ? 100 : 1; // Both selected city points are ordinary land.
                for (var draw = 0; draw < attempts * 2; draw++) random.NextUnit();
                Assert.Equal(204, row[4].GetInt32()); // Captured fake-water request forces 100 retries.
            }
            else Assert.Equal(3, row[4].GetInt32());
            WorldTrajectory.SpawnCountdown(9000, random);
            random.NextInclusive(0, 99);
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(row[2].GetString(), ufo.Destination!.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[3].GetString(), ufo.Destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(WorldGeometry.Move(origin, ufo.Destination.Position, WorldGeometry.RadianSpeed(2200)), ufo.Position);
            Assert.Equal(random.State, after.RandomState);
            var restored = Reload(content, campaign, 17, "selected-area.sav");
            var horizon = restored.Execute(new AdvanceCampaignTime(500));
            Assert.DoesNotContain(horizon.Events, item => item is CampaignActionBlocked);
            Assert.Contains(horizon.Events, item => item is MissionSiteDetected);
            Assert.True(Assert.Single(restored.Capture().World.MissionSites).Detected);
        }
    }
}
