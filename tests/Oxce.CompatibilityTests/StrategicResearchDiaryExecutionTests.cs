using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicResearchDiaryExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyDiaryOrderSourcesAndDebugBehaviorMatchNativeCaller(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "executionRows").Where(row => !row.GetProperty("mission").GetBoolean()))
        {
            var scenario = row.GetProperty("scenario").GetInt32();
            var content = Content(cache, scenario);
            var snapshot = DailySnapshot(content) with
            {
                DebugMode = row.GetProperty("debug").GetBoolean(),
                ResearchRuleStatus = scenario == 3 ? new Dictionary<string, int> { ["CORE"] = 2 } : new Dictionary<string, int>(),
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            campaign = Reload(content, campaign, 17, "diary-before.sav");
            AdvanceUnblocked(campaign, 1);
            AssertDiary(row, campaign);
            var after = campaign.Capture();
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "diary-after.sav");
                AssertDiary(row, campaign);
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyDiaryCapacityStopsBeforeAnyTickMutation(bool identityLimit)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(false, 0);
        var snapshot = DailySnapshot(content);
        var diary = identityLimit ? Array.Empty<ResearchDiaryEntrySnapshot>() :
            Enumerable.Range(0, CampaignState.MaximumResearchDiaryEntries).Select(index =>
                new ResearchDiaryEntrySnapshot("CORE", 1998, 1, 1, 0, "Old base") { PreservationKey = "old:" + index }).ToArray();
        var campaign = CampaignState.Restore(snapshot with
        {
            ResearchDiary = diary,
            ResearchDiarySequence = identityLimit ? ulong.MaxValue - 3 : 0,
        }, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        var blocked = Assert.Single(result.Events.OfType<CampaignActionBlocked>());
        Assert.Contains("Research diary", blocked.Reason, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void DebugDailyCompletionCanProceedWithNoDiaryIdentityHeadroom()
    {
        var content = Content(false, 0);
        var campaign = CampaignState.Restore(DailySnapshot(content) with
        { DebugMode = true, ResearchDiarySequence = ulong.MaxValue }, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().ResearchDiary);
        Assert.Contains("CORE", campaign.Capture().CompletedResearch);
    }

    [Fact]
    public void AutomaticDiscoveriesCanRepeatWithRepeatableParent()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: PARENT, cost: 1, repeatable: true}
              - {name: CHILD, cost: 0}
              - {name: GRANDCHILD, cost: 0, dependencies: [CHILD]}
            """);
        var snapshot = CreateSnapshot(content, "Repeated automatic diary");
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        for (var pass = 0; pass < 2; pass++)
        {
            var before = campaign.Capture();
            campaign = CampaignState.Restore(before with
            {
                Time = new(1, 1 + pass, 1, 1999, 23, 59, 55),
                Bases = [before.Bases[0] with { Research = [new("PARENT", 0, 1, 1)] }],
            }, content, new SplitMix64RandomSource(before.RandomState));
            AdvanceUnblocked(campaign, 1);
            campaign = Reload(content, campaign, 17, "repeat-diary.sav");
        }
        Assert.Equal(["PARENT", "CHILD", "PARENT", "CHILD"], campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));
        Assert.All(campaign.Capture().ResearchDiary.Where(entry => entry.RuleId == "CHILD"), entry =>
        {
            Assert.Equal((byte)ResearchDiscoverySource.FreeAfter, entry.SourceType);
            Assert.Equal("PARENT", entry.SourceName);
        });
        Assert.Empty(campaign.Capture().CompletedResearch);
    }

    [Fact]
    public void NestedAutomaticDiscoveriesKeepInitialSourceRatherThanQueueParent()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: PARENT, cost: 1}
              - {name: CHILD, cost: 0, dependencies: [PARENT]}
              - {name: GRANDCHILD, cost: 0, dependencies: [CHILD]}
            """);
        var snapshot = CreateSnapshot(content, "Nested automatic diary");
        var campaign = CampaignState.Restore(snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [new("PARENT", 0, 1, 1)] }],
        }, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(["PARENT", "CHILD", "GRANDCHILD"], campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));
        Assert.All(campaign.Capture().ResearchDiary.Skip(1), entry =>
        {
            Assert.Equal((byte)ResearchDiscoverySource.FreeAfter, entry.SourceType);
            Assert.Equal("PARENT", entry.SourceName);
        });
        Reload(content, campaign, 17, "nested-diary.sav");
    }

    internal static RuntimeContent Content(bool cache, int scenario, string extraRules = "") => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1, lookup: LOOKUP, getOneFree: [BONUS], sequentialGetOneFree: true, repeatable: {{(scenario == 2 ? "true" : "false")}}}
          - {name: LOOKUP, cost: 1}
          - {name: BONUS, cost: 1, lookup: BONUS_LOOKUP}
          - {name: BONUS_LOOKUP, cost: {{(scenario == 1 ? 0 : 1)}}, dependencies: [BONUS]}
          - {name: AUTO, cost: {{(scenario == 2 ? 0 : 1)}}, dependencies: [BONUS]}
        alienMissions:
          - {type: MISSION_SCOUT, interruptResearch: CORE}
          - {type: MISSION_LANDING, interruptResearch: BONUS}
          - {type: MISSION_SITE, interruptResearch: AUTO}
        """ + "\n" + extraRules);

    internal static CampaignSnapshot DailySnapshot(RuntimeContent content)
    {
        var snapshot = CreateSnapshot(content, "Daily research diary");
        return snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [new("CORE", 0, 1, 1)] }],
        };
    }

    internal static void AssertDiary(JsonElement row, CampaignState campaign)
    {
        var entries = campaign.Capture().ResearchDiary;
        var actual = JsonSerializer.SerializeToUtf8Bytes(entries.Select(entry => new
        { name = entry.RuleId, date = new[] { entry.Year, entry.Month, entry.Day }, sourceType = entry.SourceType, sourceName = entry.SourceName }));
        Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetProperty("diary").GetRawText())), CanonicalJson.Normalize(actual));
        Assert.Equal(entries.Count, entries.Select(entry => entry.PreservationKey).Distinct(StringComparer.Ordinal).Count());
    }
}
