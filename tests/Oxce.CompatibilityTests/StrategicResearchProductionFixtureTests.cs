using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicResearchProductionFixtureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchUnlockManufactureAndReloadFormOneEconomyChain(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul", fromCache);
        var campaign = NewCampaign(content, 42);

        var started = Assert.IsType<CampaignResearchChanged>(Assert.Single(campaign.Execute(
            new ConfigureResearchProject(0, "THEORY", 3)).Events));
        Assert.Equal(3, started.Assigned);
        var active = Assert.Single(campaign.Capture().Bases[0].Research);
        Assert.InRange(active.Cost, 5, 15);
        Assert.Equal("STR_UNKNOWN", campaign.QueryResearchProduction(0).Research[0].Progress);
        Assert.False(campaign.Capture().Bases[0].Items.ContainsKey("SPECIMEN"));

        var primed = campaign.Capture();
        var primedBase = primed.Bases[0] with
        {
            Research = [active with { Spent = Math.Max(0, active.Cost - active.Assigned) }],
        };
        campaign = CampaignState.Restore(primed with
        {
            Time = DailyBoundary(primed.Time),
            Bases = [primedBase],
        }, content, new SplitMix64RandomSource(primed.RandomState));

        var completion = Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events
            .OfType<CampaignResearchCompleted>());
        Assert.Equal(["BONUS", "THEORY", "APPLICATION"], completion.Discoveries);
        var researched = campaign.Capture();
        Assert.Equal(["APPLICATION", "BONUS", "THEORY"], researched.CompletedResearch);
        Assert.Equal(15, researched.ResearchScores[^1]);
        Assert.Equal(2, researched.Bases[0].Items["MATERIAL"]);
        Assert.Equal(2, researched.NextIds["RESEARCH_WINS"]);
        Assert.Equal(1, researched.NextIds["RESEARCH_LOSSES"]);
        Assert.Null(campaign.QueryResearchProduction(0).ProductionChoices
            .Single(choice => choice.RuleId == "PRODUCT").UnavailableReason);

        Assert.IsType<CampaignProductionChanged>(Assert.Single(campaign.Execute(
            new ConfigureProductionProject(0, "PRODUCT", 4, 2)).Events));
        var running = campaign.Capture();
        Assert.Equal(1, running.Bases[0].Items["MATERIAL"]);
        Assert.Equal(100, running.Expenditures[^1]);
        var loaded = LoadSave(OxceSaveAdapter.EmitNewCampaign(running), "economy.sav", content);
        Assert.Equivalent(running, loaded.Campaign.Capture(), strict: true);

        var firstHour = loaded.Campaign.Execute(new AdvanceCampaignTime(720));
        Assert.Equal(1, Assert.Single(firstHour.Events.OfType<CampaignProductionProgress>()).Produced);
        var afterFirst = loaded.Campaign.Capture();
        Assert.Equal(1, afterFirst.Bases[0].Items["PRODUCT"]);
        Assert.False(afterFirst.Bases[0].Items.ContainsKey("MATERIAL"));
        Assert.Equal(200, afterFirst.Expenditures[^1]);
        Assert.Single(afterFirst.Bases[0].Productions);

        var secondHour = loaded.Campaign.Execute(new AdvanceCampaignTime(720));
        var completed = Assert.Single(secondHour.Events.OfType<CampaignProductionProgress>());
        Assert.Equal("STR_PRODUCTION_COMPLETE", completed.StopReason);
        var final = loaded.Campaign.Capture();
        Assert.Equal(2, final.Bases[0].Items["PRODUCT"]);
        Assert.Empty(final.Bases[0].Productions);
        Assert.Equal(37, final.ResearchScores[^1]);
        Assert.Equal(4, final.Bases[0].Engineers);
    }

    [Fact]
    public void ProjectProgressAndProducedAmountMatchReferenceOracle()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "fixtures/expected/savegames/strategic-research-production.expected.json")));
        var expected = oracle.RootElement;
        Assert.Equal(1, expected.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15",
            expected.GetProperty("referenceCommit").GetString());
        Assert.Equal("MSVC", expected.GetProperty("referenceBuild").GetProperty("compiler").GetString());
        Assert.Equal("c++20", expected.GetProperty("referenceBuild").GetProperty("languageStandard").GetString());
        Assert.Empty(expected.GetProperty("mods").EnumerateArray());

        var campaign = NewCampaign(content, 5);
        campaign.Execute(new ConfigureResearchProject(0, "THEORY", 3));
        var snapshot = campaign.Capture();
        var owner = snapshot.Bases[0];
        campaign = CampaignState.Restore(snapshot with
        {
            Time = DailyBoundary(snapshot.Time),
            Bases =
            [
                owner with
                {
                    Research = [Assert.Single(owner.Research) with { Spent = 0, Cost = 8 }],
                    Productions =
                    [
                        new("PRODUCT", 0, 11, 3, false, false, false, NoRandomOutput()),
                    ],
                },
            ],
        }, content, new SplitMix64RandomSource(snapshot.RandomState));

        var research = expected.GetProperty("research");
        Assert.Equal(research[0].GetInt32() != 0, CompletesResearch(campaign.Execute(new AdvanceCampaignTime(1))));
        Assert.Equal(research[1].GetString(),
            Assert.Single(campaign.QueryResearchProduction(0).Research).Progress);
        Assert.Equal(expected.GetProperty("production")[0].GetInt32(),
            Assert.Single(campaign.QueryResearchProduction(0).Productions).Produced);

        campaign = AtNextDailyBoundary(campaign, content);
        Assert.Equal(research[2].GetInt32() != 0, CompletesResearch(campaign.Execute(new AdvanceCampaignTime(1))));
        campaign = AtNextDailyBoundary(campaign, content);
        Assert.Equal(research[3].GetInt32() != 0, CompletesResearch(campaign.Execute(new AdvanceCampaignTime(1))));
    }

    [Fact]
    public void HeldItemCancellationAndFailedAllocationAreAtomic()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 7);
        var before = campaign.Capture();

        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new ConfigureResearchProject(0, "THEORY", 11)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        campaign.Execute(new ConfigureResearchProject(0, "THEORY", 0));
        Assert.False(campaign.Capture().Bases[0].Items.ContainsKey("SPECIMEN"));
        Assert.IsType<CampaignResearchChanged>(Assert.Single(campaign.Execute(
            new ConfigureResearchProject(0, "THEORY", 0, Cancel: true)).Events));
        Assert.Equal(1, campaign.Capture().Bases[0].Items["SPECIMEN"]);

        campaign.Execute(new ConfigureResearchProject(0, "ZERO_REWARD", 1));
        campaign = PrimeResearch(campaign, content, "ZERO_REWARD");
        var reward = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Contains(reward.Events, value => value is SuppliesArrived);
        Assert.Equal(1, campaign.Capture().Bases[0].Items["PRODUCT"]);
    }

    [Fact]
    public void ActiveProjectAndProductionUnknownFieldsSurviveOwnedSaveRewrite()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 9);
        campaign.Execute(new ConfigureResearchProject(0, "THEORY", 1));
        var snapshot = campaign.Capture();
        var source = snapshot with
        {
            Bases =
            [
                snapshot.Bases[0] with
                {
                    Productions =
                    [
                        new("PRODUCT", 1, 0, 2, false, false, false, NoRandomOutput()),
                    ],
                },
            ],
        };
        var sourceText = OxceSaveAdapter.EmitNewCampaign(source).Replace(
            "project: THEORY", "project: THEORY\n        futureResearchField: retained", StringComparison.Ordinal);
        sourceText = sourceText.Replace(
            "item: PRODUCT", "item: PRODUCT\n        futureProductionField: retained", StringComparison.Ordinal);
        var loaded = LoadSave(sourceText, "future.sav", content);
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.Contains("futureResearchField: retained", rewritten, StringComparison.Ordinal);
        Assert.Contains("futureProductionField: retained", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void NonemptyStrategicMapsSurviveSaveRoundTripAndControlAvailability()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 10);
        var snapshot = campaign.Capture() with
        {
            ResearchRuleStatus = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["TARGET"] = 2,
                ["REENABLER"] = 0,
            },
            ManufactureRuleStatus = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["PRODUCT"] = 2,
            },
            MonthlyPurchaseLog = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["PRODUCT"] = 3,
            },
        };
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));

        var loaded = LoadSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            "strategic-maps.sav", content);
        var restored = loaded.Campaign.Capture();

        Assert.Equivalent(snapshot, restored, strict: true);
        Assert.Equal("Research is permanently disabled.", loaded.Campaign.QueryResearchProduction(0)
            .ResearchChoices.Single(choice => choice.RuleId == "TARGET").UnavailableReason);
        Assert.Equal("Production is hidden.", loaded.Campaign.QueryResearchProduction(0)
            .ProductionChoices.Single(choice => choice.RuleId == "PRODUCT").UnavailableReason);
        Assert.Null(loaded.Campaign.QueryResearchProduction(0)
            .ResearchChoices.Single(choice => choice.RuleId == "REENABLER").UnavailableReason);

        loaded.Campaign.Execute(new ConfigureResearchProject(0, "REENABLER", 1));
        var reenabled = PrimeResearch(loaded.Campaign, content, "REENABLER");
        reenabled.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, reenabled.Capture().ResearchRuleStatus["TARGET"]);
        Assert.Null(reenabled.QueryResearchProduction(0)
            .ResearchChoices.Single(choice => choice.RuleId == "TARGET").UnavailableReason);
    }

    [Fact]
    public void LegacyInfiniteProductionMigratesAndKeyboardUiStartsResearch()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 11);

        var labels = new List<string>();
        var client = new CampaignLogisticsClient(new(campaign, campaign),
            localize: value => { labels.Add(value); return value; });
        Key('h');
        var choices = campaign.QueryResearchProduction(0).ResearchChoices;
        var theory = choices.ToList().FindIndex(choice => choice.RuleId == "THEORY");
        Assert.True(theory >= 0);
        for (var index = 0; index < theory; index++) Key(0x40000051);
        Key(13);
        Assert.Equal("THEORY", Assert.Single(campaign.Capture().Bases[0].Research).RuleId);
        Assert.Contains(labels, label => label.StartsWith("Research  Scientists", StringComparison.Ordinal));
        Key('m');
        Assert.Contains(labels, label => label.StartsWith("Production  Engineers", StringComparison.Ordinal));
        var priorLabels = labels.Count;
        Key('r');
        Assert.Contains(labels.Skip(priorLabels), label =>
            label.StartsWith("Hourly service.", StringComparison.Ordinal));
        priorLabels = labels.Count;
        Key('a');
        Assert.Contains(labels.Skip(priorLabels), label =>
            label.StartsWith("Base layout cursor", StringComparison.Ordinal));
        priorLabels = labels.Count;
        Key('u');
        Assert.Contains(labels.Skip(priorLabels), label =>
            label.StartsWith("Up/Down: Soldier", StringComparison.Ordinal));
        priorLabels = labels.Count;
        Key('h');
        Assert.Contains(labels.Skip(priorLabels), label =>
            label.StartsWith("Research  Scientists", StringComparison.Ordinal));
        // Global keys stay available inside project views.
        var timeBefore = campaign.Capture().Time;
        Key(' ');
        Assert.NotEqual(timeBefore, campaign.Capture().Time);
        Key('h');
        Key('i');
        Assert.NotNull(client.Screen);

        var snapshot = campaign.Capture();
        var baseSnapshot = snapshot.Bases[0] with
        {
            Research = [],
            Productions =
            [
                new("PRODUCT", 0, 0, int.MaxValue, false, false, false, NoRandomOutput()),
            ],
        };
        var loaded = LoadSave(OxceSaveAdapter.EmitNewCampaign(snapshot with { Bases = [baseSnapshot] }),
            "legacy-infinite.sav", content);
        var migrated = Assert.Single(loaded.Campaign.Capture().Bases[0].Productions);
        Assert.Equal(999, migrated.Amount);
        Assert.True(migrated.Infinite);
        Assert.True(migrated.Sell);

        void Key(uint code) => client.HandleInput(GameInputEvent.Key(
            GameInputEventKind.KeyPressed, 0, 0, 0, code, InputKeyModifiers.None));
    }

    [Fact]
    public void MultipleResearchCompletionsShareOneBoundaryAndProductionFailureIsAtomic()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 13);
        campaign.Execute(new ConfigureResearchProject(0, "UNLOCK", 1));
        campaign.Execute(new ConfigureResearchProject(0, "THEORY", 2));
        var primed = campaign.Capture();
        campaign = CampaignState.Restore(primed with
        {
            Time = DailyBoundary(primed.Time),
            Bases =
            [
                primed.Bases[0] with
                {
                    Research = primed.Bases[0].Research.Select(project =>
                        project with { Spent = project.Cost - project.Assigned }).ToArray(),
                },
            ],
        }, content, new SplitMix64RandomSource(primed.RandomState));

        var completed = campaign.Execute(new AdvanceCampaignTime(1)).Events
            .OfType<CampaignResearchCompleted>().ToArray();
        Assert.Equal(["UNLOCK", "THEORY"], completed.Select(value => value.RuleId));

        var ready = campaign.Capture();
        campaign = CampaignState.Restore(ready with { Funds = [0] }, content,
            new SplitMix64RandomSource(ready.RandomState));
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new ConfigureProductionProject(0, "PRODUCT", 1, 1)).Events));
        Assert.Equal("STR_NOT_ENOUGH_MONEY", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ResearchDisableCleanupRunsAfterSameBoundaryReenables()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 17);
        campaign.Execute(new ConfigureResearchProject(0, "TARGET", 1));
        campaign.Execute(new ConfigureResearchProject(0, "DISABLER", 1));
        campaign.Execute(new ConfigureResearchProject(0, "REENABLER", 1));
        campaign = PrimeResearch(PrimeResearch(campaign, content, "DISABLER"), content, "REENABLER");

        campaign.Execute(new AdvanceCampaignTime(1));
        var sameBoundary = campaign.Capture();
        Assert.Equal(0, sameBoundary.ResearchRuleStatus["TARGET"]);
        Assert.Contains(sameBoundary.Bases[0].Research, project => project.RuleId == "TARGET");

        var later = NewCampaign(content, 18);
        later.Execute(new ConfigureResearchProject(0, "TARGET", 1));
        later.Execute(new ConfigureResearchProject(0, "DISABLER", 1));
        later = PrimeResearch(later, content, "DISABLER");
        later.Execute(new AdvanceCampaignTime(1));
        var disabled = later.Capture();
        Assert.Equal(2, disabled.ResearchRuleStatus["TARGET"]);
        Assert.DoesNotContain(disabled.Bases[0].Research, project => project.RuleId == "TARGET");

        later.Execute(new ConfigureResearchProject(0, "REENABLER", 1));
        later = PrimeResearch(later, content, "REENABLER");
        later.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, later.Capture().ResearchRuleStatus["TARGET"]);
        Assert.Null(later.QueryResearchProduction(0).ResearchChoices.Single(choice =>
            choice.RuleId == "TARGET").UnavailableReason);
    }

    [Fact]
    public void DuplicateCrossBaseResearchAppliesPrimarySideEffectsOnce()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 19);
        var snapshot = campaign.Capture();
        var project = new ResearchProjectSnapshot("DUPLICATE_REWARD", 1, 0, 1);
        var alpha = snapshot.Bases[0] with { Scientists = 2, Research = [project] };
        var beta = snapshot.Bases[0] with
        {
            Id = 1,
            Name = "Beta",
            Scientists = 2,
            Research = [project],
        };
        campaign = CampaignState.Restore(snapshot with
        {
            Time = DailyBoundary(snapshot.Time),
            Bases = [alpha, beta],
        }, content, new SplitMix64RandomSource(snapshot.RandomState));

        campaign.Execute(new AdvanceCampaignTime(1));
        var completed = campaign.Capture();
        Assert.DoesNotContain(completed.Bases.SelectMany(baseState => baseState.Research),
            value => value.RuleId == "DUPLICATE_REWARD");
        Assert.Equal(1, completed.Bases.Sum(baseState =>
            baseState.Items.GetValueOrDefault("MATERIAL")));
        Assert.Equal(2, completed.NextIds["DUPLICATE_REWARD_COUNT"]);
        Assert.Equal(3, completed.Bases[1].Scientists);
    }

    [Fact]
    public void ProductionHonorsFallbackQuartersPricesBookkeepingAndCraftUnload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");

        var fallback = NewCampaign(content, 23);
        var fallbackSnapshot = fallback.Capture();
        fallback = CampaignState.Restore(fallbackSnapshot with
        {
            Bases =
            [
                fallbackSnapshot.Bases[0] with
                {
                    Productions =
                    [
                        new("RANDOM_PRODUCT", 0, 0, 2, false, false, true, NoRandomOutput()),
                    ],
                },
            ],
        }, content, new SplitMix64RandomSource(fallbackSnapshot.RandomState));
        fallback.Execute(new AdvanceCampaignTime(1));
        var fallbackResult = fallback.Capture();
        var active = Assert.Single(fallbackResult.Bases[0].Productions);
        Assert.Equal(1, active.RandomProductionInfo["PRODUCT"]);
        Assert.Equal(1, active.RandomProductionInfo["MATERIAL"]);
        Assert.Equal(1, fallbackResult.Bases[0].Items["PRODUCT"]);
        Assert.Equal(1, fallbackResult.Bases[0].Items["MATERIAL"]);

        var autosell = NewCampaign(content, 29);
        var sellSnapshot = autosell.Capture();
        var sellBase = sellSnapshot.Bases[0] with
        {
            Items = new Dictionary<string, int>(sellSnapshot.Bases[0].Items, StringComparer.Ordinal)
            {
                ["MATERIAL"] = 1,
            },
        };
        autosell = CampaignState.Restore(sellSnapshot with
        {
            CompletedResearch = ["APPLICATION"],
            Bases = [sellBase],
        }, content, new SplitMix64RandomSource(sellSnapshot.RandomState));
        var fundsBefore = autosell.Capture().Funds[^1];
        autosell.Execute(new ConfigureProductionProject(0, "PRODUCT", 4, 1, Sell: true));
        autosell.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(fundsBefore - 100 + 37, autosell.Capture().Funds[^1]);

        var living = NewCampaign(content, 31);
        var livingSnapshot = living.Capture();
        living = CampaignState.Restore(livingSnapshot with
        {
            Bases = [livingSnapshot.Bases[0] with { Scientists = 6, Engineers = 4 }],
        }, content, new SplitMix64RandomSource(livingSnapshot.RandomState));
        var livingBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(living.Execute(
            new ConfigureProductionProject(0, "MAKE_ENGINEER", 0, 1)).Events));
        Assert.Equal("STR_NOT_ENOUGH_LIVING_SPACE", livingBlocked.Reason);

        var recycling = NewCampaign(content, 37);
        var recycleSnapshot = recycling.Capture();
        var logistics = new CraftLogisticsState(0, 0, "STR_READY", [null, null],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 2 }, []);
        recycling = CampaignState.Restore(recycleSnapshot with
        {
            Bases =
            [
                recycleSnapshot.Bases[0] with
                {
                    Crafts = [new CraftSnapshot("SHIP", 1) { Logistics = logistics }],
                },
            ],
        }, content, new SplitMix64RandomSource(recycleSnapshot.RandomState));
        recycling.Execute(new ConfigureProductionProject(0, "RECYCLE_SHIP", 1, 1));
        var recycled = recycling.Capture();
        Assert.Empty(recycled.Bases[0].Crafts);
        Assert.Equal(2, recycled.Bases[0].Items["SUPPLY"]);
    }

    [Fact]
    public void ResearchUsesImplicitItemsAndReferenceRewardAvailability()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var implicitItem = NewCampaign(content, 41);
        Assert.IsType<CampaignResearchChanged>(Assert.Single(implicitItem.Execute(
            new ConfigureResearchProject(0, "IMPLICIT_ITEM", 1)).Events));
        Assert.False(implicitItem.Capture().Bases[0].Items.ContainsKey("IMPLICIT_ITEM"));

        var protectedSource = NewCampaign(content, 43);
        protectedSource.Execute(new ConfigureResearchProject(0, "PROTECTED_SOURCE", 1));
        protectedSource = PrimeResearch(protectedSource, content, "PROTECTED_SOURCE");
        protectedSource.Execute(new AdvanceCampaignTime(1));
        Assert.Equal("Research is already complete.", protectedSource.QueryResearchProduction(0)
            .ResearchChoices.Single(choice => choice.RuleId == "PROTECTED_SOURCE").UnavailableReason);

        protectedSource.Execute(new ConfigureResearchProject(0, "UNLOCK", 1));
        protectedSource = PrimeResearch(protectedSource, content, "UNLOCK");
        protectedSource.Execute(new AdvanceCampaignTime(1));
        Assert.Null(protectedSource.QueryResearchProduction(0).ResearchChoices
            .Single(choice => choice.RuleId == "PROTECTED_SOURCE").UnavailableReason);

        var repeatable = NewCampaign(content, 47);
        var repeatableSnapshot = repeatable.Capture();
        repeatable = CampaignState.Restore(repeatableSnapshot with
        {
            CompletedResearch = ["REPEATABLE_PREREQ"],
        }, content, new SplitMix64RandomSource(repeatableSnapshot.RandomState));
        repeatable.Execute(new ConfigureResearchProject(0, "REPEATABLE_SOURCE", 1));
        repeatable = PrimeResearch(repeatable, content, "REPEATABLE_SOURCE");
        repeatable.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain("REPEATABLE_ZERO", repeatable.Capture().CompletedResearch);
    }

    [Fact]
    public void ProductionRejectsCapacityAndIllegalModesWithoutMutation()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        Assert.Equal("Random production weights exceed the supported range.",
            NewCampaign(content, 51).QueryResearchProduction(0).ProductionChoices.Single(choice =>
                choice.RuleId == "OVERWEIGHT_RANDOM").UnavailableReason);
        var workshop = NewCampaign(content, 53);
        var workshopSnapshot = workshop.Capture();
        workshop = CampaignState.Restore(workshopSnapshot with
        {
            Bases =
            [
                workshopSnapshot.Bases[0] with
                {
                    Engineers = 1,
                    Productions =
                    [
                        new("PRODUCT", 9, 1, 2, false, false, false, NoRandomOutput()),
                    ],
                },
            ],
        }, content, new SplitMix64RandomSource(workshopSnapshot.RandomState));
        var beforeWorkshop = workshop.Capture();
        var workshopBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(workshop.Execute(
            new ConfigureProductionProject(0, "MAKE_AMMO", 1, 1)).Events));
        Assert.Equal("STR_NOT_ENOUGH_WORK_SPACE", workshopBlocked.Reason);
        Assert.Equivalent(beforeWorkshop, workshop.Capture(), strict: true);

        var hangar = NewCampaign(content, 59);
        var hangarSnapshot = hangar.Capture();
        var ready = new CraftLogisticsState(0, 0, "STR_READY", [null, null],
            new Dictionary<string, int>(StringComparer.Ordinal), []);
        hangar = CampaignState.Restore(hangarSnapshot with
        {
            Bases =
            [
                hangarSnapshot.Bases[0] with
                {
                    Crafts =
                    [
                        new CraftSnapshot("SHIP", 1) { Logistics = ready },
                        new CraftSnapshot("SHIP", 2) { Logistics = ready },
                    ],
                },
            ],
        }, content, new SplitMix64RandomSource(hangarSnapshot.RandomState));
        var beforeHangar = hangar.Capture();
        var hangarBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(hangar.Execute(
            new ConfigureProductionProject(0, "BUILD_SHIP", 1, 1)).Events));
        Assert.Equal("STR_NO_FREE_HANGARS_FOR_CRAFT_PRODUCTION", hangarBlocked.Reason);
        Assert.Equivalent(beforeHangar, hangar.Capture(), strict: true);

        var growing = NewCampaign(content, 60);
        growing.Execute(new ConfigureProductionProject(0, "BUILD_SHIP", 1, 1));
        var growingSnapshot = growing.Capture();
        growing = CampaignState.Restore(growingSnapshot with
        {
            Bases = [growingSnapshot.Bases[0] with
            {
                Crafts =
                [
                    new CraftSnapshot("SHIP", 1) { Logistics = ready },
                    new CraftSnapshot("SHIP", 2) { Logistics = ready },
                ],
            }],
        }, content, new SplitMix64RandomSource(growingSnapshot.RandomState));
        var beforeGrowing = growing.Capture();
        var growingBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(growing.Execute(
            new ConfigureProductionProject(0, "BUILD_SHIP", 1, 2)).Events));
        Assert.Equal("STR_NO_FREE_HANGARS_FOR_CRAFT_PRODUCTION", growingBlocked.Reason);
        Assert.Equivalent(beforeGrowing, growing.Capture(), strict: true);

        var sell = NewCampaign(content, 61);
        var beforeSell = sell.Capture();
        var sellBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(sell.Execute(
            new ConfigureProductionProject(0, "RANDOM_PRODUCT", 1, 1, Sell: true)).Events));
        Assert.Equal("Production does not support autosell.", sellBlocked.Reason);
        Assert.Equivalent(beforeSell, sell.Capture(), strict: true);

        var refund = NewCampaign(content, 63);
        var refundSnapshot = refund.Capture();
        refund = CampaignState.Restore(refundSnapshot with
        {
            Incomes = [long.MaxValue],
            Bases = [refundSnapshot.Bases[0] with
            {
                Engineers = 3,
                Productions =
                [
                    new("REFUNDABLE", 1, 0, 1, false, false, false, NoRandomOutput()),
                ],
            }],
        }, content, new SplitMix64RandomSource(refundSnapshot.RandomState));
        var beforeRefund = refund.Capture();
        var refundBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(refund.Execute(
            new ConfigureProductionProject(0, "REFUNDABLE", 0, 1, Cancel: true)).Events));
        Assert.Equal("Production accounting exceeds the supported range.", refundBlocked.Reason);
        Assert.Equivalent(beforeRefund, refund.Capture(), strict: true);
    }

    [Fact]
    public void ProductionStaffingReservesWorkshopSpaceAndCraftHangars()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var staffed = NewCampaign(content, 57);
        var staffedSnapshot = staffed.Capture();
        staffed = CampaignState.Restore(staffedSnapshot with
        {
            Bases = [staffedSnapshot.Bases[0] with
            {
                Scientists = 0,
                Engineers = 5,
                Productions = [new("PRODUCT", 5, 1, 2, false, false, false, NoRandomOutput())],
            }],
        }, content, new SplitMix64RandomSource(staffedSnapshot.RandomState));
        var beforeStaffing = staffed.Capture();
        // Ten workshops hold the one-space project plus at most nine engineers.
        var overstaffed = Assert.IsType<CampaignActionBlocked>(Assert.Single(staffed.Execute(
            new ConfigureProductionProject(0, "PRODUCT", 10, 2)).Events));
        Assert.Equal("STR_NOT_ENOUGH_WORK_SPACE", overstaffed.Reason);
        Assert.Equivalent(beforeStaffing, staffed.Capture(), strict: true);
        Assert.IsType<CampaignProductionChanged>(Assert.Single(staffed.Execute(
            new ConfigureProductionProject(0, "PRODUCT", 9, 2)).Events));
        var fullyStaffed = staffed.Capture().Bases[0];
        Assert.Equal(9, Assert.Single(fullyStaffed.Productions).Assigned);
        Assert.Equal(1, fullyStaffed.Engineers);

        var hangars = NewCampaign(content, 58);
        var beforeHangars = hangars.Capture();
        var oversized = Assert.IsType<CampaignActionBlocked>(Assert.Single(hangars.Execute(
            new ConfigureProductionProject(0, "BUILD_SHIP", 0, 3)).Events));
        Assert.Equal("STR_NO_FREE_HANGARS_FOR_CRAFT_PRODUCTION", oversized.Reason);
        var infinite = Assert.IsType<CampaignActionBlocked>(Assert.Single(hangars.Execute(
            new ConfigureProductionProject(0, "BUILD_SHIP", 0, 1, Infinite: true)).Events));
        Assert.Equal("Craft production cannot be infinite.", infinite.Reason);
        Assert.Equivalent(beforeHangars, hangars.Capture(), strict: true);

        Assert.IsType<CampaignProductionChanged>(Assert.Single(hangars.Execute(
            new ConfigureProductionProject(0, "BUILD_SHIP", 0, 2)).Events));
        var reserved = hangars.Capture();
        // Both hangars are now reserved by pending units, so the queue cannot grow further.
        var grown = Assert.IsType<CampaignActionBlocked>(Assert.Single(hangars.Execute(
            new ConfigureProductionProject(0, "BUILD_SHIP", 0, 3)).Events));
        Assert.Equal("STR_NO_FREE_HANGARS_FOR_CRAFT_PRODUCTION", grown.Reason);
        Assert.Equivalent(reserved, hangars.Capture(), strict: true);
    }

    [Fact]
    public void ProductionIgnoresAirborneCraftMaterialsAndReusesImmediateAmmo()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var airborne = NewCampaign(content, 67);
        var airborneSnapshot = airborne.Capture();
        var outLogistics = new CraftLogisticsState(0, 0, "STR_OUT", [null, null],
            new Dictionary<string, int>(StringComparer.Ordinal), []);
        airborne = CampaignState.Restore(airborneSnapshot with
        {
            Bases = [airborneSnapshot.Bases[0] with
            {
                Crafts = [new CraftSnapshot("SHIP", 1) { Logistics = outLogistics }],
            }],
        }, content, new SplitMix64RandomSource(airborneSnapshot.RandomState));
        var beforeAirborne = airborne.Capture();
        var materialBlocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(airborne.Execute(
            new ConfigureProductionProject(0, "RECYCLE_SHIP", 1, 1)).Events));
        Assert.Equal("STR_NOT_ENOUGH_MATERIALS", materialBlocked.Reason);
        Assert.Equivalent(beforeAirborne, airborne.Capture(), strict: true);

        var ammunition = NewCampaign(content, 71);
        var ammunitionSnapshot = ammunition.Capture();
        var weapon = new CraftWeaponSnapshot("FIXED", 0);
        var armedLogistics = new CraftLogisticsState(0, 0, "STR_READY", [weapon, null],
            new Dictionary<string, int>(StringComparer.Ordinal), []);
        ammunition = CampaignState.Restore(ammunitionSnapshot with
        {
            Bases = [ammunitionSnapshot.Bases[0] with
            {
                Crafts = [new CraftSnapshot("SHIP", 1) { Logistics = armedLogistics }],
            }],
        }, content, new SplitMix64RandomSource(ammunitionSnapshot.RandomState));
        ammunition.Execute(new ConfigureProductionProject(0, "MAKE_AMMO", 1, 1));
        ammunition.Execute(new AdvanceCampaignTime(1));
        var manufactured = Assert.Single(ammunition.Capture().Bases[0].Crafts).Logistics!;
        Assert.Equal("STR_REARMING", manufactured.Status);
        Assert.True(manufactured.Weapons[0]!.Rearming);
    }

    [Fact]
    public void UnknownSavedProjectsAreDroppedAndReleaseTheirStaff()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var snapshot = NewCampaign(content, 79).Capture();
        var owner = snapshot.Bases[0] with
        {
            Scientists = 1,
            Engineers = 1,
            Research = [new("UNLOCK", 1, 0, 1), new("RETIRED_TOPIC", 2, 0, 5)],
            Productions = [new("RETIRED_ITEM", 3, 0, 1, false, false, false, NoRandomOutput())],
        };

        var loaded = LoadSave(OxceSaveAdapter.EmitNewCampaign(snapshot with { Bases = [owner] }),
            "retired.sav", content);

        // Base::load drops unknown projects and returns their assigned staff.
        var restored = loaded.Campaign.Capture().Bases[0];
        Assert.Equal("UNLOCK", Assert.Single(restored.Research).RuleId);
        Assert.Empty(restored.Productions);
        Assert.Equal(3, restored.Scientists);
        Assert.Equal(4, restored.Engineers);
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.DoesNotContain("RETIRED_", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoredInvalidProductionStopsTimeBeforeProgress()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var snapshot = NewCampaign(content, 83).Capture();
        var campaign = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with
            {
                Productions = [new("OVERWEIGHT_RANDOM", 1, 0, 1, false, false, false, NoRandomOutput())],
            }],
        }, content, new SplitMix64RandomSource(snapshot.RandomState));

        var result = campaign.Execute(new AdvanceCampaignTime(720));

        var blocked = Assert.Single(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal("Random production weights exceed the supported range.", blocked.Reason);
        var production = Assert.Single(campaign.Capture().Bases[0].Productions);
        Assert.Equal(0, production.Spent);
    }

    [Fact]
    public void InvalidResearchAndProductionProjectsFailBeforePublication()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var snapshot = NewCampaign(content, 73).Capture();
        var owner = snapshot.Bases[0];

        Reject(owner with { Research = [new("THEORY", 1, -1, 10)] });
        Reject(owner with
        {
            Productions =
            [
                new("PRODUCT", 1, 0, -1, false, false, false, NoRandomOutput()),
            ],
        });
        Reject(owner with { Scientists = 0, Research = [new("THEORY", 11, 0, 10)] });
        Reject(owner with
        {
            Engineers = 0,
            Productions =
            [
                new("PRODUCT", 10, 0, 1, false, false, false, NoRandomOutput()),
            ],
        });

        Reject(owner with { Research = [new("UNLOCK", 0, 0, 1), new("UNLOCK", 0, 0, 1)] });
        Reject(owner with
        {
            Productions =
            [
                new("PRODUCT", 0, 0, 1, false, false, false, NoRandomOutput()),
                new("PRODUCT", 0, 0, 2, false, false, false, NoRandomOutput()),
            ],
        });

        void Reject(BaseSnapshot invalidBase) => Assert.Throws<InvalidDataException>(() =>
            CampaignState.Restore(snapshot with { Bases = [invalidBase] }, content,
                new SplitMix64RandomSource(snapshot.RandomState)));
    }

    private static CampaignState NewCampaign(Oxce.Mods.Rulesets.Content.RuntimeContent content, ulong seed)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Economy", seed: seed);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
        return campaign;
    }

    private static LoadedOxceCampaign LoadSave(string text, string name, Oxce.Mods.Rulesets.Content.RuntimeContent content) =>
        OxceSaveAdapter.Load(text, name, content, new SplitMix64RandomSource(0),
            TestFixtures.LogisticsSaveOptions());

    private static Dictionary<string, int> NoRandomOutput() => new(StringComparer.Ordinal);

    private static CampaignState PrimeResearch(
        CampaignState campaign, Oxce.Mods.Rulesets.Content.RuntimeContent content, string ruleId)
    {
        var snapshot = campaign.Capture();
        var owner = snapshot.Bases[0];
        return CampaignState.Restore(snapshot with
        {
            Time = DailyBoundary(snapshot.Time),
            Bases =
            [
                owner with
                {
                    Research = owner.Research.Select(project => project.RuleId == ruleId
                        ? project with { Spent = project.Cost - project.Assigned }
                        : project).ToArray(),
                },
            ],
        }, content, new SplitMix64RandomSource(snapshot.RandomState));
    }

    private static CampaignState AtNextDailyBoundary(
        CampaignState campaign, Oxce.Mods.Rulesets.Content.RuntimeContent content)
    {
        var snapshot = campaign.Capture();
        return CampaignState.Restore(snapshot with { Time = DailyBoundary(snapshot.Time) },
            content, new SplitMix64RandomSource(snapshot.RandomState));
    }

    private static CampaignTime DailyBoundary(CampaignTime time) =>
        time with { Hour = 23, Minute = 59, Second = 55 };

    private static bool CompletesResearch(CampaignCommandResult result) =>
        result.Events.Any(value => value is CampaignResearchCompleted);
}
