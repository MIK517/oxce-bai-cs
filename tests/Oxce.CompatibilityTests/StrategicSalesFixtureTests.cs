using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicSalesFixtureTests
{
    [Fact]
    public void CriticalSaleCountsLauncherAndClipsWhenTheyUseTheSameItem()
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = TestFixtures.CreateLogisticsCampaign(content, "Overlapping sale inventory").Capture();
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
        var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(sold), content, seed: 1, name: "overlapping-sale.sav");
        Assert.Equivalent(sold, loaded.Campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SaleBlocksAccountingOverflowAtomically(bool fundsOverflow, bool incomeOverflow)
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = TestFixtures.CreateLogisticsCampaign(content, "Sale overflow").Capture();
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

    [Fact]
    public void CriticalSaleQuoteBlocksAggregateInventoryOverflowAtomically()
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = TestFixtures.CreateLogisticsCampaign(content, "Sale inventory overflow").Capture();
        var rules = content.RuntimeRules;
        var ship = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        {
            Status = "STR_READY",
            Weapons = [new("OVERLAP_WEAPON", 0), null],
            Items = new Dictionary<string, int> { ["BULKY"] = 6 },
        };
        var campaign = CampaignState.Restore(initial with
        {
            Options = new(StorageLimitsEnforced: true),
            Bases = [initial.Bases[0] with
            {
                Name = "Alpha",
                Crafts = [new("SHIP", 1) { Logistics = ship }],
                Items = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            }],
        }, content, new SplitMix64RandomSource(0));
        var before = campaign.Capture();

        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(0, LogisticsOperation.Sell)).Events));

        Assert.Equal("Sale inventory exceeds the supported quantity range.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CriticalSaleConsumesBaseCargoLauncherThenTransitAndRefundsLoadedClips(bool incomingCraft, bool bonusWeapon)
    {
        var content = TestFixtures.LoadStrategicLogistics();
        var initial = TestFixtures.CreateLogisticsCampaign(content, "Critical sale").Capture();
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
        var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(sold), content, seed: 1, name: "critical-sale.sav");
        Assert.Equivalent(sold, loaded.Campaign.Capture(), strict: true);
        loaded.Campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(3, loaded.Campaign.Capture().Bases[0].Items["SUPPLY"]);
        Assert.Empty(loaded.Campaign.Capture().Bases[0].Transfers);
    }
}
