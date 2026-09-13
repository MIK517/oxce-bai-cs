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
}
