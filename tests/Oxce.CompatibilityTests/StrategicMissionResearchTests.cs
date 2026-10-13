using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionResearchTests
{
    [Fact]
    public void MissionGrantsMatchNativeDiaryAndImmediateExplicitInterruption()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "executionRows").Where(row => row.GetProperty("mission").GetBoolean()))
        {
            var scenario = row.GetProperty("scenario").GetInt32();
            var content = Content(scenario);
            var initial = DailySnapshot(content);
            var snapshot = initial with
            {
                Time = new(2, 2, 1, 1999, 0, 0, 0),
                DebugMode = row.GetProperty("debug").GetBoolean(),
                ResearchRuleStatus = scenario == 3 ? new Dictionary<string, int> { ["CORE"] = 2 } : new Dictionary<string, int>(),
                World = initial.World with
                {
                    Missions = [new(1, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1),
                        new(2, "MISSION_LANDING", "REGION", "RACE_A", 0, 0, 9000, 0, -1),
                        new(3, "MISSION_SITE", "REGION", "RACE_A", 0, 0, 9000, 0, -1)],
                },
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            campaign = Reload(content, campaign, 17, "mission-research-before.sav");
            Assert.Null(campaign.MissionResearch.GrantReason("CORE"));
            Assert.True(campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
            AssertDiary(row, campaign);
            var after = campaign.Capture();
            Assert.Equal(row.GetProperty("interrupted").EnumerateArray().Select(value => value.GetBoolean()),
                after.World.Missions.Select(mission => mission.Interrupted));
            Assert.Single(after.Bases[0].Research); // Mission grants do not remove laboratory projects.
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "mission-research-after.sav");
                AssertDiary(row, campaign);
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissionResearchInterruptsExplicitTopicsIncludingDisabledOnesWithoutPrimaryEffects(bool disabled)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, points: 3, getOneFree: [BONUS], sequentialGetOneFree: true, lookup: LOOKUP, spawnedItem: SUPPLY, spawnedEvent: EVENT_DESPAWN, increaseCounters: [RESEARCH_COUNTER]}
              - {name: LOOKUP, cost: 1}
              - {name: BONUS, cost: 1, points: 2}
              - {name: AUTO, cost: 0, dependencies: [BONUS]}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: CORE}
              - {type: MISSION_LANDING, interruptResearch: BONUS}
              - {type: MISSION_SITE, interruptResearch: AUTO}
            """);
        var snapshot = DailySnapshot(content);
        snapshot = snapshot with
        {
            ResearchRuleStatus = disabled ? new Dictionary<string, int> { ["CORE"] = 2 } : new Dictionary<string, int>(),
            World = snapshot.World with
            {
                Missions = [new(1, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1),
                    new(2, "MISSION_LANDING", "REGION", "RACE_A", 0, 0, 9000, 0, -1),
                    new(3, "MISSION_SITE", "REGION", "RACE_A", 0, 0, 9000, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Null(campaign.MissionResearch.GrantReason("CORE"));
        Assert.True(campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
        var after = campaign.Capture();
        Assert.Equal([true, true, false], after.World.Missions.Select(mission => mission.Interrupted));
        Assert.Contains("AUTO", after.CompletedResearch);
        Assert.Equal(disabled ? 2 : 5, after.ResearchScores[^1]);
        Assert.Equivalent(snapshot.Bases, after.Bases, strict: true);
        Assert.Equivalent(snapshot.NextIds, after.NextIds, strict: true);
        Assert.Empty(after.World.Events);
        Reload(content, campaign, 17, "explicit-interruption.sav");
    }

    [Fact]
    public void MissionBonusIsSelectedAfterCoreSatisfiesItsProtectedPrerequisite()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - name: CORE
                cost: 1
                getOneFreeProtected: {CORE: [BONUS]}
              - {name: BONUS, cost: 1}
            """);
        var snapshot = CreateSnapshot(content, "Post-core protected reward");
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Null(campaign.MissionResearch.GrantReason("CORE"));
        campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY");
        Assert.Equal(["CORE", "BONUS"], campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));
        Assert.Equal((byte)ResearchDiscoverySource.FreeFrom, campaign.Capture().ResearchDiary[1].SourceType);
        Assert.Equal("CORE", campaign.Capture().ResearchDiary[1].SourceName);
    }

    [Fact]
    public void MissionCapacityAndRandomRewardForecastsAreReadOnly()
    {
        var content = Content(0);
        var snapshot = DailySnapshot(content) with { ResearchDiarySequence = ulong.MaxValue - 3 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Contains("diary", campaign.MissionResearch.GrantReason("CORE")!, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            """);
        campaign = CampaignState.Restore(CreateSnapshot(content, "Uncertain mission research"), content, new SplitMix64RandomSource(17));
        before = campaign.Capture();
        Assert.Null(campaign.MissionResearch.GrantReason("CORE"));
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.Advance("CORE"));
        Assert.Equal(["CORE"], forecast.GuaranteedResearch.Order(StringComparer.Ordinal));
        Assert.Equal(["BONUS", "CORE", "OTHER"], forecast.PossibleResearch.Order(StringComparer.Ordinal));
        Assert.Equal(["CORE"], forecast.GuaranteedExplicitTopics.Order(StringComparer.Ordinal));
        Assert.Equal(["BONUS", "CORE", "OTHER"], forecast.PossibleExplicitTopics.Order(StringComparer.Ordinal));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY");
        Assert.Equal(2, campaign.Capture().ResearchDiary.Count);
        Reload(content, campaign, 17, "random-mission-reward.sav");
    }

    [Fact]
    public void MissingResearchIsANativeNoOpAndEmptyCampaignsRemainInvalid()
    {
        var content = Content(0);
        var snapshot = CreateSnapshot(content, "No research grant");
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Null(campaign.MissionResearch.GrantReason("MISSING"));
        Assert.False(campaign.MissionResearch.Grant("MISSING", "DEPLOY_DIARY"));
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
        // A campaign that has not started still needs a base; a started one may have lost its last base
        // (branch 5 S7), which ends it on the next tick.
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with { Bases = [], MonthsPassed = -1 }, content,
            new SplitMix64RandomSource(17)));
    }

    [Theory]
    [InlineData(2147483647, 1, -1)]
    [InlineData(-2147483648, -1, 1)]
    public void MissionResearchScorePrefixCannotOverflowEvenWhenLaterRewardWouldCancelIt(int score, int corePoints, int bonusPoints)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, points: {{corePoints}}, getOneFree: [BONUS], sequentialGetOneFree: true}
              - {name: BONUS, cost: 1, points: {{bonusPoints}}}
            """);
        var snapshot = CreateSnapshot(content, "Mission score prefixes") with { ResearchScores = [score] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Equal("Mission research score exceeds the supported range.", campaign.MissionResearch.GrantReason("CORE"));
        Assert.Throws<InvalidOperationException>(() => campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(100, true)]
    public void MissionEligibilityChargesGeographyListsBeforeExpensiveQueries(int candidates, bool blocked)
    {
        var functions = string.Join(", ", Enumerable.Range(0, 120).Select(index => "DIARY_FUNCTION_" + index));
        var areas = string.Join(", ", Enumerable.Repeat("[180, 181, 20, 21]", 256));
        var extra = "regions:\n" + string.Concat(Enumerable.Range(0, 32).Select(index =>
            $"  - {{type: DIARY_REGION_{index}, areas: [{areas}], provideBaseFunc: [{functions}]}}\n")) +
            "research:\n  - {name: UNLOCK, cost: 1}\n  - {name: CORE, cost: 1}\n" +
            string.Concat(Enumerable.Range(0, candidates).Select(index =>
                $"  - {{name: AUTO_{index}, cost: 0, dependencies: [CORE], requiresBaseFunc: [DIARY_MISSING]}}\n"));
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extra);
        var snapshot = CreateSnapshot(content, "Mission function work");
        var campaign = CampaignState.Restore(snapshot with
        {
            Countries = [],
            Regions = snapshot.Regions.Where(region => region.RuleId.StartsWith("DIARY_REGION_", StringComparison.Ordinal)).ToArray(),
        }, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var reason = campaign.MissionResearch.GrantReason("CORE");
        if (blocked)
        {
            Assert.Equal("Mission research exceeds the supported discovery work limit.", reason);
            Assert.Throws<InvalidOperationException>(() => campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
        else
        {
            Assert.Null(reason);
            campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY");
            Assert.Equal("CORE", Assert.Single(campaign.Capture().ResearchDiary).RuleId);
        }
    }

    [Fact]
    public void OrderedRandomGrantOutcomesBoundPopulationAndScoreBeforeAnySelection()
    {
        var rewards = string.Join(", ", Enumerable.Range(0, 32).Select(index => "REWARD_" + index));
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $"research:\n  - {{name: UNLOCK, cost: 1}}\n  - {{name: CORE, cost: 1, getOneFree: [{rewards}]}}\n" +
            string.Concat(Enumerable.Range(0, 32).Select(index => $"  - {{name: REWARD_{index}, cost: 1}}\n")));
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Bounded research alternatives"), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.Advance("CORE"));
        Assert.Equal("Mission research exceeds the supported outcome limit.", forecast.Advance("CORE"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(["CORE"], forecast.GuaranteedResearch.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnsafeRandomRewardAlternativeBlocksEvenWhenOtherChoiceFits()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [SAFE, OVERFLOW]}
              - {name: SAFE, cost: 1, points: 0}
              - {name: OVERFLOW, cost: 1, points: 1}
            """);
        var snapshot = CreateSnapshot(content, "Unsafe research outcome") with { ResearchScores = [int.MaxValue] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Equal("Mission research score exceeds the supported range.", campaign.MissionResearch.GrantReason("CORE"));
        Assert.Throws<InvalidOperationException>(() => campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void RandomAlternativesMatchNativeDuplicateProtectedAndDisabledCandidateSelection()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "randomRows"))
        {
            var scenario = row.GetProperty("scenario").GetInt32();
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
                research:
                  - {name: UNLOCK, cost: 1}
                  - {name: CORE, cost: 1, repeatable: {{{(scenario == 3 ? "true" : "false")}}}, getOneFree: [BONUS, OTHER, BONUS], getOneFreeProtected: {CORE: [PROTECTED]}}
                  - {name: BONUS, cost: 1}
                  - {name: OTHER, cost: 1}
                  - {name: PROTECTED, cost: 1}
                """);
            var initial = CreateSnapshot(content, "Native random research choices");
            var snapshot = initial with
            {
                CompletedResearch = scenario == 2 ? ["BONUS"] : [],
                ResearchRuleStatus = scenario == 1 ? new Dictionary<string, int> { ["BONUS"] = 2 } : new Dictionary<string, int>(),
            };
            var choices = row.GetProperty("choices").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            var before = campaign.Capture();
            var forecast = campaign.MissionResearch.CreateForecast();
            Assert.Null(forecast.Advance("CORE"));
            Assert.Equal(choices.Append("CORE").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                forecast.PossibleExplicitTopics.Order(StringComparer.Ordinal));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            for (ulong seed = 0; seed < 8; seed++)
            {
                campaign = CampaignState.Restore(snapshot with { RandomState = seed }, content, new SplitMix64RandomSource(seed));
                campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY");
                var reward = Assert.Single(campaign.Capture().ResearchDiary, entry => entry.SourceType == (byte)ResearchDiscoverySource.FreeFrom);
                Assert.Contains(reward.RuleId, choices);
                Reload(content, campaign, seed, "native-random-research.sav");
            }
        }
    }

    [Fact]
    public void OrderedGrantsReserveRepeatedMissionValidationAndInterruptionPasses()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1}
            """);
        var initial = CreateSnapshot(content, "Repeated mission-link work");
        var snapshot = initial with
        {
            World = initial.World with
            {
                Missions = Enumerable.Range(1, 1001).Select(id =>
                    new AlienMissionSnapshot(id, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1)).ToArray(),
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        for (var index = 0; index < 100; index++) Assert.Null(forecast.Advance("CORE"));
        string? reason = null;
        for (var index = 100; index < 600 && reason is null; index++) reason = forecast.Advance("CORE");
        Assert.Equal("Mission research exceeds the supported discovery work limit.", reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
