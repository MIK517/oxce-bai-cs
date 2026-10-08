using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchGeneratedObjectiveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedCounterPrefixesRunBeforeSiteResearch(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        for (var objective = 1; objective <= 3; objective++) AssertPrefix(cache, objective, hunting: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HuntingGeneratedCounterPrefixesRunAfterSiteResearch(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        for (var objective = 1; objective <= 3; objective++) AssertPrefix(cache, objective, hunting: true);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void HuntingFinalObjectivesKeepTheResearchCompositionBoundary(bool cache, int objective)
    {
        var content = Content(cache, objective, hunting: true, prefix: false);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, hunting: true), content, new SplitMix64RandomSource(17)),
            "Mission site research requires alien-base generation phase forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeventyTwoHoursComposeSiteResearchHuntingGenerationAndDailyInterruption(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        for (var objective = 1; objective <= 3; objective++)
        {
            var content = Content(cache, objective, hunting: true);
            var initial = Snapshot(content, hunting: true);
            var batched = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            var reloaded = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(batched, 1);
            AdvanceUnblocked(reloaded, 1);
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(batched, 17_280);
                for (var hour = 0; hour < 24; hour++) AdvanceUnblocked(reloaded, 720);
                reloaded = Reload(content, reloaded, 17, "research-objective-endurance.sav");
                Assert.Equivalent(batched.Capture(), reloaded.Capture(), strict: true);
                var after = batched.Capture();
                Assert.Empty(after.World.Ufos);
                Assert.Empty(after.World.MissionSites);
                Assert.Single(after.World.AlienBases);
                Assert.InRange(after.World.Missions.Count, 1, 24);
                Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
                Assert.All(after.Countries, country => Assert.False(country.NewPact));
            }
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void DailyGeneratedFinalObjectivesCompleteBeforeSiteResearch(bool cache, int objective)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = Content(cache, objective, hunting: false, prefix: false);
        var initial = Snapshot(content, hunting: false);
        var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "daily-final-research-before.sav");
        AdvanceUnblocked(control, 1);
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.World.AlienBases.Count);
        Assert.Equivalent(control.Capture().World.AlienBases, after.World.AlienBases, strict: true);
        Assert.Equal(control.Capture().RandomState, after.RandomState);
        Assert.Equal(objective == 1, after.Countries.Any(country => country.NewPact));
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.Ufos);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(1, after.World.AlienBases.Single(alienBase => alienBase.DeploymentId == "GENERATED_OBJECTIVE_BASE").GenMissionCount);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "daily-final-research-after.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void DailyGeneratedFinalObjectivesSkipUnusedInterruptionLinksAndKeepMarkerLimits(bool cache, int objective)
    {
        var content = Content(cache, objective, hunting: false, prefix: false, interrupt: "MISSING_RESEARCH");
        var initial = Snapshot(content, hunting: false);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(2, campaign.Capture().World.AlienBases.Count);
        Assert.Equal("CORE", Assert.Single(campaign.Capture().ResearchDiary).RuleId);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", int.MaxValue), content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
        Assert.Equivalent(campaign.Capture(), Reload(content, campaign, 17, "daily-final-unused-link.sav").Capture(), strict: true);
    }

    private static void AssertPrefix(bool cache, int objective, bool hunting)
    {
        var content = Content(cache, objective, hunting);
        var initial = Snapshot(content, hunting);
        var expected = new SplitMix64RandomSource(initial.RandomState);
        if (!hunting)
        {
            expected.NextInclusive(1, 1);
            expected.NextInclusive(0, 99);
            expected.NextInclusive(0, 99);
        }
        else
        {
            expected.NextInclusive(0, 99);
            expected.NextInclusive(1, 1);
        }
        var area = -1;
        if (objective == 3)
        {
            var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
            area = expected.NextInclusive(0, region.MissionZones[1].Areas.Count - 1);
        }
        expected.NextInclusive(0, 0);
        var countdown = WorldTrajectory.SpawnCountdown(9000, expected);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Empty(after.World.Ufos);
        Assert.Single(after.World.AlienBases);
        Assert.All(after.Countries, country => Assert.False(country.NewPact));
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(!hunting, mission.Interrupted);
        Assert.Equal((1, 0, countdown, area), (mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown, mission.MissionSiteZoneArea));
        Assert.Equal(2, mission.AlienBase!.Id);
        Assert.Equal(expected.State, after.RandomState);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-generated-objective-prefix.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyEndlessInfiltrationSurvivesCompletionAndValidatesItsInterruptionLink(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-repeat");
        var content = Content(cache, 1, hunting: false, prefix: false, interrupt: "MISSING_RESEARCH", endless: true, firstTimer: 30);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, hunting: false), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
        content = Content(cache, 1, hunting: false, prefix: false, endless: true, firstTimer: 30);
        var campaign = CampaignState.Restore(Snapshot(content, hunting: false), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        Assert.True(mission.Interrupted);
        Assert.Equal(0, mission.NextWave);
        Assert.Equal(2, after.World.AlienBases.Count);
        Assert.True(Assert.Single(after.Countries).NewPact);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Equivalent(after, Reload(content, campaign, 17, "endless-research-survivor.sav").Capture(), strict: true);
        AdvanceUnblocked(campaign, 360);
        Assert.Empty(campaign.Capture().World.Missions);
    }

    internal static RuntimeContent Content(bool cache, int objective, bool hunting, bool prefix = true, string interrupt = "CORE", bool endless = false, int firstTimer = 0) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul"], cache, $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - type: GENERATED_OBJECTIVE_PREFIX
            objective: {{objective}}
            endlessInfiltration: {{endless.ToString().ToLowerInvariant()}}
            interruptResearch: {{interrupt}}
            spawnZone: 1
            siteType: ALIEN_BASE_DEPLOY
            waves:
              - {count: 1, trajectory: TRAJ_PATROL, timer: {{firstTimer}}}
        {{(prefix ? "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}" : "")}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: GENERATED_OBJECTIVE_BASE
            markerName: GENERATED_PREFIX
            genMission: {GENERATED_OBJECTIVE_PREFIX: 1}
            genMissionFreq: {{{(hunting ? 0 : 100)}}}
            genMissionLimit: 1
            baseDetectionRange: {{{(hunting ? 100 : 0)}}}
            baseDetectionChance: 100
            huntMissionMaxFrequency: 60
            huntMissionWeights: {0: {GENERATED_OBJECTIVE_PREFIX: 1}}
        """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, bool hunting)
    {
        var initial = StrategicWorldSiteResearchGenerationTests.Snapshot(content);
        if (hunting) initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_OUT", Fuel = 300, Speed = 0, Longitude = 0.2, Latitude = 0.1, Destination = WorldTargetReference.ForWaypoint(1, new(1, 0.6)) }
        });
        return initial with
        {
            Time = hunting ? new(1, 1, 1, 1999, 0, 29, 55) : initial.Time,
            World = initial.World with
            {
                Waypoints = hunting ? [new(1, 1, 0.6)] : [],
                AlienBases = [new(2, "GENERATED_OBJECTIVE_BASE", "RACE_A", 0.2, 0.1)
            { Discovered = true, MinutesSinceLastHuntMissionGeneration = hunting ? 50 : 0 }]
            },
        };
    }
}
