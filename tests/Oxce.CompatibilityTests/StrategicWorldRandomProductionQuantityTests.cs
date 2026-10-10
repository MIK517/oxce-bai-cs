using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    public void RandomFuelQuantitiesPermitCommonLaunchAndPreserveActualHistory(bool cache, int minimum, int stock)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, FuelQuantityRules(minimum));
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = FuelQuantitySnapshot(content, stock) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var history = Assert.Single(after.Bases[0].Productions).RandomProductionInfo["SUPPLY"];
            var choice = history == minimum ? 1 : 2;
            choices.Add(choice);
            var row = TestFixtures.Rows(oracle.RootElement, "productionFuelQuantityRows").Single(row => row[0].GetInt32() == minimum && row[1].GetInt32() == stock && row[2].GetInt32() == choice);
            var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
            var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { minimum, stock, choice, progress.Produced, NativeProductionStop(progress.StopReason),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), history, craft.Status, craft.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "random-fuel-quantity.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomFuelQuantitiesCrossingLaunchEligibilityRemainAtomic(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionFuelQuantityRows").Where(row => row[0].GetInt32() == 0 && row[1].GetInt32() == 0).ToArray();
        Assert.Equal(2, rows.Select(row => row[7].GetString()).Distinct().Count());
        var content = Load(cache, FuelQuantityRules(0));
        var campaign = Reload(content, CampaignState.Restore(FuelQuantitySnapshot(content, 0), content, new SplitMix64RandomSource(81)), 81, "random-fuel-quantity-guard.sav");
        AssertTimeBlocked(campaign, "UFO retargeting requires stochastic craft ammunition stock forecasts.");
    }

    [Fact]
    public void RandomFuelQuantityProjectionAddsSparseBoundsWithoutCampaignMutation()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(false, FuelQuantityRules(1));
        var campaign = CampaignState.Restore(FuelQuantitySnapshot(content, 0), content, new SplitMix64RandomSource(81));
        var before = campaign.Capture();
        var crafts = campaign.BaseStates.ToDictionary(owner => owner.Id, owner => owner.Crafts.ToList());
        var stocks = campaign.BaseStates.ToDictionary(owner => owner.Id, owner => new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items));
        Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> uncertainty = [];
        long work = 0;
        Assert.Null(campaign.ProductionPreflight.ForecastStocks(crafts, stocks, uncertainty, null, false, null, CampaignTimeTrigger.OneHour, ref work));
        var supply = content.RuntimeRules.Items.GetRequired("SUPPLY");
        Assert.Equal(2, stocks[before.Bases[0].Id][supply]);
        Assert.Equal(1, uncertainty[before.Bases[0].Id][supply]);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        work = 999_999;
        Assert.Contains("work limit", campaign.ProductionPreflight.ForecastStocks(crafts, stocks, uncertainty, null, false, null, CampaignTimeTrigger.OneHour, ref work)!);
        Assert.True(work > 1_000_000);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static string FuelQuantityRules(int minimum, int delay = 0) => $$"""
        crafts: [{type: VARIANT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}]
        manufacture: [{name: VARIANT_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, randomProducedItems: [[1, {SUPPLY: {{minimum}}}], [1, {SUPPLY: 2}]], transferTimes: [{{delay}}]}]
        facilities: [{type: VARIANT_WORKSHOP, mapName: LOGISTICS, workshops: 6}]
        """;

    private static CampaignSnapshot FuelQuantitySnapshot(RuntimeContent content, int stock)
    {
        var initial = VariantProductionSnapshot(content, false);
        return initial with
        {
            Bases = [initial.Bases[0] with
            {
                Items = stock == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = stock },
                Productions = [initial.Bases[0].Productions[0] with { Spent = 0, Amount = 3 }],
            }],
        };
    }
}
