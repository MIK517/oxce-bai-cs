using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicEventScriptTests
{
    [Fact]
    public void SelectionAndHistoryMatchExtractedLoop()
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event-scripts");
        var history = new Dictionary<string, int>(StringComparer.Ordinal);
        var round = 0;
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            if (round == 2) { history.Clear(); history["A"] = 0; }
            if (round == 3) history.Clear();
            var random = new FirstRoll();
            var generated = new List<string>();
            foreach (var id in round++ == 3 ? new[] { "SELF_TAIL" } : ["FIRST", "SECOND"])
            {
                var rules = content.RuntimeRules.EventScripts;
                var selected = WorldEventScriptSelection.Select(rules[rules.GetRequired(id)].Value, 0, history, random);
                foreach (var choice in selected)
                {
                    generated.Add(choice.Id);
                    history[choice.Id] = history.GetValueOrDefault(choice.Id) + 1;
                }
            }
            var actual = JsonSerializer.SerializeToUtf8Bytes(new { events = generated, draws = random.Calls });
            TestFixtures.AssertJsonEqual(row, actual);
        }
    }

    [Fact]
    public void SubsystemSchedulesPersistsSuppressesAndCleansUpRepeatedEvents()
    {
        TestFixtures.LoadVerifiedManifest("strategic-event-scripts");
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Event scripts") with { RandomState = 17, CompletedResearch = ["STOP"] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events);
        var first = campaign.Capture();
        Assert.Equal(5, first.World.Events.Count);
        Assert.Equal(["A", "B", "C"], first.World.GeneratedEvents.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(first.World.Events.Count, first.World.Events.Select(entry => entry.PreservationKey).Distinct().Count());
        Assert.All(first.World.Events, entry => Assert.Equal(entry.RuleId == "A" ? 90 : 60, entry.SpawnCountdown));
        campaign = Reload(content, campaign, 17, "script-scheduled.sav");
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events);
        var second = campaign.Capture();
        Assert.Equal(6, second.World.Events.Count);
        Assert.Equal(first.World.GeneratedEvents["A"] + 1, second.World.GeneratedEvents["A"]);
        Assert.Equal(first.World.GeneratedEvents["B"], second.World.GeneratedEvents["B"]);
        Assert.Equal(first.World.GeneratedEvents["C"], second.World.GeneratedEvents["C"]);
        campaign = Reload(content, campaign, 17, "script-repeat.sav");
        AdvanceUnblocked(campaign, 1080);
        Assert.Empty(campaign.Capture().World.Events);
        var afterCleanup = Reload(content, campaign, 17, "script-cleanup.sav").Capture();
        Assert.Equal(second.World.GeneratedEvents, afterCleanup.World.GeneratedEvents);
        Assert.Equal(initial.MonthsPassed, afterCleanup.MonthsPassed);
    }

    [Fact]
    public void AllExecutionOddsAreDrawnBeforeAnySelectionOrSpawnTimer()
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Event draw ordering") with { MonthsPassed = 2, RandomState = 74 };
        var expectedRandom = new SplitMix64RandomSource(74);
        var first = expectedRandom.NextInclusive(0, 99) < 50;
        var second = expectedRandom.NextInclusive(0, 99) < 50;
        var expected = new List<string>();
        if (first) expected.Add("A");
        if (second) expected.Add("B");
        foreach (var _ in expected)
        {
            expectedRandom.NextInclusive(1, 1); // repeatable choice
            expectedRandom.NextInclusive(0, 0); // spawn timer
        }
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(74));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events);
        Assert.Equal(expected, campaign.Capture().World.Events.Select(entry => entry.RuleId));
        Assert.Equal(expectedRandom.State, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("research")]
    [InlineData("item")]
    [InlineData("facility")]
    [InlineData("soldier")]
    [InlineData("region")]
    [InlineData("country")]
    [InlineData("pact")]
    [InlineData("runs")]
    [InlineData("marker")]
    [InlineData("score")]
    [InlineData("funds")]
    [InlineData("difficulty")]
    [InlineData("debug")]
    public void AllEligibilityGroupsUseLiveCampaignState(string condition)
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Script eligibility");
        initial = initial with
        {
            MonthsPassed = 1,
            RandomState = 17,
            CompletedResearch = condition == "research" ? [] : ["STOP"],
            DebugMode = condition == "debug",
            Difficulty = condition == "difficulty" ? CampaignDifficulty.Beginner : CampaignDifficulty.Veteran,
            Countries = condition == "country" ? [] : [initial.Countries[0] with { Pact = condition != "pact", NewPact = true }],
            Regions = condition == "region" ? [] : initial.Regions,
            NextIds = new Dictionary<string, int> { ["MARKER"] = condition == "marker" ? 4 : 1 },
            World = initial.World with
            {
                Strategy = initial.World.Strategy with
                { MissionRuns = [new("RUNS", condition == "runs" ? 0 : 1)] }
            },
        };
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with
        {
            // Disabled-but-built facilities count; items in transfer do not.
            Facilities = [owner.Facilities[0] with { BuildTime = condition == "facility" ? 1 : 0, Disabled = true }],
            Items = new Dictionary<string, int>(),
            Crafts = [.. owner.Crafts.Select(craft => craft with { Logistics = craft.Logistics! with
                { Items = condition == "item" ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = 1 } } })],
            Soldiers = condition == "soldier" ? [] : owner.Soldiers,
            Transfers = [new(100, 2, CampaignTransferKind.Item, "BULKY", 1), new(101, 2, CampaignTransferKind.Item, "SUPPLY", 1)],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(condition == "score" ? 31 : 20,
            condition == "funds" ? 6000000001L : 5000000000L)).Events);
        if (condition == "valid") Assert.Equal("C", Assert.Single(campaign.Capture().World.Events).RuleId);
        else { Assert.Empty(campaign.Capture().World.Events); Assert.Equal(17UL, campaign.Capture().RandomState); }
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(8, false)]
    public void InvalidSelectionsStopBeforeAnyMutationOrRandomDraw(int month, bool throws)
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Invalid event selection") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        if (throws) Assert.Throws<InvalidDataException>(() => campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)));
        else Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(9, 3)]
    [InlineData(10, 1)]
    [InlineData(14, 1)]
    public void UnreachableSequentialTailWaitsUntilNextPassEvenAcrossReload(int month, int count)
    {
        TestFixtures.LoadVerifiedManifest("strategic-event-scripts");
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Sequential tail") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events);
        Assert.Equal(count, campaign.Capture().World.Events.Count);
        Assert.All(campaign.Capture().World.Events, entry => Assert.Equal("A", entry.RuleId));
        campaign = Reload(content, campaign, 17, "sequential-tail.sav");
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void EarlierScriptsExposeSequentialTailBeforeAnyMutationOrRandomDraw(int month)
    {
        TestFixtures.LoadVerifiedManifest("strategic-event-scripts");
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Earlier script exposes tail") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ZeroExecutionOddsStillDrawsButDoesNotValidateUnreachablePayload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Zero script odds") with { MonthsPassed = 7, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99);
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events);
        Assert.Empty(campaign.Capture().World.Events);
        Assert.Equal(expected.State, campaign.Capture().RandomState);
    }

    [Fact]
    public void ZeroCountHistorySuppressesOneTimeEventsAcrossReload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Zero event history") with { RandomState = 17 };
        initial = initial with { World = initial.World with { GeneratedEvents = new Dictionary<string, int> { ["A"] = 0 } } };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "zero-history.sav");
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events);
        Assert.Equal(["B", "B", "A", "C"], campaign.Capture().World.Events.Select(entry => entry.RuleId));
        Assert.Equal(1, campaign.Capture().World.GeneratedEvents["A"]);
        campaign = Reload(content, campaign, 17, "zero-history-generated.sav");
        AdvanceUnblocked(campaign, 360); // first half-hour decreases timers; scheduling future effects is allowed.
        AssertEventTickRuns(campaign, popped: ["B", "B", "C"]);
    }

    [Fact]
    public void ExhaustedHistoryStopsBeforeMutationAndTheMonthStillRuns()
    {
        var content = StrategicReadinessTestContent.Load("strategic-event-scripts.rul");
        var initial = CreateSnapshot(content, "Event capacity") with { RandomState = 17 };
        initial = initial with { World = initial.World with { GeneratedEvents = new Dictionary<string, int> { ["A"] = int.MaxValue } } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldEvents(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        campaign = CampaignState.Restore(initial with { Time = new(1, 31, 1, 1999, 23, 59, 55) }, content,
            new SplitMix64RandomSource(17));
        // Branch 6 S3: the month runs (event A is not eligible then) and its report holds the clock.
        Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>());
        AssertTimeBlocked(campaign, CampaignState.MonthlyReportOpen);
    }

    private sealed class FirstRoll : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minimum, int maximum) { Calls++; Assert.True(minimum <= 1 && maximum >= 1); return 1; }
        public int NextExclusive(int maximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }
}
