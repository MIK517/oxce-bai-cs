using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicSalesFixtureTests
{
    [Fact]
    public void CriticalSaleCountsLauncherAndClipsWhenTheyUseTheSameItem()
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Overlapping sale inventory", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance).Capture();
        var rules = content.RuntimeRules;
        var ship = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        {
            Status = "STR_READY",
            Weapons = [new("OVERLAP_WEAPON", 12), null],
        };
        var campaign = CampaignState.Restore(initial with
        {
            Options = new(StorageLimitsEnforced: true),
            Bases = [initial.Bases[0] with { Name = "Alpha", Crafts = [new("SHIP", 1) { Logistics = ship }], Items = new Dictionary<string, int>() }],
        }, content, new SplitMix64RandomSource(0));

        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(0, LogisticsOperation.Sell)).Events)).Quote;
        var supply = quote.Rows.Single(row => row.RuleId == "SUPPLY");
        Assert.Equal(13, supply.Owned);
        var before = campaign.Capture();

        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 13)])).Events));
        var sold = campaign.Capture();
        Assert.All(sold.Bases[0].Crafts[0].Logistics!.Weapons, Assert.Null);
        Assert.False(sold.Bases[0].Items.ContainsKey("SUPPLY"));
        Assert.Equal(checked(before.Funds[^1] + 13L * supply.UnitCost), sold.Funds[^1]);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(sold), "overlapping-sale.sav", content,
            new SplitMix64RandomSource(1), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(sold, loaded.Campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SaleBlocksAccountingOverflowAtomically(bool fundsOverflow, bool incomeOverflow)
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Sale overflow", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance).Capture();
        var campaign = CampaignState.Restore(initial with
        {
            Funds = [fundsOverflow ? long.MaxValue : 0],
            Incomes = [incomeOverflow ? long.MaxValue : 0],
            Bases = [initial.Bases[0] with { Name = "Alpha", Items = new Dictionary<string, int> { ["SUPPLY"] = 1 } }],
        }, content, new SplitMix64RandomSource(0));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(0, LogisticsOperation.Sell)).Events)).Quote;
        var supply = quote.Rows.Single(row => row.RuleId == "SUPPLY");
        var before = campaign.Capture();

        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 1)])).Events));

        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CriticalSaleConsumesBaseCargoLauncherThenTransitAndRefundsLoadedClips(bool incomingCraft, bool bonusWeapon)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var discovery = ModDiscovery.ScanDirectory(Path.Combine(root.FullName, "fixtures/public/mods/strategic-logistics"));
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new("logistics", true)], "logistics", new("Extended", "8.6.1.0"));
        var content = ContentSnapshotBuilder.Build(plan).Content;
        var initial = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Critical sale", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance).Capture();
        var rules = content.RuntimeRules;
        var craft = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        {
            Status = "STR_READY",
            Weapons = [new(bonusWeapon ? "NEGATIVE_CAPACITY" : "FIXED", 3), null],
            Items = new Dictionary<string, int> { ["SUPPLY"] = 5 },
        };
        var campaign = CampaignState.Restore(initial with
        {
            Options = new(StorageLimitsEnforced: true),
            Bases = [initial.Bases[0] with
            {
                Name = "Alpha",
                Crafts = incomingCraft ? [] : [new("SHIP", 1) { Logistics = craft }],
                Transfers = incomingCraft ?
                [
                    new(2, 1, CampaignTransferKind.Craft, "SHIP", 1, Craft: new("SHIP", 1) { Logistics = craft }),
                    new(1, 1, CampaignTransferKind.Item, "SUPPLY", 4) { PreservationKey = "incoming-supplies" },
                ] : [new(1, 1, CampaignTransferKind.Item, "SUPPLY", 4) { PreservationKey = "incoming-supplies" }],
            }],
        }, content, new SplitMix64RandomSource(0));
        var before = campaign.Capture();
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Sell)).Events)).Quote;
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        Assert.Equal(12, supply.Owned);
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 1)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        var result = Assert.Single(campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 9)])).Events);
        Assert.IsType<LogisticsOrderCompleted>(result);
        var sold = campaign.Capture();
        Assert.False(sold.Bases[0].Items.ContainsKey("SUPPLY"));
        Assert.Equal(bonusWeapon ? 1 : 3, sold.Bases[0].Items["BULKY"]);
        var remainingCraft = incomingCraft ? sold.Bases[0].Transfers.Single(t => t.Craft is not null).Craft! : sold.Bases[0].Crafts[0];
        Assert.All(remainingCraft.Logistics!.Weapons, Assert.Null);
        Assert.Empty(remainingCraft.Logistics.Items);
        var transfer = Assert.Single(sold.Bases[0].Transfers, t => t.Kind == CampaignTransferKind.Item);
        Assert.Equal(3, transfer.Quantity);
        Assert.Equal("incoming-supplies", transfer.PreservationKey);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(sold), "critical-sale.sav", content,
            new SplitMix64RandomSource(1), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(sold, loaded.Campaign.Capture(), strict: true);
        loaded.Campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(3, loaded.Campaign.Capture().Bases[0].Items["SUPPLY"]);
        Assert.Empty(loaded.Campaign.Capture().Bases[0].Transfers);
    }

}
