using System.Collections.ObjectModel;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S5: a popped event runs GeoscapeEventState::eventLogic inside the half-hour tick and its window holds the
/// clock until btnOkClick. The <c>strategic-event-payload</c> oracle (the reference methods compiled verbatim over stubbed
/// collaborators) is replayed on a campaign built like the probe's world: an HQ and a second base with the same stores,
/// a Skyranger out from the HQ and one at home, Europe and North America (Washington, Ottawa). Where the probe stubs a
/// collaborator that the port runs for real (getOneFree draws, soldier generation), the scenario states its own expectation.
/// </summary>
public sealed class StrategicEventPayloadTests
{
    private const string Rules = """
        defeatScore: -100000
        regions:
          - type: STR_EUROPE
            missionZones:
              - [[10, 20, 40, 50, 0]]
          - type: STR_NORTH_AMERICA
            missionZones:
              - [[-77, -77, 38.9, 38.9, 0, Washington], [-75.7, -75.7, 45.4, 45.4, 0, Ottawa]]
        items:
          - type: STR_RIFLE
          - type: STR_RIFLE_CLIP
          - type: STR_GRENADE
          - type: STR_MEDI_KIT
          - type: STR_ELERIUM_115
          - type: BULK
            size: 1000
        crafts:
          - type: STR_SKYRANGER
        research:
          - name: STR_LASER
            cost: 10
          - name: STR_PLASMA_LOOKUP
            cost: 10
          - name: STR_BONUS_LOOKUP
            cost: 10
          - name: STR_BONUS
            cost: 10
            lookup: STR_BONUS_LOOKUP
          - name: STR_PLASMA
            cost: 10
            lookup: STR_PLASMA_LOOKUP
            getOneFree: [STR_BONUS]
          - name: STR_ALLOYS
            cost: 10
        cutscenes:
          - type: winVideo
            winGame: true
          - type: plainVideo
        events:
          - name: STR_CITY_EVENT
            regionList: [STR_EUROPE, STR_NORTH_AMERICA]
            city: true
            points: 120
            funds: -25000
            spawnedPersons: 3
            spawnedPersonType: STR_SCIENTIST
          - name: STR_REGION_EVENT
            regionList: [STR_NORTH_AMERICA]
            points: -40
            spawnedPersons: 2
            spawnedPersonType: STR_ENGINEER
          - name: STR_ITEMS
            everyMultiItemList: {STR_RIFLE: 2, STR_NONE: 5, "": 1}
            everyItemList: [STR_RIFLE, STR_GRENADE, STR_RIFLE]
            randomItemList: [STR_MEDI_KIT, STR_RIFLE_CLIP]
            randomMultiItemList: [{STR_RIFLE_CLIP: 4}, {STR_GRENADE: 3, STR_RIFLE: 1}]
            weightedItemList: {STR_ELERIUM_115: 3, STR_GRENADE: 1}
          - name: STR_BAD_ITEM
            points: 7
            funds: 500
            everyItemList: [STR_RIFLE, STR_BOGUS]
            spawnedPersons: 2
            spawnedPersonType: STR_NOBODY
          - name: STR_THEFT
            invert: true
            everyMultiItemList: {STR_RIFLE: 7, STR_ELERIUM_115: 50, STR_GRENADE: 1}
          - name: STR_CRAFT_GIFT
            spawnedCraftType: STR_SKYRANGER
          - name: STR_RESEARCH_GIFT
            researchList: [STR_LASER, STR_PLASMA, STR_ALLOYS]
          - name: STR_ADHOC
            adhocMissionScriptTags: [TAG_A]
            funds: 1000
          - name: STR_ENDING
            cutscene: winVideo
          - name: STR_PLAIN
            cutscene: plainVideo
            researchList: [STR_ALLOYS]
          - name: STR_NAMED_SOLDIERS
            points: 15
            spawnedPersons: 2
            spawnedPersonType: RECRUIT
            spawnedPersonName: STR_HERO
            spawnedSoldier:
              rank: 3
          - name: STR_SOLDIER_LISTS
            everyMultiSoldierList: {TEMPLATE_RECRUIT: 1, RECRUIT: 1, STR_NULL: 2}
            randomMultiSoldierList: [{RECRUIT: 1}, {TEMPLATE_RECRUIT: 2, RECRUIT: 1}]
          - name: STR_FUNDS
            funds: 1
          - name: STR_MORE_FUNDS
            funds: 10
        """;

    private static readonly Lazy<RuntimeContent> Content = new(() =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules, extraRulesAfterOverlays: true));

    private static readonly CampaignTime HalfHour = new(1, 1, 1, 1999, 0, 29, 55);

    /// <summary>The probe's world, with the event due at the next (half-hour) tick.</summary>
    private static CampaignSnapshot World(string eventId, bool instant = true, int monthsPassed = 2, bool overfull = false,
        IReadOnlyList<string>? researched = null, bool storageLimits = false)
    {
        var content = Content.Value;
        var snapshot = CreateSnapshot(content, "Event payload");
        var hq = snapshot.Bases[0];
        CraftSnapshot Skyranger(int id, string status, IReadOnlyDictionary<string, int> items) => new("STR_SKYRANGER", id)
        {
            PreservationKey = $"test:craft:{id}",
            Logistics = new CraftLogisticsState(0, 0, status, [], new ReadOnlyDictionary<string, int>(items.ToDictionary()), [])
            { Longitude = hq.Longitude, Latitude = hq.Latitude },
        };
        var hqItems = new Dictionary<string, int>(StringComparer.Ordinal) { ["STR_RIFLE"] = 3, ["STR_ELERIUM_115"] = 10 };
        if (overfull) hqItems["BULK"] = 1_000_000;
        var second = hq with
        {
            Id = hq.Id + 1,
            Name = "Second",
            Crafts = [Skyranger(2, "STR_READY", new Dictionary<string, int> { ["STR_RIFLE"] = 4, ["STR_ELERIUM_115"] = 30 })],
            Soldiers = [],
            Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["STR_RIFLE"] = 1, ["STR_ELERIUM_115"] = 15 },
            Transfers = [],
            Research = [],
            Productions = [],
        };
        return snapshot.WithNextId("STR_SKYRANGER", 3) with
        {
            Time = HalfHour,
            RandomState = 0,
            MonthsPassed = monthsPassed,
            Funds = [.. snapshot.Funds.SkipLast(1), 100_000],
            ResearchScores = [.. snapshot.ResearchScores.SkipLast(1), 0],
            Regions = [new("STR_EUROPE", [0], [0]), new("STR_NORTH_AMERICA", [0], [0])],
            // The base fixture's zero-cost UNLOCK is already known, so no grant discovers it as a free topic.
            CompletedResearch = [.. researched ?? [], "UNLOCK"],
            Options = snapshot.Options with { GeoscapeEventsInstantDelivery = instant, StorageLimitsEnforced = storageLimits },
            Bases =
            [
                hq with
                {
                    Crafts = [.. hq.Crafts, Skyranger(1, "STR_OUT", new Dictionary<string, int> { ["STR_RIFLE"] = 5, ["STR_ELERIUM_115"] = 5 })],
                    Items = hqItems,
                    Transfers = [],
                },
                second,
            ],
            World = snapshot.World with { Events = [new GeoscapeEventSnapshot(eventId, 30)] },
        };
    }

    private static JsonElement Expected(string name)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event-payload");
        return TestFixtures.Rows(oracle.RootElement, "scenarios").Single(s => s.GetProperty("name").GetString() == name).Clone();
    }

    /// <summary>Runs the half-hour tick with the scenario's raw draws and checks the window, then acknowledges it.</summary>
    private static (CampaignState Campaign, CampaignEventWindow Window, ScriptedRandom Random) Pop(CampaignSnapshot snapshot,
        IEnumerable<ulong> rng)
    {
        var random = new ScriptedRandom([.. rng]);
        var campaign = CampaignState.Restore(snapshot, Content.Value, random);
        var events = AssertEventTickRuns(campaign);
        var window = Assert.Single(events.OfType<CampaignEventOpened>()).Window;
        Assert.Equal(window, Assert.Single(campaign.GetQuery<ICampaignEventWindowQuery>()!.QueryEventWindows()));
        // D2: the window holds the clock and forbids saving until it is answered.
        AssertTimeBlocked(campaign, CampaignState.EventWindowOpen);
        Assert.Equal(CampaignState.EventWindowOpen, campaign.QuerySaveRefusal());
        return (campaign, window, random);
    }

    private static void AssertLedger(CampaignState campaign, JsonElement expected)
    {
        var after = campaign.Capture();
        var regions = expected.GetProperty("regions");
        Assert.Equal((regions.GetProperty("STR_EUROPE").GetInt32(), regions.GetProperty("STR_NORTH_AMERICA").GetInt32()),
            (after.Regions[0].ActivityXcom[^1], after.Regions[1].ActivityXcom[^1]));
        Assert.Equal(expected.GetProperty("researchScore").GetInt32(), after.ResearchScores[^1]);
        Assert.Equal(expected.GetProperty("funds").GetInt64(), after.Funds[^1]);
        var stores = expected.GetProperty("stores").EnumerateArray().ToArray();
        for (var index = 0; index < 2; index++)
            Assert.Equal(Map(stores[index]), Items(after.Bases[index].Items));
        var craftItems = expected.GetProperty("craftItems").EnumerateArray().ToArray();
        Assert.Equal(Map(craftItems[0]), Items(after.Bases[0].Crafts.Single(static c => c.RuleId == "STR_SKYRANGER" && c.Id == 1).Logistics!.Items));
        Assert.Equal(Map(craftItems[1]), Items(after.Bases[1].Crafts.Single(static c => c.RuleId == "STR_SKYRANGER" && c.Id == 2).Logistics!.Items));
        // Transfers into the HQ, in order: staff, soldiers, items, a craft.
        var transfers = after.Bases[0].Transfers.Select(static t => t.Kind switch
        {
            CampaignTransferKind.Scientist => $"{t.Hours} scientists {t.Quantity}",
            CampaignTransferKind.Engineer => $"{t.Hours} engineers {t.Quantity}",
            CampaignTransferKind.Item => $"{t.Hours} item {t.RuleId} {t.Quantity}",
            CampaignTransferKind.Craft => $"{t.Hours} craft {t.RuleId}-{t.Craft!.Id} {t.Craft.Logistics!.Status}",
            _ => $"{t.Hours} soldier {t.RuleId}",
        });
        Assert.Equal(expected.GetProperty("transfers").EnumerateArray().Select(static t =>
            t.TryGetProperty("scientists", out var s) ? $"{t.GetProperty("hours").GetInt32()} scientists {s.GetInt32()}"
            : t.TryGetProperty("engineers", out var e) ? $"{t.GetProperty("hours").GetInt32()} engineers {e.GetInt32()}"
            : t.TryGetProperty("item", out var i) ? $"{t.GetProperty("hours").GetInt32()} item {i.GetString()} {t.GetProperty("quantity").GetInt32()}"
            : t.TryGetProperty("craft", out var c) ? $"{t.GetProperty("hours").GetInt32()} craft {c.GetProperty("type").GetString()}-{c.GetProperty("id").GetInt32()} {c.GetProperty("status").GetString()}"
            : $"{t.GetProperty("hours").GetInt32()} soldier {t.GetProperty("soldier").GetProperty("type").GetString()}"), transfers);
        // New crafts at the HQ (the out craft's own status is the later levels' business).
        Assert.Equal(expected.GetProperty("hqCrafts").EnumerateArray().Select(static c => c.GetString()!).Where(static c => !c.StartsWith("STR_SKYRANGER-1 ", StringComparison.Ordinal)),
            after.Bases[0].Crafts.Where(static c => c.RuleId == "STR_SKYRANGER" && c.Id >= 3).Select(static c => $"{c.RuleId}-{c.Id} {c.Logistics!.Status}"));
    }

    private static void AssertDiary(CampaignState campaign, JsonElement expected) =>
        Assert.Equal(expected.GetProperty("diary").EnumerateArray().Select(static d => d.GetString()!),
            campaign.Capture().ResearchDiary.Select(static d =>
                $"{d.RuleId} {((ResearchDiscoverySource)d.SourceType == ResearchDiscoverySource.Event ? "event" : "free")} {d.SourceName}"));

    private static void AssertRows(CampaignEventWindow window, JsonElement expected) =>
        Assert.Equal(expected.GetProperty("rows").EnumerateArray().Select(static r => new CampaignEventItemRow(r[0].GetString()!,
            int.Parse(r[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture))), window.Items);

    private static void AssertDraws(ScriptedRandom random, JsonElement expected) =>
        Assert.Equal(JsonSerializer.Serialize(expected.GetProperty("rngCalls")), JsonSerializer.Serialize(random.Calls));

    [Theory]
    [InlineData("city-region-score-funds-scientists", "STR_CITY_EVENT", true)]
    [InlineData("region-not-city-specific-still-picks-a-city", "STR_REGION_EVENT", true)]
    [InlineData("items-instant-delivery", "STR_ITEMS", true)]
    [InlineData("items-by-transfer", "STR_ITEMS", false)]
    [InlineData("invert-removes-from-stores-then-crafts-not-out", "STR_THEFT", true)]
    [InlineData("craft-instant-delivery", "STR_CRAFT_GIFT", true)]
    [InlineData("craft-by-transfer", "STR_CRAFT_GIFT", false)]
    [InlineData("adhoc-scripts-run-first", "STR_ADHOC", true)]
    public void PayloadMatchesTheReference(string name, string eventId, bool instant)
    {
        var expected = Expected(name);
        var (campaign, window, random) = Pop(World(eventId, instant), expected.GetProperty("rng").EnumerateArray().Select(static v => v.GetUInt64()));

        AssertLedger(campaign, expected);
        AssertRows(window, expected);
        AssertDiary(campaign, expected);
        AssertDraws(random, expected);
        Assert.Equal(("", ""), (window.ResearchName, window.BonusResearchName));
        // The place: the region and the city picked for it, named in the title of a city-specific event.
        if (name.StartsWith("city", StringComparison.Ordinal)) Assert.Equal(("STR_NORTH_AMERICA", "Ottawa"), (window.Region, window.City));
        if (name.StartsWith("region", StringComparison.Ordinal)) Assert.Equal(("STR_NORTH_AMERICA", "Ottawa"), (window.Region, window.City));

        Assert.Equal([new CampaignEventAcknowledged(eventId, null, false, "", "")], campaign.Execute(new AcknowledgeCampaignEvent()).Events);
        Assert.Null(campaign.QuerySaveRefusal());
        AdvanceUnblocked(campaign, 1);
    }

    [Fact]
    public void AnUnknownItemRefusesTheTickBeforeAnythingChanges()
    {
        // The reference throws (Mod::getRule) after the score and funds have changed, and the game stops; the port
        // refuses the tick on its shadow copy, so nothing changes (ADR 0036).
        Assert.NotEmpty(Expected("unknown-item-throws-after-score-and-funds").GetProperty("exception").GetString()!);
        var campaign = CampaignState.Restore(World("STR_BAD_ITEM"), Content.Value, new SplitMix64RandomSource(5));
        var before = campaign.Capture();
        AssertTimeBlocked(campaign, "Strategic event item 'STR_BOGUS' is not defined.");
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ResearchSkipsKnownTopicsAndGrantsTheLookupAndAFreeTopic()
    {
        var expected = Expected("research-skips-known-topics-and-adds-lookup-and-bonus");
        // The probe's getOneFree stub draws nothing; SavedGame::selectGetOneFree draws over its one candidate.
        var (campaign, window, random) = Pop(World("STR_RESEARCH_GIFT", researched: ["STR_LASER"]), [0, 0]);

        AssertDiary(campaign, expected);
        Assert.Equal(("STR_PLASMA_LOOKUP", "STR_BONUS_LOOKUP"), (window.ResearchName, window.BonusResearchName));
        Assert.Equal([[0, 1, 0], [0, 0, 0]], random.Calls);
        Assert.Superset(new HashSet<string> { "STR_LASER", "STR_PLASMA", "STR_PLASMA_LOOKUP", "STR_BONUS", "STR_BONUS_LOOKUP" },
            campaign.Capture().CompletedResearch.ToHashSet());
        Assert.Equal([new CampaignEventAcknowledged("STR_RESEARCH_GIFT", null, false, "STR_BONUS_LOOKUP", "STR_PLASMA_LOOKUP")],
            campaign.Execute(new AcknowledgeCampaignEvent()).Events);
    }

    [Fact]
    public void AKnownTopicWithAnUndiscoveredFreeTopicShowsNoArticleOfItsOwn()
    {
        // STR_PLASMA and its lookup are known, its free topic is not: the event grants the free topic and shows only it.
        var (campaign, window, _) = Pop(World("STR_RESEARCH_GIFT", researched: ["STR_LASER", "STR_PLASMA", "STR_PLASMA_LOOKUP", "STR_ALLOYS"]), [0, 0]);

        Assert.Equal(("", "STR_BONUS_LOOKUP"), (window.ResearchName, window.BonusResearchName));
        Assert.Equal(["STR_BONUS free STR_PLASMA", "STR_BONUS_LOOKUP free STR_PLASMA"],
            campaign.Capture().ResearchDiary.Select(static d => $"{d.RuleId} {((ResearchDiscoverySource)d.SourceType == ResearchDiscoverySource.Event ? "event" : "free")} {d.SourceName}"));
    }

    [Fact]
    public void AnEventCutsceneWinsOnlyWhileTheCampaignHasNoEnding()
    {
        Assert.Equal(1, Expected("cutscene-wins-when-no-ending").GetProperty("ending").GetInt32());
        var snapshot = World("STR_ENDING");
        // Two windows in one tick: the first OK ends the campaign; the ended campaign then refuses every command.
        snapshot = snapshot with { World = snapshot.World with { Events = [new("STR_ENDING", 30), new("STR_ENDING", 30)] } };
        var campaign = CampaignState.Restore(snapshot, Content.Value, new SplitMix64RandomSource(5));
        Assert.Equal(2, AssertEventTickRuns(campaign).OfType<CampaignEventOpened>().Count());

        Assert.Equal([new CampaignEventAcknowledged("STR_ENDING", "winVideo", false, "", ""), new CampaignEnded(1)],
            campaign.Execute(new AcknowledgeCampaignEvent()).Events);
        Assert.Equal("This campaign has ended.", Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new AcknowledgeCampaignEvent()).Events)).Reason);
        Assert.Equal(1, campaign.Ending);
    }

    [Theory]
    [InlineData("overfull-stores-open-the-sell-screen", 2, true)]
    [InlineData("overfull-ignored-before-month-zero", -1, false)]
    public void AnOverfullHqOpensTheSellScreenFromTheFirstMonth(string name, int monthsPassed, bool exceeded)
    {
        var expected = Expected(name);
        var (campaign, window, _) = Pop(World("STR_PLAIN", monthsPassed: monthsPassed, overfull: true, storageLimits: true), [0]);

        AssertDiary(campaign, expected);
        Assert.Equal(expected.GetProperty("researchName").GetString(), window.ResearchName);
        Assert.Equal(exceeded, expected.GetProperty("ok").EnumerateArray().Any(static entry => entry.GetString()!.StartsWith("pushState SellState", StringComparison.Ordinal)));
        Assert.Equal([new CampaignEventAcknowledged("STR_PLAIN", "plainVideo", exceeded, "", "STR_ALLOYS")],
            campaign.Execute(new AcknowledgeCampaignEvent()).Events);
    }

    [Fact]
    public void NamedSoldiersArriveFromTheirTemplateWithoutARegionToPlaceThem()
    {
        var expected = Expected("no-region-research-score-named-soldiers");
        var (campaign, _, _) = Pop(World("STR_NAMED_SOLDIERS"), []);

        // No region: the score goes to research, and no city places the soldiers (no nationality draw).
        Assert.Equal(expected.GetProperty("researchScore").GetInt32(), campaign.Capture().ResearchScores[^1]);
        var arrivals = campaign.Capture().Bases[0].Transfers;
        Assert.Equal(2, arrivals.Count);
        Assert.All(arrivals, static t =>
        {
            Assert.Equal((24, CampaignTransferKind.Soldier, "RECRUIT"), (t.Hours, t.Kind, t.RuleId));
            Assert.Equal(("STR_HERO", 3), (t.Soldier!.Personal!.Name, t.Soldier.Personal.Rank));
        });
        Assert.Equal(2, arrivals.Select(static t => t.Soldier!.Id).Distinct().Count());
    }

    [Fact]
    public void SoldierListsMergeByRuleInLoadOrder()
    {
        // The random list's second entry; STR_NULL names no rule. std::map by rule address keeps load order: RECRUIT
        // before TEMPLATE_RECRUIT.
        var (campaign, _, random) = Pop(World("STR_SOLDIER_LISTS"), [1]);

        Assert.Equal([0, 1, 1], random.Calls[0]);
        Assert.Equal(["RECRUIT", "RECRUIT", "TEMPLATE_RECRUIT", "TEMPLATE_RECRUIT", "TEMPLATE_RECRUIT"],
            campaign.Capture().Bases[0].Transfers.Select(static t => t.RuleId));
    }

    [Fact]
    public void EventsPopInListOrderAndTheirWindowsAreAnsweredInTurn()
    {
        var snapshot = World("STR_FUNDS");
        snapshot = snapshot with { World = snapshot.World with { Events = [new("STR_MORE_FUNDS", 30), new("STR_FUNDS", 60), new("STR_FUNDS", 30)] } };
        var campaign = CampaignState.Restore(snapshot, Content.Value, new SplitMix64RandomSource(5));

        Assert.Equal(["STR_MORE_FUNDS", "STR_FUNDS"], AssertEventTickRuns(campaign).OfType<CampaignEventOpened>().Select(static e => e.Window.RuleId));
        Assert.Equal(100_011, campaign.Capture().Funds[^1]);
        // The event that was not due counts down and stays.
        Assert.Equal(30, Assert.Single(campaign.Capture().World.Events).SpawnCountdown);
        Assert.Equal("STR_MORE_FUNDS", Assert.IsType<CampaignEventAcknowledged>(Assert.Single(campaign.Execute(new AcknowledgeCampaignEvent()).Events)).RuleId);
        // The second window still holds the clock and every other command.
        AssertTimeBlocked(campaign, CampaignState.EventWindowOpen);
        Assert.Equal(CampaignState.EventWindowOpen, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new RecallCraft(snapshot.Bases[0].Id, "STR_SKYRANGER", 1)).Events)).Reason);
        Assert.Equal("STR_FUNDS", Assert.IsType<CampaignEventAcknowledged>(Assert.Single(campaign.Execute(new AcknowledgeCampaignEvent()).Events)).RuleId);
        AdvanceUnblocked(campaign, 1);
    }

    [Fact]
    public void TheMonthlyReportIsAnsweredBeforeTheMonthTicksEvents()
    {
        var snapshot = World("STR_FUNDS") with { Time = new(1, 31, 1, 1999, 23, 59, 55) };
        var campaign = CampaignState.Restore(snapshot, Content.Value, new SplitMix64RandomSource(5));
        var events = AssertEventTickRuns(campaign);
        Assert.Single(events.OfType<MonthlyReportReady>());
        Assert.Single(events.OfType<CampaignEventOpened>());

        AssertTimeBlocked(campaign, CampaignState.MonthlyReportOpen);
        Assert.Equal(CampaignState.MonthlyReportOpen, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new AcknowledgeCampaignEvent()).Events)).Reason);
        Assert.Contains(campaign.Execute(new AcknowledgeMonthlyReport()).Events, static e => e is MonthlyReportAcknowledged);
        AssertTimeBlocked(campaign, CampaignState.EventWindowOpen);
        Assert.IsType<CampaignEventAcknowledged>(Assert.Single(campaign.Execute(new AcknowledgeCampaignEvent()).Events));
        AdvanceUnblocked(campaign, 1);
    }

    [Fact]
    public void AReloadDropsAnOpenEventWindowAndKeepsItsPayload()
    {
        var campaign = CampaignState.Restore(World("STR_FUNDS"), Content.Value, new SplitMix64RandomSource(5));
        AssertEventTickRuns(campaign);
        // Popups are not saved: the payload has run, the window is gone and the clock runs.
        var reloaded = Reload(Content.Value, campaign, 5, "event-window.sav");
        Assert.Empty(reloaded.GetQuery<ICampaignEventWindowQuery>()!.QueryEventWindows());
        Assert.Equal(100_001, reloaded.Capture().Funds[^1]);
        AdvanceUnblocked(reloaded, 1);
    }

    // RandomState::generate over scripted raw values; zero once the script is exhausted. The index is the state.
    internal sealed class ScriptedRandom(ulong[] script) : IStatefulRandomSource
    {
        private int _next;
        public List<int[]> Calls { get; } = [];
        public ulong State => (uint)_next | (ulong)(uint)Calls.Count << 32;

        public void Restore(ulong state)
        {
            _next = (int)(state & uint.MaxValue);
            var keep = (int)(state >> 32);
            if (keep < Calls.Count) Calls.RemoveRange(keep, Calls.Count - keep);
        }

        public int NextInclusive(int minimum, int maximum)
        {
            var raw = _next < script.Length ? script[_next] : 0;
            _next++;
            var result = unchecked((int)(raw % (ulong)(long)(maximum - minimum + 1) + (ulong)(long)minimum));
            Calls.Add([minimum, maximum, result]);
            return result;
        }

        public int NextExclusive(int exclusiveMaximum) => NextInclusive(0, exclusiveMaximum - 1);
        public double NextUnit() => throw new InvalidOperationException("Event payloads draw integers only.");
    }

    private static Dictionary<string, int> Map(JsonElement element) =>
        element.EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value.GetInt32(), StringComparer.Ordinal);

    private static Dictionary<string, int> Items(IReadOnlyDictionary<string, int> items) =>
        items.Where(static p => p.Key != "BULK" && p.Value != 0).ToDictionary(static p => p.Key, static p => p.Value, StringComparer.Ordinal);
}
