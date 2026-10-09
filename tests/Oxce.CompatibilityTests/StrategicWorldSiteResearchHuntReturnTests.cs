using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchHuntingTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchHuntReturnTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GroundedFollowersKeepTheirStatusWhenTheirTargetDisappears(bool cache, bool alienBase)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        var content = Content(cache, extraRules: "  - {type: QUIET_BASE, markerName: QUIET, baseSelfDestructCode: UNLOCK}",
            ruleFiles: ["strategic-world.rul", "strategic-hunting.rul", "strategic-site-timer.rul"],
            finalRules: "crafts: [{type: SHIP, fuelMax: 300}]\n");
        var initial = Snapshot(content);
        initial = initial with
        {
            Time = alienBase ? new(1, 1, 1, 1999, 23, 59, 55) : initial.Time,
            CompletedResearch = alienBase ? ["UNLOCK"] : initial.CompletedResearch,
            World = initial.World with
            {
                AlienBases = alienBase ? [new(2, "QUIET_BASE", "RACE_A", 0.2, 0.1) { Discovered = true }] : [],
                MissionSites = [initial.World.MissionSites[0] with { MissionRuleId = "MISSION_SITE_FORCE" }],
                Ufos = [initial.World.Ufos[0] with
                { Hunting = true, OriginalWaypoint = new(1, 0.6), Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21) }],
            }
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_READY",
                Fuel = 300,
                Destination = new(alienBase ? WorldTargetKind.AlienBase : WorldTargetKind.MissionSite,
                    alienBase ? "QUIET" : "DIARY_SITE", alienBase ? 2 : 1, 0.2, 0.1),
            }
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var craft = after.Bases.SelectMany(owner => owner.Crafts).Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal("STR_READY", craft.Status);
        Assert.Equal(300, craft.Fuel);
        Assert.Equal(6000, craft.Speed);
        Assert.Equal(60, craft.Takeoff);
        Assert.Equal(WorldTargetKind.Base, craft.Destination!.Kind);
        Assert.True(Assert.Single(after.World.Ufos).Hunting);
        Assert.Equivalent(after, Reload(content, campaign, 17, "grounded-world-follower.sav").Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, 300)]
    [InlineData(true, 300)]
    public void DailyBaseDeletionReturnsFollowersBeforeSiteResearchAndHunting(bool cache, int fuel)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, extraRules: "  - {type: QUIET_BASE, markerName: QUIET, baseSelfDestructCode: UNLOCK}",
            finalRules: "crafts: [{type: SHIP, fuelMax: 300}]\n");
        var initial = Snapshot(content);
        initial = initial with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            CompletedResearch = ["UNLOCK"],
            World = initial.World with
            { AlienBases = [new(2, "QUIET_BASE", "RACE_A", 0.2, 0.1) { Discovered = true }] }
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Fuel = fuel, Destination = new(WorldTargetKind.AlienBase, "QUIET", 2, 0.2, 0.1) }
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.World.AlienBases);
        Assert.Empty(after.World.MissionSites);
        var craft = after.Bases.SelectMany(owner => owner.Crafts).Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(fuel - 60, craft.Fuel);
        Assert.Equal(WorldTargetKind.Base, craft.Destination!.Kind);
        Assert.Equal(fuel == 100, craft.LowFuel);
        Assert.Equal(fuel == 300, Assert.Single(after.World.Ufos).Hunting);
        Assert.Equivalent(after, Reload(content, campaign, 17, "site-research-base-return.sav").Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, 100, true)]
    [InlineData(true, 100, true)]
    [InlineData(false, 300, true)]
    [InlineData(true, 300, true)]
    [InlineData(false, 100, false)]
    [InlineData(true, 100, false)]
    public void ForcedSiteReturnBurnsFuelBeforeActiveHunterEligibility(bool cache, int fuel, bool expiry)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, ruleFiles: ["strategic-world.rul", "strategic-hunting.rul", "strategic-site-timer.rul"],
            finalRules: "crafts: [{type: SHIP, fuelMax: 300}]\n");
        var initial = Snapshot(content);
        if (!expiry) initial = initial with { Time = new(1, 1, 1, 1999, 1, 19, 55) };
        var site = initial.World.MissionSites[0];
        initial = initial with
        {
            World = initial.World with
            {
                MissionSites = [site with { MissionRuleId = "MISSION_SITE_FORCE" }],
                Ufos = [initial.World.Ufos[0] with
                {
                    Hunting = true, OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
                }],
            }
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Fuel = fuel, Destination = new(WorldTargetKind.MissionSite, "DIARY_SITE", 1, site.Longitude, site.Latitude) }
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(expiry ? 0 : 1, after.World.MissionSites.Count);
        Assert.Equal(expiry, Assert.Single(after.World.Missions).Interrupted);
        if (expiry) Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        else Assert.Empty(after.ResearchDiary);
        var craft = after.Bases.SelectMany(owner => owner.Crafts).Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(expiry ? fuel - 60 : fuel, craft.Fuel);
        Assert.Equal(expiry ? WorldTargetKind.Base : WorldTargetKind.MissionSite, craft.Destination!.Kind);
        Assert.Equal(expiry && fuel == 100, craft.LowFuel);
        var hunter = Assert.Single(after.World.Ufos);
        Assert.Equal(!expiry || fuel == 300, hunter.Hunting);
        Assert.Equal(!expiry || fuel == 300 ? WorldTargetKind.Craft : WorldTargetKind.Waypoint, hunter.Destination!.Kind);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-hunter-return.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }
}
