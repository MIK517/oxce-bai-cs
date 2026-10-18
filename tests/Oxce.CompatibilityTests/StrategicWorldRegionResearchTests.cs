using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRegionResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedRegionAndAreaChoicesKeepTheirOwnGrantSequence(bool areas)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = Content(areas);
        var firstSeen = false;
        var secondSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var first = after.NextIds.GetValueOrDefault("PATH_A_MARKER", 1) == 3;
            var second = after.NextIds.GetValueOrDefault("PATH_B_MARKER", 1) == 3;
            Assert.NotEqual(first, second);
            Assert.Equal(first, after.CompletedResearch.Contains("CORE"));
            Assert.Equal(second, after.CompletedResearch.Contains("BONUS"));
            Assert.Equal(first ? "CORE" : "BONUS", Assert.Single(after.ResearchDiary).RuleId);
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Events);
            Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
            firstSeen |= first;
            secondSeen |= second;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "generated-path-research.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.True(firstSeen);
        Assert.True(secondSeen);
    }

    [Fact]
    public void UnchosenPathCannotSuppressAnEarlierDueEvent()
    {
        var content = Content(false);
        var initial = Snapshot(content);
        AssertEventTickRuns(CampaignState.Restore(initial with
        {
            World = initial.World with { Events = [new GeoscapeEventSnapshot("PATH_A_EVENT", 30)] },
        }, content, new SplitMix64RandomSource(17)), popped: []);
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = StrategicWorldOptionalGenerationResearchTests.Snapshot(content);
        return initial with
        {
            World = initial.World with
            { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "PATH_BASE" }] }
        };
    }

    internal static RuntimeContent Content(bool areas, int secondDuration = 0, bool suppressed = false, bool common = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
          - {name: BONUS, cost: 1}
        globe:
          textures:
            - {id: 910, deployments: {PATH_A_SITE: 1}}
            - {id: 911, deployments: {{{(suppressed ? "{\"\": 1}" : "{PATH_B_SITE: 1}")}}}}
        regions:
          - type: PATH_A_REGION
            missionZones:
              - [[11.459155902616464, 11.459155902616464, 5.7295779513082321, 5.7295779513082321, 910]{{{(areas ? ", [11.459155902616464, 11.459155902616464, 5.7295779513082321, 5.7295779513082321, 911]" : "")}}}]
          - type: PATH_B_REGION
            missionZones:
              - [[11.459155902616464, 11.459155902616464, 5.7295779513082321, 5.7295779513082321, 911]]
        alienMissions:
          - type: PATH_MISSION
            objective: 3
            spawnZone: 0
            regionWeights: {0: {{{(areas ? "{PATH_A_REGION: 1}" : "{PATH_A_REGION: 1, PATH_B_REGION: 1}")}}}}
            waves:
              - {count: {{{(common ? 1 : 2)}}}, trajectory: TRAJ_PATROL, objective: true, timer: 0}
        {{{(common ? "      - {ufo: PATH_COMMON_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}" : "")}}}
        alienDeployments:
          - {type: PATH_A_SITE, markerName: PATH_A_MARKER, duration: [0, 0], unlockedResearchOnDespawn: CORE, despawnEvents: {PATH_A_EVENT: 1}}
          - {type: PATH_B_SITE, markerName: PATH_B_MARKER, duration: [{{{secondDuration}}}, {{{secondDuration}}}], unlockedResearchOnDespawn: BONUS, despawnEvents: {PATH_B_EVENT: 1}}
          - {type: PATH_COMMON_SITE, markerName: PATH_COMMON_MARKER, duration: [0, 0], unlockedResearchOnDespawn: BONUS, despawnEvents: {PATH_B_EVENT: 1}}
          - type: PATH_BASE
            markerName: PATH_BASE_MARKER
            genMission: {PATH_MISSION: 1}
            genMissionFreq: 100
            genMissionLimit: 1
        events:
          - {name: PATH_A_EVENT, interruptResearch: CORE}
          - {name: PATH_B_EVENT, interruptResearch: BONUS}
        """);
}
