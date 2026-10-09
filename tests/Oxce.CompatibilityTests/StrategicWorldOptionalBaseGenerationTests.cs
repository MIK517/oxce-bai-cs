using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseGenerationTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void OptionalSourceDeletionComposesDailyAndHuntingGenerationInCallerOrder(bool cache, int generation)
    {
        using var daily = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        using var hunting = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-generation");
        Assert.NotEmpty(TestFixtures.Rows(daily.RootElement, "rows"));
        Assert.NotEmpty(TestFixtures.Rows(hunting.RootElement, "rows"));
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        for (var kind = 0; kind < 6; kind++)
        {
            var content = Content(cache, kind, generation);
            var deletedContent = Content(cache, kind, generation, "CORE");
            var survivedContent = Content(cache, kind, generation, "");
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = Snapshot(content, seed, kind, generation);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                var source = after.World.AlienBases.SingleOrDefault(alienBase => alienBase.Id == 6);
                Assert.Equal(deleted, source is null);
                if (source is not null)
                {
                    Assert.Equal(generation is 1 or 3 ? 1 : 0, source.GenMissionCount);
                    Assert.Equal(generation >= 2 ? 0 : 50, source.MinutesSinceLastHuntMissionGeneration);
                }
                if (deleted)
                {
                    Assert.Empty(after.World.Missions);
                    Assert.Empty(after.World.Ufos);
                    Assert.Empty(after.World.MissionSites);
                    Assert.Equal(1, after.NextIds.GetValueOrDefault("ALIEN_MISSIONS", 1));
                }
                else
                {
                    Assert.Equal(generation == 3 ? 3 : 2, after.NextIds["ALIEN_MISSIONS"]);
                    Assert.All(after.World.Missions, mission => Assert.Equal(6, mission.AlienBase!.Id));
                    Assert.All(after.World.Missions, mission => Assert.False(mission.Interrupted));
                }
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "optional-source-generation.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void PossibleGenerationKeepsMissionAndTargetCapacityChecksBeforeDailyMutation(bool cache, int generation)
    {
        var content = Content(cache, 0, generation);
        var initial = Snapshot(content, 17, 0, generation).WithNextId("ALIEN_MISSIONS", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission IDs or population exceed the supported limit.");
        content = Content(cache, 3, generation);
        initial = Snapshot(content, 17, 3, generation).WithNextId("DAILY_BASE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
        content = Content(cache, 5, generation);
        initial = Snapshot(content, 17, 5, generation).WithNextId("DAILY_SITE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void ImmediateGeneratedResearchRetainsItsOrderedDailyProjectionBoundary(bool cache, int generation)
    {
        var content = StrategicWorldDailyWaveResearchTests.Content(cache, 5, interrupt: "", operation: 1,
            generation: generation, immediateResearch: true);
        var initial = Snapshot(content, 17, 5, generation);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Mission site research requires deterministic daily research phase forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HuntingZeroDurationResearchWaitsForTheFollowingSiteCaller(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicWorldDailyWaveResearchTests.Content(cache, 5, interrupt: "", operation: 1,
            generation: 2, immediateResearch: true);
        var deletedContent = StrategicWorldDailyWaveResearchTests.Content(cache, 5, interrupt: "", operation: 1,
            sourceCode: "CORE", generation: 2, immediateResearch: true);
        var survivedContent = StrategicWorldDailyWaveResearchTests.Content(cache, 5, interrupt: "", operation: 1,
            sourceCode: "", generation: 2, immediateResearch: true);
        var generatedSeen = false;
        var deletedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, 5, 2);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(deleted ? 0 : 1, after.World.MissionSites.Count);
            Assert.All(after.World.MissionSites, site => Assert.Equal(0, site.SecondsRemaining));
            // The logistics fixture also automatically discovers zero-cost UNLOCK.
            Assert.Equal(initial.ResearchDiary.Count + 3, after.ResearchDiary.Count);
            Assert.Single(after.ResearchDiary, entry => entry.RuleId == "UNLOCK");
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-hunting-zero-research.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 360);
            AdvanceUnblocked(control, 360);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            Assert.Contains("BONUS", campaign.Capture().CompletedResearch);
            Assert.Empty(campaign.Capture().World.MissionSites);
            generatedSeen |= !deleted;
            deletedSeen |= deleted;
        }
        Assert.True(generatedSeen);
        Assert.True(deletedSeen);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void DelayedGenerationAndHuntingWithoutEligibleCraftKeepOnlySurvivingSourceState(bool cache, int activity)
    {
        var content = StrategicWorldDailyBaseResearchTests.Content(cache, activity: activity);
        var deletedContent = StrategicWorldDailyBaseResearchTests.Content(cache, "CORE", activity: activity);
        var survivedContent = StrategicWorldDailyBaseResearchTests.Content(cache, "", activity: activity);
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldDailyBaseResearchTests.Snapshot(content, seed, false);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            Assert.Equal(!deleted && activity == 1 ? 1 : 0, after.World.Missions.Count);
            if (!deleted)
            {
                var source = Assert.Single(after.World.AlienBases, alienBase => alienBase.Id == 6);
                Assert.Equal(activity == 1 ? 1 : 0, source.GenMissionCount);
                Assert.Equal(activity == 2 ? 10 : 0, source.MinutesSinceLastHuntMissionGeneration);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-source-quiet-generation.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    private static RuntimeContent Content(bool cache, int kind, int generation, string code = "BONUS") =>
        StrategicWorldDailyWaveResearchTests.Content(cache, kind, interrupt: "", operation: 1, sourceCode: code, generation: generation);

    private static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int kind, int generation)
    {
        var initial = StrategicWorldDailyWaveResearchTests.Snapshot(content, seed, kind);
        if (generation >= 2) initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 300,
                Speed = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = WorldTargetReference.ForWaypoint(1, new(1, 0.6)),
            },
        });
        return initial with
        {
            World = initial.World with
            {
                Missions = [],
                Waypoints = generation >= 2 ? [new(1, 1, 0.6)] : [],
                AlienBaseBirthSequence = 8,
                AlienBases = [new(6, "OPTIONAL_OPERATION_BASE", "RACE_A", 0.2, 0.1)
                { Discovered = true, PreservationKey = "alien-base:generated:8", MinutesSinceLastHuntMissionGeneration = 50 }],
            },
        };
    }
}
