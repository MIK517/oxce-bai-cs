using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
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
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "fixtures/expected/savegames/strategic-research-production.expected.json")));
        Assert.Equal("4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15",
            oracle.RootElement.GetProperty("referenceCommit").GetString());
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Economy", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));

        var started = Assert.IsType<CampaignResearchChanged>(Assert.Single(campaign.Execute(
            new ConfigureResearchProject(0, "THEORY", 3)).Events));
        Assert.Equal(3, started.Assigned);
        var active = Assert.Single(campaign.Capture().Bases[0].Research);
        Assert.InRange(active.Cost, 5, 15);
        Assert.Equal(oracle.RootElement.GetProperty("research")[1].GetString(),
            campaign.QueryResearchProduction(0).Research[0].Progress);
        Assert.False(campaign.Capture().Bases[0].Items.ContainsKey("SPECIMEN"));

        var primed = campaign.Capture();
        var primedBase = primed.Bases[0] with
        {
            Research = [active with { Spent = Math.Max(0, active.Cost - active.Assigned) }],
        };
        campaign = CampaignState.Restore(primed with
        {
            Time = primed.Time with { Hour = 23, Minute = 59, Second = 55 },
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
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(running), "economy.sav", content,
            new SplitMix64RandomSource(0),
            new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
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
    public void HeldItemCancellationAndFailedAllocationAreAtomic()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Economy", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(7), SystemCampaignClock.Instance);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
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
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Economy", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(9), SystemCampaignClock.Instance);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
        campaign.Execute(new ConfigureResearchProject(0, "THEORY", 1));
        var sourceText = OxceSaveAdapter.EmitNewCampaign(campaign.Capture()).Replace(
            "project: THEORY", "project: THEORY\n        futureResearchField: retained", StringComparison.Ordinal);
        var loaded = OxceSaveAdapter.Load(sourceText, "future.sav", content, new SplitMix64RandomSource(0),
            new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.Contains("futureResearchField: retained", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyInfiniteProductionMigratesAndKeyboardUiStartsResearch()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Economy", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(11), SystemCampaignClock.Instance);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));

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

        var snapshot = campaign.Capture();
        var baseSnapshot = snapshot.Bases[0] with
        {
            Research = [],
            Productions =
            [
                new("PRODUCT", 0, 0, int.MaxValue, false, false, false,
                    new Dictionary<string, int>(StringComparer.Ordinal)),
            ],
        };
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(snapshot with { Bases = [baseSnapshot] }),
            "legacy-infinite.sav", content, new SplitMix64RandomSource(0),
            new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
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
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Economy", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(13), SystemCampaignClock.Instance);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
        campaign.Execute(new ConfigureResearchProject(0, "UNLOCK", 1));
        campaign.Execute(new ConfigureResearchProject(0, "THEORY", 2));
        var primed = campaign.Capture();
        campaign = CampaignState.Restore(primed with
        {
            Time = primed.Time with { Hour = 23, Minute = 59, Second = 55 },
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
    public void ResearchDisableCanBeReenabledWithoutDiscardingTheActiveProject()
    {
        var content = StrategicReadinessTestContent.Load("strategic-research-production.rul");
        var campaign = NewCampaign(content, 17);
        campaign.Execute(new ConfigureResearchProject(0, "TARGET", 1));
        campaign.Execute(new ConfigureResearchProject(0, "DISABLER", 1));
        campaign = PrimeResearch(campaign, content, "DISABLER");

        campaign.Execute(new AdvanceCampaignTime(1));
        var disabled = campaign.Capture();
        Assert.Equal(2, disabled.ResearchRuleStatus["TARGET"]);
        Assert.Contains(disabled.Bases[0].Research, project => project.RuleId == "TARGET");

        campaign.Execute(new ConfigureResearchProject(0, "REENABLER", 1));
        campaign = PrimeResearch(campaign, content, "REENABLER");
        campaign.Execute(new AdvanceCampaignTime(1));
        var reenabled = campaign.Capture();
        Assert.Equal(0, reenabled.ResearchRuleStatus["TARGET"]);
        Assert.Contains(reenabled.Bases[0].Research, project => project.RuleId == "TARGET");
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
            Time = snapshot.Time with { Hour = 23, Minute = 59, Second = 55 },
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
                        new("RANDOM_PRODUCT", 0, 0, 2, false, false, true,
                            new Dictionary<string, int>(StringComparer.Ordinal)),
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

    private static CampaignState NewCampaign(Oxce.Mods.Rulesets.Content.RuntimeContent content, ulong seed)
    {
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Economy", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(seed), SystemCampaignClock.Instance);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
        return campaign;
    }

    private static CampaignState PrimeResearch(
        CampaignState campaign, Oxce.Mods.Rulesets.Content.RuntimeContent content, string ruleId)
    {
        var snapshot = campaign.Capture();
        var owner = snapshot.Bases[0];
        return CampaignState.Restore(snapshot with
        {
            Time = snapshot.Time with { Hour = 23, Minute = 59, Second = 55 },
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
}
