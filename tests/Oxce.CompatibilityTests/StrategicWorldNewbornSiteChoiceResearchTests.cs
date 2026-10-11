using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornSiteChoiceResearchTests
{
    private static readonly string[] Topics = ["CORE", "BONUS"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualImmediateGrantAlternativesPreserveSelectedBirthsCountersAndRandomState(bool generated)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = Content(generated);
        var seenA = false;
        var seenB = false;
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var initial = Snapshot(content, generated) with { RandomState = seed };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "choice-research-before.sav");
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Missions);
            Assert.Equal(Topics, after.ResearchDiary.Select(entry => entry.RuleId));
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Equivalent(control.Capture().NextIds, after.NextIds.Where(pair => pair.Key != "SITE_COUNT")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), strict: true);
            Assert.Equal(initial.NextIds.GetValueOrDefault("SITE_COUNT", 1) + 2, after.NextIds["SITE_COUNT"]);
            var a = after.NextIds.GetValueOrDefault("CHOICE_A_MARKER", 1) - 1;
            var b = after.NextIds.GetValueOrDefault("CHOICE_B_MARKER", 1) - 1;
            Assert.Equal(2, a + b);
            Assert.Equal(a, after.NextIds.GetValueOrDefault("CHOICE_A_COUNTER", 1) - 1);
            Assert.Equal(b, after.NextIds.GetValueOrDefault("CHOICE_B_COUNTER", 1) - 1);
            seenA |= a != 0;
            seenB |= b != 0;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "choice-research-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.True(seenA);
        Assert.True(seenB);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryReachableDeploymentRetainsResearchDurationAndEventBoundaries(bool generated)
    {
        var content = Content(generated, differentGrant: true);
        var choices = CampaignState.Restore(Snapshot(content, generated), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(choices, 1);
        var selected = choices.Capture();
        Assert.Equal(selected.NextIds.GetValueOrDefault("CHOICE_A_MARKER", 1) != 1, selected.CompletedResearch.Contains("BONUS"));
        Assert.Empty(selected.World.MissionSites);
        Assert.Equivalent(selected, Reload(content, choices, 17, "choice-different-grants.sav").Capture(), strict: true);
        content = Content(generated, mixedDuration: true);
        var campaign = CampaignState.Restore(Snapshot(content, generated), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.All(after.World.MissionSites, site => Assert.Equal(1800, site.SecondsRemaining));
        Assert.Equal(after.World.MissionSites.Count != 2, after.CompletedResearch.Contains("BONUS"));
        Assert.Equivalent(after, Reload(content, campaign, 17, "choice-mixed-duration.sav").Capture(), strict: true);
        content = Content(generated, eventPayload: true);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, generated), content, new SplitMix64RandomSource(17)),
            "Mission site despawn requires research or event payloads.");
    }

    internal static RuntimeContent Content(bool generated, bool differentGrant = false, bool mixedDuration = false,
        bool eventPayload = false, bool ownEvents = false, bool nullChoice = false) => StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
          - {name: BONUS, cost: 1}
        globe:
          textures:
            - {id: 900, deployments: {{{(nullChoice ? "{CHOICE_A: 1, CHOICE_B: 0, \"\": 3}" : "{CHOICE_A: 1, CHOICE_B: 3}")}}}}
        regions:
          - type: CHOICE_REGION
            missionZones:
              - [[11.459155902616464, 11.459155902616464, 5.7295779513082321, 5.7295779513082321, 900]]
        alienMissions:
          - type: CHOICE_RESEARCH_MISSION
            objective: 3
            spawnZone: 0
            regionWeights: {0: {CHOICE_REGION: 1}}
            waves:
              - {count: 2, trajectory: TRAJ_PATROL, objective: true, timer: 0}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: CHOICE_A
            markerName: CHOICE_A_MARKER
            duration: [0, 0]
            unlockedResearchOnDespawn: BONUS
            counterAll: CHOICE_A_COUNTER
        {{{(ownEvents ? "    despawnEvents: {OWN_A: 1}" : "")}}}
          - type: CHOICE_B
            markerName: CHOICE_B_MARKER
            duration: [0, {{{(mixedDuration ? 1 : 0)}}}]
            unlockedResearchOnDespawn: {{{(differentGrant ? "CORE" : "BONUS")}}}
            counterAll: CHOICE_B_COUNTER
        {{{(eventPayload ? "    despawnEvents: {CHOICE_EVENT: 1}" : ownEvents ? "    despawnEvents: {OWN_B: 1}" : "")}}}
          - type: RESEARCH_DIRECT_BASE
            markerName: RESEARCH_DIRECT_BASE_MARKER
            genMission: {CHOICE_RESEARCH_MISSION: 1}
            genMissionFreq: {{{(generated ? 100 : 0)}}}
            genMissionLimit: 1
        events:
          - {name: CHOICE_EVENT, interruptResearch: UNLOCK}
          - {name: OWN_A, interruptResearch: BONUS}
          - {name: OWN_B, interruptResearch: {{{(differentGrant ? "CORE" : "BONUS")}}}}
        """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, bool generated)
    {
        if (generated) return StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content);
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial with
        {
            RandomState = 17,
            Bases = [initial.Bases[0] with { Research = [] }],
            World = initial.World with
            { Missions = [new(4, "CHOICE_RESEARCH_MISSION", "CHOICE_REGION", "RACE_A", 0, 0, 0, 0, 0)] },
        };
    }
}
