using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S1: the global rules, cutscene flags, event payloads and user options that the month rollover,
/// the monthly report and event payloads read are projected with their reference defaults and merge rules
/// (<c>Mod::loadFile</c>, <c>RuleEvent::load</c>, <c>Options::create</c>) on fresh and cached content.
/// </summary>
public sealed class StrategicCampaignCycleRulesTests
{
    private const string MonthlyRules = """
        chanceToDetectAlienBaseEachMonth: 35
        defeatScore: -100
        defeatFunds: -5000
        gameOver:
          loseMoney: MONEY_VIDEO
          loseRating: RATING_VIDEO
        monthlyRatings:
          0: STR_FINE
          -300: STR_MEH
        cutscenes:
          - type: MONEY_VIDEO
            loseGame: true
          - type: WIN_VIDEO
            winGame: true
        research:
          - name: VICTORY
            cutscene: WIN_VIDEO
        """;

    [Fact]
    public void MonthlyRulesDefaultAsTheReference()
    {
        var campaign = StrategicReadinessTestContent.Load("strategic-world.rul").RuntimeRules.Campaign;

        Assert.Equal(20, campaign.Monthly.ChanceToDetectAlienBaseEachMonth);
        Assert.Equal(["loseGame", "loseGame", "loseGame"],
            [campaign.Monthly.LoseMoneyCutscene, campaign.Monthly.LoseRatingCutscene, campaign.Monthly.LoseDefeatCutscene]);
        Assert.Empty(campaign.Monthly.MonthlyRatings);
        Assert.Equal(0, campaign.DefeatScore);
        Assert.Equal(0, campaign.DefeatFunds);
    }

    [Fact]
    public void MonthlyRulesCutscenesAndResearchCutscenesAreProjected()
    {
        var rules = StrategicReadinessTestContent.Load(["strategic-world.rul"], MonthlyRules).RuntimeRules;
        var monthly = rules.Campaign.Monthly;

        Assert.Equal(35, monthly.ChanceToDetectAlienBaseEachMonth);
        Assert.Equal(("MONEY_VIDEO", "RATING_VIDEO", "loseGame"),
            (monthly.LoseMoneyCutscene, monthly.LoseRatingCutscene, monthly.LoseDefeatCutscene));
        // std::map<int, std::string>: ordered by score.
        Assert.Equal([new CampaignMonthlyRating(-300, "STR_MEH"), new CampaignMonthlyRating(0, "STR_FINE")], monthly.MonthlyRatings);
        Assert.Equal((-100, -5000), (rules.Campaign.DefeatScore, rules.Campaign.DefeatFunds));
        Assert.Equal(new(false, true), rules.Campaign.Cutscenes["MONEY_VIDEO"]);
        Assert.Equal(new(true, false), rules.Campaign.Cutscenes["WIN_VIDEO"]);
        Assert.Equal("WIN_VIDEO", rules.Research[rules.Research.GetRequired("VICTORY")].Value.Cutscene);
        Assert.Equal("", rules.Research[rules.Research.GetRequired("UNLOCK")].Value.Cutscene);
    }

    [Fact]
    public void ALaterMonthlyRatingsMappingReplacesTheEarlierOne()
    {
        var monthly = StrategicReadinessTestContent.Load(["strategic-world.rul"], MonthlyRules,
            finalRules: "monthlyRatings:\n  500: STR_TOP\n").RuntimeRules.Campaign.Monthly;

        Assert.Equal([new CampaignMonthlyRating(500, "STR_TOP")], monthly.MonthlyRatings);
        // Scalars from the earlier layer stay.
        Assert.Equal(35, monthly.ChanceToDetectAlienBaseEachMonth);
    }

    [Fact]
    public void EventPayloadKeepsTheReferenceOrders()
    {
        var rules = StrategicReadinessTestContent.Load(["strategic-world.rul"], MonthlyRules + """

            events:
              - name: STR_GIFT
                description: STR_GIFT_DESC
                cutscene: WIN_VIDEO
                regionList: [REGION]
                city: true
                points: 5
                funds: -10
                spawnedCraftType: CARRIER
                spawnedPersons: 2
                spawnedPersonType: RECRUIT
                spawnedPersonName: STR_HERO
                spawnedSoldier: {rank: 2}
                everyMultiItemList: {SUPPLY: 2, CORPSE: 1}
                everyItemList: [SUPPLY, SUPPLY]
                randomItemList: [CORPSE]
                randomMultiItemList: [{SUPPLY: 1, CORPSE: 3}]
                weightedItemList: {SUPPLY: 3, CORPSE: 1}
                researchList: [UNLOCK, VICTORY]
                invert: true
                everyMultiSoldierList: {TEMPLATE_RECRUIT: 1, RECRUIT: 2}
                randomMultiSoldierList: [{RECRUIT: 1}]
              - name: STR_PLAIN
            """).RuntimeRules;

        var payload = rules.Events[rules.Events.GetRequired("STR_GIFT")].Value.Payload;
        Assert.Equal(("STR_GIFT_DESC", "WIN_VIDEO", true, 5, -10), (payload.Description, payload.Cutscene, payload.CitySpecific,
            payload.Points, payload.Funds));
        Assert.Equal(["REGION"], payload.Regions);
        Assert.Equal(("CARRIER", 2, "RECRUIT", "STR_HERO"), (payload.SpawnedCraftType, payload.SpawnedPersons,
            payload.SpawnedPersonType, payload.SpawnedPersonName));
        Assert.Equal(2, payload.SpawnedSoldierTemplate!.Integers["rank"]);
        // std::map<std::string, ...> keys iterate in byte order; lists keep their order.
        Assert.Equal(["CORPSE", "SUPPLY"], payload.EveryMultiItems.Select(static pair => pair.Key));
        Assert.Equal([1, 2], payload.EveryMultiItems.Select(static pair => pair.Value));
        Assert.Equal(["SUPPLY", "SUPPLY"], payload.EveryItems);
        Assert.Equal(["CORPSE"], payload.RandomItems);
        Assert.Equal(["CORPSE", "SUPPLY"], Assert.Single(payload.RandomMultiItems).Select(static pair => pair.Key));
        Assert.Equal(["CORPSE", "SUPPLY"], payload.WeightedItems.Select(static pair => pair.Key));
        Assert.Equal(["UNLOCK", "VICTORY"], payload.Research);
        Assert.True(payload.Invert);
        Assert.Equal(["RECRUIT", "TEMPLATE_RECRUIT"], payload.EveryMultiSoldiers.Select(static pair => pair.Key));
        Assert.Equal(["RECRUIT"], Assert.Single(payload.RandomMultiSoldiers).Select(static pair => pair.Key));

        var plain = rules.Events[rules.Events.GetRequired("STR_PLAIN")].Value.Payload;
        Assert.Equal(("", "", false, 0, 0, false), (plain.Description, plain.Cutscene, plain.CitySpecific, plain.Points,
            plain.Funds, plain.Invert));
        Assert.Null(plain.SpawnedSoldierTemplate);
        Assert.Empty(plain.Research);
    }

    [Fact]
    public void MonthlyAndEventOptionsDefaultPersistAndCanBeFixed()
    {
        var defaults = new CampaignOptions();
        // Options::create: psiStrengthEval false, oxceGeoscapeEventsInstantDelivery true, autosave true.
        Assert.Equal((false, true, true), (defaults.PsiStrengthEval, defaults.GeoscapeEventsInstantDelivery, defaults.Autosave));

        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Monthly options") with
        {
            Options = defaults with { PsiStrengthEval = true, GeoscapeEventsInstantDelivery = false, Autosave = false },
        };
        var reloaded = Reload(content, CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(3)), 3, "monthly-options.sav");
        Assert.Equal((true, false, false), (reloaded.Options.PsiStrengthEval, reloaded.Options.GeoscapeEventsInstantDelivery,
            reloaded.Options.Autosave));

        var forced = defaults.WithFixedUserOptions(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["psiStrengthEval"] = "true",
            ["oxceGeoscapeEventsInstantDelivery"] = "false",
            ["autosave"] = "false",
        });
        Assert.Equal((true, false, false), (forced.PsiStrengthEval, forced.GeoscapeEventsInstantDelivery, forced.Autosave));
    }

    [Fact]
    public void TheDebtWarningIsReadAndAlwaysWritten()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var text = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content, "Debt warning"));
        // SavedGame::save writes warned even when it is false; a save without it loads unwarned.
        Assert.Contains("\nwarned: false\n", text, StringComparison.Ordinal);
        Assert.False(TestFixtures.LoadLogisticsSave(text.Replace("\nwarned: false\n", "\n", StringComparison.Ordinal), content,
            name: "no-warned.sav").Campaign.Warned);

        var warned = TestFixtures.LoadLogisticsSave(text.Replace("\nwarned: false\n", "\nwarned: true\n", StringComparison.Ordinal),
            content, name: "warned.sav").Campaign;
        Assert.True(warned.Warned);
        Assert.True(Reload(content, warned, 3, "warned-again.sav").Warned);
    }

    [Fact]
    public void ReferenceSaveOptionsSnapshotSeedsMonthlyAndEventOptions()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var lines = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content, "Reference monthly options")).Split('\n').ToList();
        var start = lines.FindIndex(line => line == "oxcePortOptions:");
        Assert.True(start >= 0);
        var end = lines.FindIndex(start + 1, line => line.Length == 0 || !line.StartsWith(' '));
        if (end < 0) end = lines.Count;
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, ["options:", "  psiStrengthEval: true", "  oxceGeoscapeEventsInstantDelivery: false", "  autosave: false"]);

        var options = TestFixtures.LoadLogisticsSave(string.Join('\n', lines), content, name: "reference-monthly-options.sav").Campaign.Options;

        Assert.Equal((true, false, false), (options.PsiStrengthEval, options.GeoscapeEventsInstantDelivery, options.Autosave));
    }
}
