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
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, 300)]
    [InlineData(true, 300)]
    public void ForcedSiteReturnBurnsFuelBeforeActiveHunterEligibility(bool cache, int fuel)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, ruleFiles: ["strategic-world.rul", "strategic-hunting.rul", "strategic-site-timer.rul"]);
        var initial = Snapshot(content);
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
        Assert.Empty(after.World.MissionSites);
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        var craft = after.Bases.SelectMany(owner => owner.Crafts).Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(fuel - 60, craft.Fuel);
        Assert.Equal(WorldTargetKind.Base, craft.Destination!.Kind);
        Assert.Equal(fuel == 100, craft.LowFuel);
        var hunter = Assert.Single(after.World.Ufos);
        Assert.Equal(fuel == 300, hunter.Hunting);
        Assert.Equal(fuel == 300 ? WorldTargetKind.Craft : WorldTargetKind.Waypoint, hunter.Destination!.Kind);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-hunter-return.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }
}
