using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicEventSchedulingTests
{
    [Fact]
    public void TimersMatchExtractedReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var timer = row.GetProperty("timer").GetInt32();
            var randomMaximum = row.GetProperty("random").GetInt32();
            var choice = row.GetProperty("choice").GetInt32();
            var scheduled = new GeoscapeEventSnapshot("EVENT",
                WorldEventTiming.SpawnCountdown(timer, randomMaximum, new EventChoice(choice)));
            List<int> countdowns = [scheduled.SpawnCountdown];
            while (!scheduled.Over)
            {
                scheduled = WorldEventTiming.Think(scheduled);
                countdowns.Add(scheduled.SpawnCountdown);
            }
            scheduled = WorldEventTiming.Think(scheduled);
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetProperty("countdowns").GetRawText())),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(countdowns)));
            Assert.Equal(row.GetProperty("over").GetBoolean(), scheduled.Over);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchSchedulesRepeatedEventsAndPersistsHistoryInsideTheTick(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-event");
        var content = StrategicReadinessTestContent.Load("strategic-events.rul", fromCache);
        var rule = content.RuntimeRules.Events[content.RuntimeRules.Events.GetRequired("EVENT_DEFAULT")].Value;
        Assert.Equal(30, rule.Timer);
        Assert.Equal(0, rule.TimerRandom);
        Assert.Equal("", rule.InterruptResearch);
        var snapshot = CreateSnapshot(content, "Event research");
        var owner = snapshot.Bases[0];
        var campaign = CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
            Bases = [owner with { Research = [new ResearchProjectSnapshot("EVENT_SOURCE", 1, 0, 1)] }],
        }, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(2, result.Events.OfType<CampaignStrategicEventRequested>().Count());
        var saved = campaign.Capture();
        Assert.Equal(2, saved.World.GeneratedEvents["EVENT_REPEAT"]);
        Assert.Equal(2, saved.World.Events.Count);
        Assert.All(saved.World.Events, scheduled => Assert.Equal(60, scheduled.SpawnCountdown));
        campaign = Reload(content, campaign, 17, "event-research.sav");
        AdvanceUnblocked(campaign, 360);
        Assert.All(campaign.Capture().World.Events, scheduled => Assert.Equal(30, scheduled.SpawnCountdown));
        AdvanceUnblocked(campaign, 359);
        campaign = Reload(content, campaign, 17, "event-due.sav");
        AssertTimeBlocked(campaign, "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionSchedulesPerUnitAndInterruptedEventsExpireWithoutPayload(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-events.rul", fromCache);
        var snapshot = CreateSnapshot(content, "Event production");
        var owner = snapshot.Bases[0];
        var campaign = CampaignState.Restore(snapshot with
        {
            CompletedResearch = ["STOP_EVENT"],
            Time = new CampaignTime(1, 1, 1, 1999, 0, 59, 55),
            Bases = [owner with { Productions = [new ProductionSnapshot("EVENT_PRODUCT", 2, 0, 2, false, false, false,
                new Dictionary<string, int>())] }],
        }, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(2, campaign.Capture().World.Events.Count);
        Assert.Equal(2, campaign.Capture().World.GeneratedEvents["EVENT_REPEAT"]);
        campaign = Reload(content, campaign, 17, "event-production.sav");
        AdvanceUnblocked(campaign, 720);
        Assert.Empty(campaign.Capture().World.Events);
        Assert.Equal(2, campaign.Capture().World.GeneratedEvents["EVENT_REPEAT"]);
        Reload(content, campaign, 17, "event-expired.sav");
    }

    [Theory]
    [InlineData("BAD_SOURCE", false)]
    [InlineData("EVENT_SOURCE", true)]
    public void ProducerPreflightRejectsBeforeResearchOrTimeMutates(string research, bool exhaustedHistory)
    {
        var content = StrategicReadinessTestContent.Load("strategic-events.rul");
        var snapshot = CreateSnapshot(content, "Blocked event");
        var campaign = CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [new ResearchProjectSnapshot(research, 1, 0, 1)] }],
            World = snapshot.World with
            {
                GeneratedEvents = exhaustedHistory
                ? new Dictionary<string, int> { ["EVENT_REPEAT"] = int.MaxValue }
                : new Dictionary<string, int>(),
            },
        }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, exhaustedHistory
            ? "Strategic event population or generation count exceeds the supported range."
            : "Strategic event timer exceeds the supported range.");
    }

    [Fact]
    public void DueEventCannotUseInterruptionResearchThatMayBeDisabledEarlierInTheTick()
    {
        var content = StrategicReadinessTestContent.Load("strategic-events.rul");
        var snapshot = CreateSnapshot(content, "Interruption changes");
        var campaign = CampaignState.Restore(snapshot with
        {
            CompletedResearch = ["STOP_EVENT"],
            Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [new ResearchProjectSnapshot("EVENT_SOURCE", 1, 0, 1)] }],
            World = snapshot.World with { Events = [new GeoscapeEventSnapshot("EVENT_REPEAT", 30)] },
        }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
    }

    [Fact]
    public void RemovedRuleHistoryAndDeletedEventsSurviveLoadedRewriteWithoutResurrection()
    {
        var content = StrategicReadinessTestContent.Load("strategic-events.rul");
        var snapshot = CreateSnapshot(content, "Event rewrite");
        snapshot = snapshot with
        {
            CompletedResearch = ["STOP_EVENT"],
            Time = new CampaignTime(1, 1, 1, 1999, 0, 29, 55),
            World = snapshot.World with
            {
                GeneratedEvents = new Dictionary<string, int> { ["REMOVED_RULE"] = 7, ["EVENT_REPEAT"] = 2 },
            },
        };
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot) + """

            geoscapeEvents:
              - name: EVENT_REPEAT
                spawnCountdown: 30
                futureEvent: expired
              - name: EVENT_REPEAT
                spawnCountdown: 90
                futureEvent: survivor
            """;
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 17, "events.sav");
        AdvanceOne(loaded.Campaign);
        for (var pass = 0; pass < 3; pass++)
        {
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
            Assert.Contains("futureEvent: survivor", rewritten, StringComparison.Ordinal);
            Assert.DoesNotContain("futureEvent: expired", rewritten, StringComparison.Ordinal);
            loaded = TestFixtures.LoadLogisticsSave(rewritten, content, 17, "events.sav");
            Assert.Equal(60, Assert.Single(loaded.Campaign.Capture().World.Events).SpawnCountdown);
            Assert.Equal(7, loaded.Campaign.Capture().World.GeneratedEvents["REMOVED_RULE"]);
        }
    }

    private sealed class EventChoice(int choice) : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) { Assert.InRange(choice, minimum, maximum); return choice; }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }
}
