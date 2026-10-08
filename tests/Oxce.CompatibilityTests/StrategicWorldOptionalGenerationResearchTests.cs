using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalGenerationResearchTests
{
    private static readonly string[] Topics = ["CORE", "BONUS"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalGenerationKeepsItsWholeOrderedGrantSequenceAndConditionalEvents(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = Content(cache);
        var generatedSeen = false;
        var skippedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content) with { RandomState = seed };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "optional-generation-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var generated = Assert.Single(after.World.AlienBases).GenMissionCount != 0;
            Assert.Equal(generated, after.CompletedResearch.Contains("CORE"));
            Assert.Equal(generated, after.CompletedResearch.Contains("BONUS"));
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.Events);
            if (generated) Assert.Equal(Topics, after.ResearchDiary.Select(entry => entry.RuleId));
            else Assert.Empty(after.ResearchDiary);
            Assert.Equal(generated ? 2 : 1, after.NextIds.GetValueOrDefault("FIRST_MARKER", 1));
            Assert.Equal(generated ? 2 : 1, after.NextIds.GetValueOrDefault("SECOND_MARKER", 1));
            generatedSeen |= generated;
            skippedSeen |= !generated;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-generation-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.True(generatedSeen);
        Assert.True(skippedSeen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalSequenceForecastPreservesCorrelationAndCampaignState(bool cache)
    {
        var content = Content(cache);
        var campaign = CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.AdvanceOptionalSequence(() =>
        {
            if (forecast.Advance("CORE") is { } reason) return reason;
            Assert.Contains("CORE", forecast.GuaranteedResearch);
            if (forecast.Advance("BONUS") is { } bonusReason) return bonusReason;
            Assert.Contains("CORE", forecast.GuaranteedResearch);
            Assert.Contains("BONUS", forecast.GuaranteedResearch);
            return null;
        }));
        Assert.DoesNotContain("CORE", forecast.GuaranteedResearch);
        Assert.DoesNotContain("BONUS", forecast.GuaranteedResearch);
        Assert.Contains("CORE", forecast.PossibleResearch);
        Assert.Contains("BONUS", forecast.PossibleResearch);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalGenerationAndRandomDurationComposeWithoutInventingGrants(bool cache)
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, duration: 0, maximumDuration: 1, generated: true,
            frequency: 50, interrupt: "BONUS", eventInterrupt: "BONUS");
        var generatedSeen = false;
        var skippedSeen = false;
        var expiredSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var generated = Assert.Single(after.World.AlienBases).GenMissionCount == 1;
            var expired = generated && after.World.MissionSites.Count != 2;
            Assert.Equal(expired, after.CompletedResearch.Contains("BONUS"));
            Assert.Equal(expired ? 2 : 1, after.ResearchDiary.Count);
            Assert.Equal("CORE", after.ResearchDiary[0].RuleId);
            Assert.Empty(after.World.Events);
            Assert.All(after.World.MissionSites, site => Assert.Equal(1800, site.SecondsRemaining));
            if (generated) Assert.Equal(expired, Assert.Single(after.World.Missions).Interrupted);
            else Assert.Empty(after.World.Missions);
            Assert.Equivalent(after, Reload(content, campaign, seed, "optional-generation-duration.sav").Capture(), strict: true);
            generatedSeen |= generated;
            skippedSeen |= !generated;
            expiredSeen |= expired;
        }
        Assert.True(generatedSeen);
        Assert.True(skippedSeen);
        Assert.True(expiredSeen);
    }

    internal static RuntimeContent Content(bool cache) => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
          - {name: BONUS, cost: 1}
        alienMissions:
          - type: OPTIONAL_RESEARCH_MISSION
            waves:
              - {ufo: FIRST_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}
              - {ufo: SECOND_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + """

          - {type: FIRST_SITE, markerName: FIRST_MARKER, duration: [0, 0], unlockedResearchOnDespawn: CORE}
          - {type: SECOND_SITE, markerName: SECOND_MARKER, duration: [0, 0], unlockedResearchOnDespawn: BONUS, despawnEvents: {AFTER_FIRST: 1}}
          - type: RESEARCH_DIRECT_BASE
            markerName: RESEARCH_DIRECT_BASE_MARKER
            genMission: {OPTIONAL_RESEARCH_MISSION: 1}
            genMissionFreq: 50
            genMissionLimit: 1
        events:
          - {name: AFTER_FIRST, interruptResearch: CORE}
        """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content);
        return initial with { World = initial.World with { MissionSites = [] } };
    }
}
