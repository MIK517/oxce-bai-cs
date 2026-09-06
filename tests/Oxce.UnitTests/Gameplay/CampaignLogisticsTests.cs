using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignLogisticsTests
{
    [Fact]
    public void RecruitsAreGeneratedBeforeTransitAndReloadDoesNotReroll()
    {
        var content = LoadFixture();
        var campaign = Create(content);
        var quote = Quote(campaign);
        var recruit = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Soldier);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(recruit.Id, 2)])).Events));
        var transit = campaign.Capture();
        Assert.Equal(2, transit.Bases[0].Transfers.Count);
        Assert.All(transit.Bases[0].Transfers, t => Assert.Equal("Alex Example", t.Soldier!.Personal!.Name));
        Assert.NotEqual(transit.Bases[0].Transfers[0].Soldier!.Id, transit.Bases[0].Transfers[1].Soldier!.Id);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "recruits.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(transit, loaded.Campaign.Capture(), strict: true);
        var randomBefore = loaded.Campaign.Capture().RandomState;
        Assert.Single(loaded.Campaign.Execute(new AdvanceCampaignTime(10)).Events.OfType<SuppliesArrived>());
        var arrived = loaded.Campaign.Capture();
        Assert.Equal(randomBefore, arrived.RandomState);
        Assert.Equivalent(transit.Bases[0].Transfers.Select(t => t.Soldier).ToArray(), arrived.Bases[0].Soldiers, strict: true);
    }

    [Fact]
    public void PurchaseReservesCapacityAndArrivesOnceAfterRestore()
    {
        var content = LoadFixture();
        var campaign = Create(content);
        var quote = Quote(campaign);
        var supply = Assert.Single(quote.Rows, r => r.RuleId == "SUPPLY");
        Assert.Equal(149, supply.UnitCost);
        Assert.Equal(8, supply.MaximumQuantity);
        var locked = Assert.Single(quote.Rows, r => r.RuleId == "LOCKED");
        Assert.Equal(0, locked.MaximumQuantity);
        var before = campaign.Capture();
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 3)])).Events));
        var ordered = campaign.Capture();
        Assert.Equal(before.Funds[0] - 447, ordered.Funds[0]);
        Assert.Equal(2, ordered.Bases[0].Items["SUPPLY"]);
        Assert.Equal(3, ordered.MonthlyPurchaseLog["SUPPLY"]);
        Assert.Equal(5, Quote(campaign).Rows.Single(r => r.RuleId == "SUPPLY").MaximumQuantity);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(ordered), "order.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var restored = loaded.Campaign;
        Assert.Equivalent(ordered, restored.Capture(), strict: true);
        var arrival = restored.Execute(new AdvanceCampaignTime(100));
        Assert.Single(arrival.Events.OfType<SuppliesArrived>());
        Assert.Equal(1, Assert.Single(arrival.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        Assert.Equal(5, restored.Capture().Bases[0].Items["SUPPLY"]);
        Assert.Empty(restored.Capture().Bases[0].Transfers);
        Assert.Empty(restored.Execute(new AdvanceCampaignTime(720)).Events.OfType<SuppliesArrived>());
        Assert.Equal(5, restored.Capture().Bases[0].Items["SUPPLY"]);
    }

    [Fact]
    public void CombinedOverCapacityAndDuplicateOrdersDoNotMutateState()
    {
        var campaign = Create(LoadFixture());
        var quote = Quote(campaign);
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        var bulky = quote.Rows.Single(r => r.RuleId == "BULKY");
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 5), new(bulky.Id, 3)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 1), new(supply.Id, 1)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ExhaustedTransferIdsRejectWholeOrderAndSellingStillWorks()
    {
        var content = LoadFixture();
        var before = Create(content).Capture() with
        {
            NextIds = new Dictionary<string, int> { ["oxcePortTransfer"] = int.MaxValue },
        };
        var campaign = CampaignState.Restore(before, content, new SplitMix64RandomSource(0));
        var quote = Quote(campaign);
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 1)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        quote = Quote(campaign, LogisticsOperation.Sell);
        supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 2)])).Events));
        Assert.Equal(before.Funds[0] + 200, campaign.Capture().Funds[0]);
        Assert.False(campaign.Capture().Bases[0].Items.ContainsKey("SUPPLY"));
    }

    private static LogisticsQuote Quote(CampaignState campaign, LogisticsOperation operation = LogisticsOperation.Purchase) =>
        Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(0, operation)).Events)).Quote;

    [Fact]
    public void StaffAndItemsMoveBetweenBasesThroughSavedTransit()
    {
        var content = LoadFixture();
        var initial = Create(content).Capture();
        var origin = initial.Bases[0] with { Name = "Alpha" };
        var destination = origin with { Id = 7, Name = "Beta", Items = new Dictionary<string, int>(), Scientists = 0, Engineers = 0 };
        var campaign = CampaignState.Restore(initial with { Bases = [origin, destination] }, content, new SplitMix64RandomSource(0));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(0, LogisticsOperation.Transfer, 7)).Events)).Quote;
        var scientist = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Scientist);
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        var completed = Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(scientist.Id, 1), new(supply.Id, 2)])).Events));
        Assert.Equal(1, completed.Cost); // Reference minimum even for colocated bases.
        var transit = campaign.Capture();
        Assert.Equal(0, transit.Bases[0].Scientists);
        Assert.Empty(transit.Bases[0].Items);
        Assert.Equal(2, transit.Bases[1].Transfers.Count);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "transit.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var result = loaded.Campaign.Execute(new AdvanceCampaignTime(5 * 720 + 1));
        Assert.Single(result.Events.OfType<SuppliesArrived>());
        var arrived = loaded.Campaign.Capture();
        Assert.Equal(1, arrived.Bases[1].Scientists);
        Assert.Equal(2, arrived.Bases[1].Items["SUPPLY"]);
        Assert.Empty(arrived.Bases[1].Transfers);
        var reloaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitLoadedCampaign(arrived, loaded.Source), "arrived.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(arrived, reloaded.Campaign.Capture(), strict: true);
    }

    private static CampaignState Create(RuntimeContent content) => CampaignFactory.Create(content,
        CampaignFoundationTests.Request() with { MasterId = "logistics", ActiveMods = ["logistics"] },
        new SplitMix64RandomSource(42), new CampaignFoundationTests.FixedClock());

    internal static RuntimeContent LoadFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var discovery = ModDiscovery.ScanDirectory(Path.Combine(root.FullName, "fixtures", "public", "mods", "strategic-logistics"));
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new ModActivation("logistics", true)],
            "logistics", new ModEngineIdentity("Extended", "8.6.1.0"));
        var snapshot = ContentSnapshotBuilder.Build(plan);
        Assert.True(snapshot.Content.Capabilities.Has(ContentLoadStage.RuntimeLinked),
            string.Join(Environment.NewLine, snapshot.Diagnostics.Select(d => d.Message)));
        return snapshot.Content;
    }
}
