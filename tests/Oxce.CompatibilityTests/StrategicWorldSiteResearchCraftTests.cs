using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchCraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FollowerControlsResearchExpiryAndReturnBeforeFuelAndMovement(bool forced)
    {
        using var oracle = Oxce.TestSupport.TestFixtures.ReadVerifiedExpected("strategic-site-timer");
        Oxce.TestSupport.TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(0, Deployment, ["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = Snapshot(content);
        var site = snapshot.World.MissionSites[0] with
        {
            MissionRuleId = forced ? "MISSION_SITE_FORCE" : "MISSION_SITE",
            Longitude = 0.25,
            Latitude = 0.15,
        };
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            World = snapshot.World with { MissionSites = [site] },
            Bases = [owner with
            {
                Crafts = owner.Crafts.Select(craft => craft.RuleId == "SHIP" ? craft with { Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT", Takeoff = 0, Fuel = 100, Speed = 100,
                    Longitude = 0.5, Latitude = 0.3,
                    Destination = new(WorldTargetKind.MissionSite, "DIARY_SITE", site.Id, site.Longitude, site.Latitude),
                } } : craft).ToArray(),
            }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        if (forced)
        {
            Assert.Single(result.Events.OfType<MissionSiteLost>());
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(WorldTargetKind.Base, after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Destination!.Kind);
            var craftRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value;
            Assert.Equal(100 - WorldFlight.FuelConsumption(craftRule.RefuelItem is not null, craftRule.SpeedMaximum, craftRule.SpeedMaximum, 0),
                after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Fuel);
            Assert.Equal(["CORE", "LOOKUP", "BONUS", "BONUS_LOOKUP"], after.ResearchDiary.Select(entry => entry.RuleId));
            Assert.Equal(3, after.NextIds["SITE_COUNT"]);
        }
        else
        {
            Assert.Single(after.World.MissionSites);
            Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Fuel);
            Assert.Empty(after.ResearchDiary);
            Assert.Equal(WorldTargetKind.MissionSite, after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Destination!.Kind);
            Assert.False(after.NextIds.ContainsKey("SITE_COUNT"));
        }
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-craft-research.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedSiteResearchCannotBypassCraftLandingGuard(bool forced)
    {
        var content = Content(0, Deployment, ["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = Snapshot(content);
        var first = snapshot.World.MissionSites[0];
        var landing = first with { Id = 2, DeploymentId = "SITE_TIMER", MissionRuleId = "MISSION_SITE", SecondsRemaining = 3600 };
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            World = snapshot.World with { MissionSites = [first with { MissionRuleId = forced ? "MISSION_SITE_FORCE" : "MISSION_SITE" }, landing] },
            Bases = [owner with
            {
                Crafts = owner.Crafts.Select(craft => craft.RuleId == "SHIP" ? craft with { Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT", Takeoff = 0, Fuel = 100, Speed = 6000,
                    Longitude = landing.Longitude, Latitude = landing.Latitude,
                    Destination = new(WorldTargetKind.MissionSite, "STR_TERROR_SITE", landing.Id, landing.Longitude, landing.Latitude),
                } } : craft).ToArray(),
                Soldiers = [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } }, .. owner.Soldiers.Skip(1)],
            }],
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)),
            "Craft landing requires deployment (Phase 6 branch 5).");
    }

    [Fact]
    public void SiteGrantAndPatrolComposeForSeventyTwoHoursAcrossDailyReloads()
    {
        Oxce.TestSupport.TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        Oxce.TestSupport.TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(0, Deployment);
        var snapshot = Snapshot(content);
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with
        {
            Research = [],
            Crafts = owner.Crafts.Select(craft => craft.RuleId == "SHIP" ? craft with { Logistics = craft.Logistics! with
            {
                Status = "STR_OUT", Takeoff = 0, Fuel = 100, Speed = 3000,
                Longitude = owner.Longitude, Latitude = owner.Latitude, Destination = null,
            } } : craft).ToArray(),
        }]
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        for (var hour = 1; hour <= 72; hour++)
        {
            AdvanceUnblocked(campaign, 720);
            if (hour % 24 == 0) campaign = Reload(content, campaign, 17, "site-research-patrol-days.sav");
        }
        var after = campaign.Capture();
        Assert.Equal(5, after.Time.Day);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(4, after.ResearchDiary.Count);
        Assert.Equal(3, after.NextIds["SITE_COUNT"]);
        Assert.Equal(100, after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Fuel);
    }
}
