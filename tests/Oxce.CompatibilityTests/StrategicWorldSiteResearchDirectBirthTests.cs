using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchDirectBirthTests
{
    private static readonly string[] DiaryTopics = ["CORE", "BONUS"];
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SavedDirectSiteChainsSurviveTheBirthTickGrantAndPersistTheirLaterResearch(bool cache, bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, final);
        var initial = Snapshot(content);
        var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "direct-research-before.sav");
        AdvanceUnblocked(control, 1);
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.World.MissionSites.Count);
        Assert.All(after.World.MissionSites, site =>
        {
            Assert.Equal("RESEARCH_BORN_SITE", site.DeploymentId);
            Assert.Equal(1800, site.SecondsRemaining);
        });
        Assert.Equivalent(control.Capture().World.MissionSites, after.World.MissionSites, strict: true);
        Assert.Equal(control.Capture().RandomState, after.RandomState);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        if (final) Assert.Empty(after.World.Missions);
        else Assert.True(Assert.Single(after.World.Missions).Interrupted);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "direct-research-born.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        AdvanceUnblocked(campaign, 1080);
        var discovered = Assert.Single(campaign.Capture().World.MissionSites);
        Assert.True(discovered.Detected);
        Assert.Equal(1800, discovered.SecondsRemaining);
        Assert.Equal(DiaryTopics, campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));
        AdvanceUnblocked(campaign, 720);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Equal(DiaryTopics, campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));
        Assert.Empty(campaign.Capture().World.Missions);
        Reload(content, campaign, 17, "direct-research-expired.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedDirectSitesRetainImmediateResearchAndIdentityBoundaries(bool cache)
    {
        var content = Content(cache, duration: 0);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Newborn mission site research requires ordered expiry forecasts.");
        content = Content(cache);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    internal static RuntimeContent Content(bool cache, bool final = false, int duration = 1) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
          - {name: BONUS, cost: 1}
        alienMissions:
          - type: RESEARCH_DIRECT_CHAIN
            interruptResearch: CORE
            waves:
              - {ufo: RESEARCH_BORN_SITE, count: 2, trajectory: TRAJ_PATROL, timer: 0}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: RESEARCH_BORN_SITE
            markerName: RESEARCH_BORN_MARKER
            duration: [{{{duration}}}, {{{duration}}}]
            unlockedResearchOnDespawn: BONUS
            points: 4
        """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = StrategicWorldSiteResearchSpawnTests.Snapshot(content);
        return initial with
        {
            World = initial.World with
            { Missions = [initial.World.Missions[0] with { RuleId = "RESEARCH_DIRECT_CHAIN" }] }
        };
    }
}
