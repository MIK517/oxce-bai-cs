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
    [InlineData(false)]
    [InlineData(true)]
    public void SavedDirectSiteChainsSurviveTheBirthTickGrantAndPersistTheirLaterResearch(bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(final);
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
        campaign = ReloadRepeatedly(content, campaign, 17, "direct-research-born.sav", 3, after);
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

    [Fact]
    public void SavedDirectSitesRetainDurationAndIdentityBoundaries()
    {
        var content = Content(duration: -1);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Direct mission site duration is outside the supported range.");
        content = Content();
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedDirectSitesComposeBeforeResearchAndRetainCleanupOrdering(bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var objective = 0; objective <= 3; objective += 3)
        {
            var content = Content(final, generated: true, objective: objective,
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
            campaign = ReloadRepeatedly(content, campaign, 17, "daily-direct-after.sav", 3, after);
        }
    }

    [Fact]
    public void DailyGeneratedDirectSitesPreserveDurationAndMarkerBoundaries()
    {
        var content = Content(generated: true, duration: -1);
        AssertTimeBlocked(CampaignState.Restore(GeneratedSnapshot(content), content, new SplitMix64RandomSource(17)),
            "Direct mission site duration is outside the supported range.");
        content = Content(generated: true);
        AssertTimeBlocked(CampaignState.Restore(GeneratedSnapshot(content).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LateGeneratedDirectSitesDeferTheirExpiryAndResearchUntilTheNextCaller(int duration)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        for (var finalIndex = 0; finalIndex <= 1; finalIndex++)
        {
            var final = finalIndex == 1;
            var content = Content(final, duration, hunting: true, interrupt: final ? "MISSING_RESEARCH" : "CORE");
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
            campaign = ReloadRepeatedly(content, campaign, 17, "late-direct-after.sav", 3, after);
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

    [Fact]
    public void LateGeneratedDirectSitesRetainBirthTickCapacityBoundaries()
    {
        var content = Content(hunting: true, duration: 0);
        AssertTimeBlocked(CampaignState.Restore(GeneratedSnapshot(content, hunting: true).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    internal static RuntimeContent Content(bool final = false, int duration = 1, bool generated = false, bool hunting = false,
        int objective = 0, string interrupt = "CORE", int? maximumDuration = null, string eventInterrupt = "", int frequency = 100,
        int count = 2, int firstTimer = 0) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], $$$"""
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
              - {ufo: RESEARCH_BORN_SITE, count: {{{count}}}, trajectory: TRAJ_PATROL, timer: {{{firstTimer}}}}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: RESEARCH_BORN_SITE
            markerName: RESEARCH_BORN_MARKER
            duration: [{{{duration}}}, {{{maximumDuration ?? duration}}}]
            unlockedResearchOnDespawn: BONUS
            points: 4
        {{{(eventInterrupt.Length == 0 ? "" : "    despawnEvents: {NEWBORN_EVENT: 1}")}}}
          - type: RESEARCH_DIRECT_BASE
            markerName: RESEARCH_DIRECT_BASE_MARKER
            genMission: {RESEARCH_DIRECT_CHAIN: 1}
            genMissionFreq: {{{(generated ? frequency : 0)}}}
            genMissionLimit: 1
            baseDetectionRange: {{{(hunting ? 100 : 0)}}}
            baseDetectionChance: 100
            huntMissionMaxFrequency: 9000
            huntMissionWeights: {0: {RESEARCH_DIRECT_CHAIN: 1}}
        """ + (eventInterrupt.Length == 0 ? "" : "\nevents:\n  - {name: NEWBORN_EVENT, interruptResearch: " + eventInterrupt + "}\n"));

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
