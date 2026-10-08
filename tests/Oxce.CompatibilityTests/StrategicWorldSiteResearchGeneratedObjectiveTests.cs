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
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void GeneratedFinalObjectivesKeepTheResearchCompositionBoundary(bool cache, int objective)
    {
        var content = Content(cache, objective, hunting: false, prefix: false);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, hunting: false), content, new SplitMix64RandomSource(17)),
            "Mission site research requires alien-base generation phase forecasts.");
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

    internal static RuntimeContent Content(bool cache, int objective, bool hunting, bool prefix = true) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul"], cache, $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - type: GENERATED_OBJECTIVE_PREFIX
            objective: {{objective}}
            endlessInfiltration: false
            interruptResearch: CORE
            spawnZone: 1
            siteType: ALIEN_BASE_DEPLOY
            waves:
              - {count: 1, trajectory: TRAJ_PATROL, timer: 0}
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
            { Status = "STR_OUT", Fuel = 300, Speed = 0, Longitude = 0.2, Latitude = 0.1, Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)) }
        });
        return initial with
        {
            Time = hunting ? new(1, 1, 1, 1999, 0, 29, 55) : initial.Time,
            World = initial.World with
            {
                AlienBases = [new(2, "GENERATED_OBJECTIVE_BASE", "RACE_A", 0.2, 0.1)
            { Discovered = true, MinutesSinceLastHuntMissionGeneration = hunting ? 50 : 0 }]
            },
        };
    }
}
