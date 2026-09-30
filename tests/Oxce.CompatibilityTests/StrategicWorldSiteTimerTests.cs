using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteTimerTests
{
    [Fact]
    public void TimerFollowerAndScoreBranchesMatchExtractedReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-timer");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var actual = WorldSiteTimer.Advance(row[0].GetInt32(), row[1].GetBoolean(),
                row[2].GetBoolean(), row[3].GetInt32(), row[4].GetInt32());
            var encoded = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                actual.Remove, actual.SecondsRemaining, actual.Score, actual.ReturnFollowers, actual.LostAlert
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[5].GetBoolean(), row[6].GetInt32(), row[7].GetInt32(), row[8].GetBoolean(), row[9].GetInt32() != 0
            });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(encoded));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveSiteScoresCountsDownAndExpiresAcrossReload(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-site-timer.rul"], fromCache);
        var snapshot = CreateSnapshot(content, "Site timer") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        snapshot = WithSite(snapshot, "SITE_TIMER", 3600);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        AdvanceUnblocked(campaign, 1);
        var counted = campaign.Capture();
        Assert.Equal(1800, Assert.Single(counted.World.MissionSites).SecondsRemaining);
        Assert.Equal(before.Regions[0].ActivityAlien[^1] + 4, counted.Regions[0].ActivityAlien[^1]);
        Assert.Equal(before.Countries[0].ActivityAlien[^1] + 4, counted.Countries[0].ActivityAlien[^1]);

        campaign = Reload(content, campaign, 17, "site-countdown.sav");
        AdvanceUnblocked(campaign, 360);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.MissionSites).SecondsRemaining);
        AdvanceUnblocked(campaign, 360);
        var expired = campaign.Capture();
        Assert.Empty(expired.World.MissionSites);
        Assert.Equal(before.Regions[0].ActivityAlien[^1] + 17, expired.Regions[0].ActivityAlien[^1]);
        Assert.Equal(before.Countries[0].ActivityAlien[^1] + 17, expired.Countries[0].ActivityAlien[^1]);
        Assert.Empty(Reload(content, campaign, 17, "site-expired.sav").Capture().World.MissionSites);
    }

    [Fact]
    public void FollowerHoldsExpiredSiteAndUnsupportedDespawnStopsBeforeTick()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = CreateSnapshot(content, "Targeted site") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        snapshot = WithSite(snapshot, "SITE_TIMER", 0);
        var site = Assert.Single(snapshot.World.MissionSites);
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts[0];
        snapshot = snapshot with
        {
            Bases = [owner with
            {
                Crafts = [craft with { Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Longitude = 0.5,
                    Latitude = 0.3,
                    Destination = new(WorldTargetKind.MissionSite, "STR_TERROR_SITE", site.Id,
                        site.Longitude, site.Latitude),
                } }, .. owner.Crafts.Skip(1)]
            }, .. snapshot.Bases.Skip(1)]
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.MissionSites).SecondsRemaining);

        var blockedSnapshot = WithSite(CreateSnapshot(content, "Guarded site") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        }, "SITE_DEPLOY", 0);
        var blocked = CampaignState.Restore(blockedSnapshot, content, new SplitMix64RandomSource(17));
        var before = blocked.Capture();
        Assert.Contains(blocked.Execute(new AdvanceCampaignTime(1)).Events,
            item => item is CampaignActionBlocked action && action.Reason.Contains("despawn", StringComparison.OrdinalIgnoreCase));
        Assert.Equivalent(before, blocked.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForcedExpiryReturnsFollowerBeforeLandingAndPersists(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-site-timer.rul"], fromCache);
        var snapshot = WithSite(CreateSnapshot(content, "Forced site") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        }, "SITE_TIMER", 0);
        var site = Assert.Single(snapshot.World.MissionSites) with
        {
            MissionRuleId = "MISSION_SITE_FORCE",
            Longitude = 0.25,
            Latitude = 0.15,
            Name = "Site",
        };
        snapshot = snapshot with { World = snapshot.World with { MissionSites = [site] } };
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with
            {
                Crafts = [owner.Crafts[0] with { Logistics = owner.Crafts[0].Logistics! with
                {
                    Status = "STR_OUT", Takeoff = 0, Fuel = 50, Speed = 6000,
                    Longitude = site.Longitude, Latitude = site.Latitude,
                    Destination = new(WorldTargetKind.MissionSite, "STR_TERROR_SITE", site.Id,
                        site.Longitude, site.Latitude),
                } }, .. owner.Crafts.Skip(1)],
                Soldiers = [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with
                {
                    CraftType = "SHIP", CraftId = 1,
                } }, .. owner.Soldiers.Skip(1)],
            }, .. snapshot.Bases.Skip(1)]
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Single(result.Events.OfType<MissionSiteLost>());
        Assert.Equal(new MissionSiteLost(site.Id, site.DeploymentId, "Site"),
            result.Events.OfType<MissionSiteLost>().Single());
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(WorldTargetKind.Base, after.Bases[0].Crafts[0].Logistics!.Destination!.Kind);
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 9, after.Regions[0].ActivityAlien[^1]);
        var reloaded = Reload(content, campaign, 17, "forced-site-expiry.sav").Capture();
        Assert.Empty(reloaded.World.MissionSites);
        Assert.Equal(WorldTargetKind.Base, reloaded.Bases[0].Crafts[0].Logistics!.Destination!.Kind);
    }

    [Fact]
    public void CombinedSiteActivityOverflowStopsBeforeTheTick()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = WithSite(CreateSnapshot(content, "Site activity overflow") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        }, "SITE_TIMER", 3600);
        snapshot = snapshot with
        {
            Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 3] }, .. snapshot.Regions.Skip(1)]
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Contains(campaign.Execute(new AdvanceCampaignTime(1)).Events,
            item => item is CampaignActionBlocked action && action.Reason.Contains("activity", StringComparison.OrdinalIgnoreCase));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static CampaignSnapshot WithSite(CampaignSnapshot snapshot, string deployment, int seconds) =>
        snapshot with
        {
            World = snapshot.World with
            {
                MissionSites = [new MissionSiteSnapshot(1, "STR_ALIEN_TERROR", deployment,
                    "RACE_A", 0.2, 0.1, seconds) { Detected = true }]
            }
        };
}
