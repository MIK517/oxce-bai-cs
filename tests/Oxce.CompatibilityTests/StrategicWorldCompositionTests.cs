using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldCompositionTests
{
    [Fact]
    public void DependentEventEligibilityCoversCapturedResearchAndMissionEffects()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-composition");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            if (row.GetProperty("failed").GetBoolean()) continue;
            var month = row.GetProperty("month").GetInt32();
            var zero = row.GetProperty("case").GetString() == "zero-odds";
            var context = new WorldScriptContext(month, 0, 0, 1000, false,
                new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
                new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
                new HashSet<string>(), new Dictionary<string, int>(), new Dictionary<string, int>());
            var criteria = new RuntimeScriptCriteria(0, -1, 100, 0, 4,
                int.MinValue, int.MaxValue, long.MinValue, long.MaxValue, "RUN", "", 2, -1,
                [new(new("R3", null), true)], [], [], [], [], [], []);
            var possible = WorldScriptEligibility.MayMatch(criteria, context,
                new HashSet<string>(zero ? [] : ["R1", "R2", "R3"], StringComparer.Ordinal),
                new Dictionary<string, long> { ["RUN"] = zero ? 0 : 2 }, new Dictionary<string, long>());
            Assert.Equal(row.GetProperty("events").GetInt32() != 0, possible);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposedSchedulingMatchesReferenceDependenciesAndPersists(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-composition");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var name = row.GetProperty("case").GetString();
            if (name == "initial") continue;
            var extra = name switch
            {
                "zero-odds" => "arcScripts: [{type: A1, executionOdds: 0}, {type: A2, executionOdds: 0}]",
                "disable" => "missionScripts: [{type: M1_SCRIPT, researchTriggers: {OLD: false}}]",
                "missing-event" => null,
                _ => null,
            };
            if (name != "missing-event") extra = "events: [{name: E, timer: 60}]\n" + extra;
            var content = StrategicReadinessTestContent.Load(
                ["strategic-world.rul", "strategic-world-composition.rul"], fromCache, extra);
            if (name == "missing-event") Assert.False(content.RuntimeRules.Events.TryGet("E", out _));
            var snapshot = CreateSnapshot(content, "Composed world scripts") with
            {
                MonthsPassed = 1, RandomState = 17, Funds = [1000], ResearchScores = [0],
                CompletedResearch = name == "disable" ? ["OLD"] : [],
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            var result = campaign.Execute(new ScheduleWorldScripts());
            if (row.GetProperty("failed").GetBoolean())
            {
                Assert.Contains("E", Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Reason);
                Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
                continue;
            }
            var after = campaign.Capture();
            var trace = result.Events.Cast<CampaignArcUnlocked>().Select(entry => "article:" + entry.ArticleId)
                .Concat(after.World.Missions.Select(entry => entry.RuleId == "MISSION_SCOUT" ? "mission:M1" : "mission:M2"))
                .Concat(after.World.Events.Select(entry => "event:" + entry.RuleId)).ToArray();
            var expectedTrace = row.GetProperty("trace").EnumerateArray().Select(entry => entry.GetString()!)
                .Where(entry => entry.StartsWith("article:", StringComparison.Ordinal) ||
                    entry.StartsWith("mission:", StringComparison.Ordinal) || entry.StartsWith("event:", StringComparison.Ordinal));
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expectedTrace)),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(trace)));
            Assert.Equal(row.GetProperty("runs").GetInt32(), after.World.Strategy.MissionRuns.SingleOrDefault(pair => pair.Key == "RUN").Value);
            Assert.Equal(row.GetProperty("events").GetInt32(), after.World.GeneratedEvents.GetValueOrDefault("E"));
            Assert.Equal(snapshot.Funds, after.Funds); // Eligibility funds are never paid out here.
            Assert.Equal(snapshot.MonthsPassed, after.MonthsPassed);
            campaign = Reload(content, campaign, 17, "composed-world-scripts.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalBasePhaseIsPreflightedBeforeResearchOrEventsAndHonorsNewMonth(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-world-composition.rul"], fromCache,
            "events: [{name: E, timer: 60}]\nalienDeployments: [{type: BAD_MONTHLY, markerName: BAD_MONTHLY, alienBase: true, alienBaseUpgrades: {99: {BAD_MONTHLY: 1}}}]");
        var snapshot = CreateSnapshot(content, "Final base phase preflight") with { MonthsPassed = 1, RandomState = 17 };
        snapshot = snapshot with { World = snapshot.World with
        { AlienBases = [new AlienBaseSnapshot(1, "BAD_MONTHLY", "RACE_A", 0.2, 0.1)] } };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Contains("timeline", Assert.IsType<CampaignActionBlocked>(
            Assert.Single(campaign.Execute(new ScheduleWorldScripts()).Events)).Reason);
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
        Assert.Equal(2, campaign.Execute(new ScheduleWorldScripts(false)).Events.Count);
        Assert.Equal(2, campaign.Capture().World.Missions.Count);
        Assert.Single(campaign.Capture().World.Events);
    }

    [Fact]
    public void IntegratedMonthBoundaryRemainsBlockedBeforeTheAffectedTick()
    {
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-world-composition.rul"],
            extraRules: "events: [{name: E, timer: 60}]");
        var snapshot = CreateSnapshot(content, "Guarded month composition") with
        { Time = new CampaignTime(1, 31, 1, 1999, 23, 59, 55), RandomState = 17 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Monthly campaign simulation is not implemented yet; time stopped before the month boundary.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialEntrySchedulesOnceAndChargesAfterSchedulingAcrossReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-composition");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(entry => entry.GetProperty("case").GetString() == "initial");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-world-composition.rul"], fromCache,
            "events: [{name: E, timer: 60}]");
        var snapshot = CreateSnapshot(content, "Initial world entry") with
        { MonthsPassed = -1, DaysPassed = 0, RandomState = 17, Funds = [1000], ResearchScores = [0] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var maintenance = campaign.QueryBaseManagement(snapshot.Bases[0].Id).Maintenance;
        Assert.Equal(2, campaign.Execute(new EnterCampaignWorld()).Events.Count);
        var after = campaign.Capture();
        Assert.Equal(row.GetProperty("month").GetInt32(), after.MonthsPassed);
        Assert.Equal(row.GetProperty("days").GetInt32(), after.DaysPassed);
        Assert.Equal(row.GetProperty("runs").GetInt32(), after.World.Strategy.MissionRuns.Single(pair => pair.Key == "RUN").Value);
        Assert.Equal(row.GetProperty("events").GetInt32(), after.World.Events.Count);
        Assert.Equal(snapshot.Funds[^1] - (maintenance.Total - maintenance.Personnel), after.Funds[^1]);
        Assert.Equal(snapshot.Expenditures, after.Expenditures); // setFunds, not an expenditure.
        Assert.Equal(snapshot.Time, after.Time);
        campaign = Reload(content, campaign, 17, "initial-world-entry.sav");
        Assert.Empty(campaign.Execute(new EnterCampaignWorld()).Events);
        Assert.Equivalent(after, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialEntryBlocksBeforeChangingCalendarResearchFundsOrRandom(bool accountingFailure)
    {
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-world-composition.rul"],
            extraRules: accountingFailure ? "events: [{name: E, timer: 60}]" : null);
        var snapshot = CreateSnapshot(content, "Initial entry preflight") with
        { MonthsPassed = -1, RandomState = 17, DaysPassed = accountingFailure ? int.MaxValue : 0 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new EnterCampaignWorld()).Events));
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
    }

    [Fact]
    public void EntryBeforePlacementIsANoOpAndLegacyOrphansAreGuarded()
    {
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-world-composition.rul"],
            extraRules: "events: [{name: E, timer: 60}]");
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Unplaced initial entry", CampaignDifficulty.Veteran);
        var before = campaign.Capture();
        Assert.Empty(campaign.Execute(new EnterCampaignWorld()).Events);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        var legacy = CreateUfoTransitCampaign(content, before.Time).Capture() with { MonthsPassed = -1 };
        legacy = legacy with { World = legacy.World with { Missions = [] } };
        campaign = CampaignState.Restore(legacy, content, new SplitMix64RandomSource(17));
        Assert.Contains("UFO", Assert.IsType<CampaignActionBlocked>(
            Assert.Single(campaign.Execute(new EnterCampaignWorld()).Events)).Reason);
        Assert.Equivalent(legacy, campaign.Capture(), strict: true);
    }

}
