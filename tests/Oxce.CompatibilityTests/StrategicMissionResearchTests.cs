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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissionGrantsMatchNativeDiaryAndImmediateExplicitInterruption(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "executionRows").Where(row => row.GetProperty("mission").GetBoolean()))
        {
            var scenario = row.GetProperty("scenario").GetInt32();
            var content = Content(cache, scenario);
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
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
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
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
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
    public void MissionCapacityAndUncertainRewardsAreReadOnlyAndGuardActualGrant()
    {
        var content = Content(false, 0);
        var snapshot = DailySnapshot(content) with { ResearchDiarySequence = ulong.MaxValue - 3 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Contains("diary", campaign.MissionResearch.GrantReason("CORE")!, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            """);
        campaign = CampaignState.Restore(CreateSnapshot(content, "Uncertain mission research"), content, new SplitMix64RandomSource(17));
        before = campaign.Capture();
        Assert.Equal("Mission research depends on uncertain reward outcomes.", campaign.MissionResearch.GrantReason("CORE"));
        Assert.Throws<InvalidOperationException>(() => campaign.MissionResearch.Grant("CORE", "DEPLOY_DIARY"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void MissingResearchIsANativeNoOpAndEmptyCampaignsRemainInvalid()
    {
        var content = Content(false, 0);
        var snapshot = CreateSnapshot(content, "No research grant");
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Null(campaign.MissionResearch.GrantReason("MISSING"));
        Assert.False(campaign.MissionResearch.Grant("MISSING", "DEPLOY_DIARY"));
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with { Bases = [] }, content, new SplitMix64RandomSource(17)));
    }

    [Theory]
    [InlineData(2147483647, 1, -1)]
    [InlineData(-2147483648, -1, 1)]
    public void MissionResearchScorePrefixCannotOverflowEvenWhenLaterRewardWouldCancelIt(int score, int corePoints, int bonusPoints)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, $$"""
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
}
