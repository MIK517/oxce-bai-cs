using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSourceChoiceResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExclusiveSourceChoicesKeepOrderedGrantsAndConditionalEvents(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var variant = 0; variant <= 2; variant++)
        {
            var content = Content(cache, variant);
            var firstSeen = false;
            var otherSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldOptionalGenerationResearchTests.Snapshot(content) with { RandomState = seed };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var first = after.NextIds.GetValueOrDefault("FIRST_MARKER", 1) == 2;
                var other = after.NextIds.GetValueOrDefault("OTHER_MARKER", 1) == 2;
                Assert.Equal(variant == 2 && !first ? 0 : 1, Assert.Single(after.World.AlienBases).GenMissionCount);
                Assert.Equal(first, after.CompletedResearch.Contains("CORE"));
                Assert.Equal(first || other, after.CompletedResearch.Contains("BONUS"));
                Assert.Equal(first ? 2 : other ? 1 : 0, after.ResearchDiary.Count);
                Assert.Equal(first, after.NextIds.GetValueOrDefault("SECOND_MARKER", 1) == 2);
                Assert.Empty(after.World.MissionSites);
                Assert.Empty(after.World.Events);
                if (variant == 1 && !first)
                {
                    var mission = Assert.Single(after.World.Missions);
                    Assert.Equal("OTHER_MISSION", mission.RuleId);
                    Assert.Equal(0, mission.NextWave);
                    Assert.True(mission.SpawnCountdown >= 30);
                }
                else Assert.Empty(after.World.Missions);
                firstSeen |= first;
                otherSeen |= !first;
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "source-choice-research.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
            }
            Assert.True(firstSeen);
            Assert.True(otherSeen);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchosenSourceResearchCannotSuppressDueEvents(bool cache)
    {
        var content = Content(cache);
        var initial = StrategicWorldOptionalGenerationResearchTests.Snapshot(content);
        AssertTimeBlocked(CampaignState.Restore(initial with
        {
            World = initial.World with { Events = [new GeoscapeEventSnapshot("AFTER_FIRST", 30)] },
        }, content, new SplitMix64RandomSource(17)), "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.AdvanceSequenceChoices(new Func<string?>[]
        {
            () => forecast.Advance("CORE"),
            () =>
            {
                Assert.DoesNotContain("CORE", forecast.GuaranteedResearch);
                return forecast.Advance("BONUS");
            },
            () =>
            {
                Assert.DoesNotContain("BONUS", forecast.GuaranteedResearch);
                return null;
            },
        }));
        Assert.Empty(forecast.GuaranteedResearch);
        Assert.Contains("CORE", forecast.PossibleResearch);
        Assert.Contains("BONUS", forecast.PossibleResearch);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExclusiveSequenceOutcomesRemainBoundedWithoutMutatingCampaign(bool cache)
    {
        var content = Content(cache);
        var campaign = CampaignState.Restore(StrategicWorldOptionalGenerationResearchTests.Snapshot(content), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Equal("Mission research exceeds the supported outcome limit.",
            forecast.AdvanceSequenceChoices(Enumerable.Repeat<Func<string?>>(static () => null, 257)));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    internal static RuntimeContent Content(bool cache, int variant = 0) => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
          - {name: BONUS, cost: 1}
        alienMissions:
          - type: FIRST_MISSION
            waves:
              - {ufo: FIRST_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}
              - {ufo: SECOND_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}
          - type: OTHER_MISSION
            waves:
              - {ufo: OTHER_SITE, count: 1, trajectory: TRAJ_PATROL, timer: {{{(variant == 1 ? 120 : 0)}}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - {type: FIRST_SITE, markerName: FIRST_MARKER, duration: [0, 0], unlockedResearchOnDespawn: CORE}
          - {type: SECOND_SITE, markerName: SECOND_MARKER, duration: [0, 0], unlockedResearchOnDespawn: BONUS, despawnEvents: {AFTER_FIRST: 1}}
          - {type: OTHER_SITE, markerName: OTHER_MARKER, duration: [0, 0], unlockedResearchOnDespawn: BONUS, despawnEvents: {AFTER_OTHER: 1}}
          - type: RESEARCH_DIRECT_BASE
            markerName: RESEARCH_DIRECT_BASE_MARKER
            genMission: {FIRST_MISSION: 1, {{{(variant == 2 ? "\"\"" : "OTHER_MISSION")}}}: 1}
            genMissionFreq: 100
            genMissionLimit: 1
        events:
          - {name: AFTER_FIRST, interruptResearch: CORE}
          - {name: AFTER_OTHER, interruptResearch: BONUS}
        """);
}
