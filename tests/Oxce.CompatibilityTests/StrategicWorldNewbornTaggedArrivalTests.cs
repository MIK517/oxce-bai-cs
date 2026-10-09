using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornTaggedArrivalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ResearchTagsDoNotBlockBirthsWithoutARelevantLaterGrant(bool cache, bool unrelatedGrant)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = Content(cache, "CORE", unrelatedGrant ? "OTHER" : "");
        var controlContent = Content(cache, "", unrelatedGrant ? "OTHER" : "");
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed, unrelatedGrant);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var control = CampaignState.Restore(initial, controlContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            var after = campaign.Capture();
            Assert.Equal(3, after.World.Ufos.Count);
            Assert.All(after.World.Ufos, ufo => Assert.Equal(1, ufo.TrajectoryPoint));
            Assert.False(Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(unrelatedGrant, after.CompletedResearch.Contains("OTHER"));
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-harmless-tag.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RelevantPostBirthGrantRetainsTheTerminalArrivalBoundary(bool cache, bool optional)
    {
        var content = Content(cache, optional ? "BONUS" : "CORE", "CORE");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, true), content, new SplitMix64RandomSource(17)),
            "Alien mission wave spawning requires world simulation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyOptionalInterruptionEitherOmitsTheBirthOrPermitsItsOrdinaryArrival(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = Content(cache, "BONUS", "");
        var interruptedContent = Content(cache, "CORE", "");
        var continuedContent = Content(cache, "", "");
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, false);
            initial = initial with
            {
                Time = new(2, 2, 1, 1999, 23, 59, 55),
                Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)], Soldiers = [] }],
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(interrupted ? 0 : 3, after.World.Ufos.Count);
            Assert.Equal(interrupted ? 0 : 1, after.World.Missions.Count);
            Assert.All(after.World.Ufos, ufo => Assert.Equal(1, ufo.TrajectoryPoint));
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-daily-interruption.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    private static RuntimeContent Content(bool cache, string interrupt, string grant) =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, interrupt, extraRules: $$$"""
            research:
              - {name: CORE, cost: 1, getOneFree: [BONUS, EXTRA]}
              - {name: OTHER, cost: 1}
              - {name: BONUS, cost: 1}
              - {name: EXTRA, cost: 1}
            alienDeployments:
              - {type: FIRST_TAG_SITE, markerName: FIRST_TAG, points: 0, duration: [0, 0], unlockedResearchOnDespawn: {{{grant}}}}
            """);

    private static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool site)
    {
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
        return site ? initial with
        { World = initial.World with { MissionSites = [new(7, "MISSION_SITE", "FIRST_TAG_SITE", "RACE_A", Degrees(8), Degrees(2), 0)] } } : initial;
    }
}
