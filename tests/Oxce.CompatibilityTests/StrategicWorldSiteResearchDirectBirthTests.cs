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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DailyGeneratedDirectSitesComposeBeforeResearchAndRetainCleanupOrdering(bool cache, bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var objective = 0; objective <= 3; objective += 3)
        {
            var content = Content(cache, final, generated: true, objective: objective,
                interrupt: final ? "MISSING_RESEARCH" : "CORE");
            var initial = GeneratedSnapshot(content);
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "daily-direct-before.sav");
            AdvanceUnblocked(control, 1);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(2, after.World.MissionSites.Count);
            Assert.All(after.World.MissionSites, site => Assert.Equal(1800, site.SecondsRemaining));
            Assert.Equivalent(control.Capture().World.MissionSites, after.World.MissionSites, strict: true);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            if (final) Assert.Empty(after.World.Missions);
            else Assert.True(Assert.Single(after.World.Missions).Interrupted);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "daily-direct-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedDirectSitesPreserveImmediateExpiryAndMarkerBoundaries(bool cache)
    {
        var content = Content(cache, generated: true, duration: 0);
        AssertTimeBlocked(CampaignState.Restore(GeneratedSnapshot(content), content, new SplitMix64RandomSource(17)),
            "Newborn mission site research requires ordered expiry forecasts.");
        content = Content(cache, generated: true);
        AssertTimeBlocked(CampaignState.Restore(GeneratedSnapshot(content).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void LateGeneratedDirectSitesDeferTheirExpiryAndResearchUntilTheNextCaller(bool cache, int duration)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        for (var finalIndex = 0; finalIndex <= 1; finalIndex++)
        {
            var final = finalIndex == 1;
            var content = Content(cache, final, duration, hunting: true, interrupt: final ? "MISSING_RESEARCH" : "CORE");
            var initial = GeneratedSnapshot(content, hunting: true);
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "late-direct-before.sav");
            AdvanceUnblocked(control, 1);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(2, after.World.MissionSites.Count);
            Assert.All(after.World.MissionSites, site => Assert.Equal(duration * 3600, site.SecondsRemaining));
            Assert.Equivalent(control.Capture().World.MissionSites, after.World.MissionSites, strict: true);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            Assert.False(Assert.Single(after.World.Missions).Interrupted);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "late-direct-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 360);
            var later = campaign.Capture();
            if (duration == 0)
            {
                Assert.Empty(later.World.MissionSites);
                Assert.Equal(DiaryTopics, later.ResearchDiary.Select(entry => entry.RuleId));
            }
            else
            {
                Assert.Equal(2, later.World.MissionSites.Count);
                Assert.All(later.World.MissionSites, site => Assert.Equal(1800, site.SecondsRemaining));
                Assert.Single(later.ResearchDiary);
            }
            if (final) Assert.Empty(later.World.Missions);
            else Assert.False(Assert.Single(later.World.Missions).Interrupted);
            Assert.Equivalent(later, Reload(content, campaign, 17, "late-direct-next-caller.sav").Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateGeneratedDirectSitesRetainBirthTickCapacityBoundaries(bool cache)
    {
        var content = Content(cache, hunting: true, duration: 0);
        AssertTimeBlocked(CampaignState.Restore(GeneratedSnapshot(content, hunting: true).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    internal static RuntimeContent Content(bool cache, bool final = false, int duration = 1, bool generated = false, bool hunting = false,
        int objective = 0, string interrupt = "CORE") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
          - {name: BONUS, cost: 1}
        alienMissions:
          - type: RESEARCH_DIRECT_CHAIN
            objective: {{{objective}}}
            spawnZone: 0
            siteType: RESEARCH_BORN_SITE
            interruptResearch: {{{interrupt}}}
            waves:
              - {ufo: RESEARCH_BORN_SITE, count: 2, trajectory: TRAJ_PATROL, timer: 0}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: RESEARCH_BORN_SITE
            markerName: RESEARCH_BORN_MARKER
            duration: [{{{duration}}}, {{{duration}}}]
            unlockedResearchOnDespawn: BONUS
            points: 4
          - type: RESEARCH_DIRECT_BASE
            markerName: RESEARCH_DIRECT_BASE_MARKER
            genMission: {RESEARCH_DIRECT_CHAIN: 1}
            genMissionFreq: {{{(generated ? 100 : 0)}}}
            genMissionLimit: 1
            baseDetectionRange: {{{(hunting ? 100 : 0)}}}
            baseDetectionChance: 100
            huntMissionMaxFrequency: 9000
            huntMissionWeights: {0: {RESEARCH_DIRECT_CHAIN: 1}}
        """);

    internal static CampaignSnapshot GeneratedSnapshot(RuntimeContent content, bool hunting = false)
    {
        var initial = StrategicWorldSiteResearchGeneratedObjectiveTests.Snapshot(content, hunting);
        if (hunting) initial = initial with { Time = new(1, 1, 1, 1999, 0, 59, 55) };
        return initial with
        {
            World = initial.World with
            {
                AlienBases = [new(2, "RESEARCH_DIRECT_BASE", "RACE_A", 0.2, 0.1)
            { Discovered = true, MinutesSinceLastHuntMissionGeneration = hunting ? 8990 : 0 }]
            }
        };
    }

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
