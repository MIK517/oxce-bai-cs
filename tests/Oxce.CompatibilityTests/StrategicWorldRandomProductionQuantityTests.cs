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

    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 0, 2)]
    [InlineData(true, 0, 2)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 1, 2)]
    public void DelayedFuelQuantitiesPreserveActualTransfersThroughArrival(bool cache, int minimum, int stock)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, FuelQuantityRules(minimum, 2));
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Restore(FuelQuantitySnapshot(content, stock) with { RandomState = seed }, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var owner = after.Bases[0];
            var history = Assert.Single(owner.Productions).RandomProductionInfo["SUPPLY"];
            var choice = history == minimum ? 1 : 2;
            choices.Add(choice);
            var row = TestFixtures.Rows(oracle.RootElement, "productionDelayedFuelQuantityRows").Single(row => row[0].GetInt32() == minimum && row[1].GetInt32() == stock && row[2].GetInt32() == choice);
            var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
            var craft = Assert.Single(owner.Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
            Assert.Equal(2, owner.Transfers.Count);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { minimum, stock, choice, progress.Produced, NativeProductionStop(progress.StopReason),
                owner.Items.GetValueOrDefault("SUPPLY"), history, craft.Status, craft.Fuel, after.World.Ufos[0].Destination!.Id,
                owner.Transfers.Count, owner.Transfers[0].Quantity, owner.Transfers[1].Quantity, owner.Transfers[0].Hours });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Take(14).ToArray())), CanonicalJson.Normalize(actual));
            var transferIds = owner.Transfers.Select(transfer => transfer.Id).ToArray();
            Assert.Equal(2, transferIds.Distinct().Count());
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "delayed-fuel-quantity.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            // Isolate the two hourly delivery boundaries, pausing further production.
            // The airborne craft remains in its takeoff countdown during both ticks.
            for (var hour = 3; hour <= 4; hour++)
            {
                var boundary = campaign.Capture();
                boundary = boundary with
                {
                    Time = new(1, 1, 1, 1999, hour - 1, 59, 55),
                    Bases = [boundary.Bases[0] with { Productions = [boundary.Bases[0].Productions[0] with { Assigned = 0 }] }],
                };
                campaign = CampaignState.Restore(boundary, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                after = campaign.Capture();
                owner = after.Bases[0];
                Assert.Equal(row[hour == 3 ? 14 : 16].GetInt32(), owner.Items.GetValueOrDefault("SUPPLY"));
                if (hour == 3)
                {
                    Assert.Equal(transferIds, owner.Transfers.Select(transfer => transfer.Id));
                    Assert.All(owner.Transfers, transfer => Assert.Equal(row[15].GetInt32(), transfer.Hours));
                }
                else Assert.Empty(owner.Transfers);
                Assert.Equal(history, Assert.Single(owner.Productions).RandomProductionInfo["SUPPLY"]);
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "delayed-fuel-arrival.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
            }
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedFuelQuantityRangesReserveActualTransferCountAndUpperCapacity(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(cache, FuelQuantityRules(0, 2));
        var initial = FuelQuantitySnapshot(content, 1);
        initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue - 2 } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.Bases[0].Transfers.Count);
        Assert.Equal(int.MaxValue, after.NextIds["oxcePortTransfer"]);
        Assert.Equivalent(after, Reload(content, campaign, 81, "delayed-fuel-ids.sav").Capture(), strict: true);
        initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue - 1 } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Production arrivals exhaust the transfer identity range.");
        initial = initial with
        {
            NextIds = new Dictionary<string, int> { ["oxcePortTransfer"] = 18 },
            Bases = [initial.Bases[0] with { Transfers = [new(17, 4, CampaignTransferKind.Item, "SUPPLY", int.MaxValue)] }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Production arrivals exceed the supported quantity range.");
    }

    [Fact]
    public void DelayedFuelQuantityProjectionLeavesCurrentStockAndUncertaintyUntouched()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(false, FuelQuantityRules(0, 2));
        var campaign = CampaignState.Restore(FuelQuantitySnapshot(content, 1), content, new SplitMix64RandomSource(81));
        var before = campaign.Capture();
        var crafts = campaign.BaseStates.ToDictionary(owner => owner.Id, owner => owner.Crafts.ToList());
        var stocks = campaign.BaseStates.ToDictionary(owner => owner.Id, owner => new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items));
        Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> uncertainty = [];
        long work = 0;
        Assert.Null(campaign.ProductionPreflight.ForecastStocks(crafts, stocks, uncertainty, null, false, null, CampaignTimeTrigger.OneHour, ref work));
        Assert.Equal(1, stocks[before.Bases[0].Id][content.RuntimeRules.Items.GetRequired("SUPPLY")]);
        Assert.Empty(uncertainty);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedFuelQuantityRangesPreserveSavedCraftOrder(bool cache, bool reverse)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, FuelQuantityRules(1));
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = SharedFuelQuantitySnapshot(content, 1, reverse) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var owner = after.Bases[0];
            var history = Assert.Single(owner.Productions).RandomProductionInfo["SUPPLY"];
            choices.Add(history);
            var row = TestFixtures.Rows(oracle.RootElement, "productionSharedFuelQuantityRows").Single(row => row[0].GetInt32() == (reverse ? 1 : 0) && row[1].GetInt32() == 1 && row[2].GetInt32() == history);
            var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
            var first = owner.Crafts[0];
            var second = owner.Crafts[1];
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { reverse ? 1 : 0, 1, history, progress.Produced, NativeProductionStop(progress.StopReason),
                owner.Items.GetValueOrDefault("SUPPLY"), history, first.Id, first.Logistics!.Status, first.Logistics.Fuel,
                second.Id, second.Logistics!.Status, second.Logistics.Fuel, after.World.Ufos[0].Destination!.Id });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "shared-fuel-quantity.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedFuelQuantityThresholdsStopBeforeSecondCraftChanges(bool cache, bool reverse)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionSharedFuelQuantityRows").Where(row => row[0].GetInt32() == (reverse ? 1 : 0) && row[1].GetInt32() == 0).ToArray();
        Assert.Equal(2, rows.Select(row => row[11].GetString()).Distinct().Count());
        var content = Load(cache, FuelQuantityRules(1));
        var campaign = Reload(content, CampaignState.Restore(SharedFuelQuantitySnapshot(content, 0, reverse), content, new SplitMix64RandomSource(81)), 81, "shared-fuel-guard.sav");
        AssertTimeBlocked(campaign, "UFO retargeting requires stochastic craft ammunition stock forecasts.");
    }

    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 1, 2)]
    [InlineData(false, 2, 2)]
    [InlineData(true, 2, 2)]
    public void SharedMaterialQuantityRangesPreserveReachedStartsAndFuel(bool cache, int required, int stock)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, SharedMaterialQuantityRules(required));
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Restore(SharedMaterialQuantitySnapshot(content, stock) with { RandomState = seed }, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var owner = after.Bases[0];
            var history = owner.Productions[0].RandomProductionInfo["SUPPLY"];
            choices.Add(history);
            var row = TestFixtures.Rows(oracle.RootElement, "productionSharedMaterialQuantityRows").Single(row => row[0].GetInt32() == required && row[1].GetInt32() == stock && row[2].GetInt32() == history);
            var progress = result.Events.OfType<CampaignProductionProgress>().ToArray();
            Assert.Equal(2, progress.Length);
            var craft = Assert.Single(owner.Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { required, stock, history, progress[0].Produced, NativeProductionStop(progress[0].StopReason),
                progress[1].Produced, NativeProductionStop(progress[1].StopReason), owner.Items.GetValueOrDefault("SUPPLY"), owner.Items.GetValueOrDefault("INERT_A"), history,
                craft.Status, craft.Fuel, after.World.Ufos[0].Destination!.Id });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            Assert.Equal(2, owner.Productions[1].Spent);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "shared-material-quantity.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(true, 1, 0)]
    [InlineData(false, 2, 0)]
    [InlineData(true, 2, 0)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 2, 1)]
    public void SharedMaterialQuantityThresholdsRemainAtomic(bool cache, int required, int stock)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionSharedMaterialQuantityRows").Where(row => row[0].GetInt32() == required && row[1].GetInt32() == stock).ToArray();
        Assert.Equal(2, rows.Select(row => row[10].GetString()).Distinct().Count());
        if (required == 2 && stock == 0) Assert.Equal(2, rows.Select(row => row[6].GetInt32()).Distinct().Count());
        var content = Load(cache, SharedMaterialQuantityRules(required));
        var campaign = Reload(content, CampaignState.Restore(SharedMaterialQuantitySnapshot(content, stock), content, new SplitMix64RandomSource(81)), 81, "shared-material-guard.sav");
        AssertTimeBlocked(campaign, required == 2 && stock == 0 ? "UFO retargeting requires stochastic production material forecasts."
            : "UFO retargeting requires stochastic craft ammunition stock forecasts.");
    }

    private static CampaignSnapshot SharedFuelQuantitySnapshot(RuntimeContent content, int stock, bool reverse)
    {
        var initial = FuelQuantitySnapshot(content, stock);
        var craft = Assert.Single(initial.Bases[0].Crafts, craft => craft.RuleId == "VARIANT_HUNT");
        return initial with { Bases = [initial.Bases[0] with { Crafts = [craft with { Id = reverse ? 2 : 1 }, craft with { Id = reverse ? 1 : 2 }] }] };
    }

    private static string SharedMaterialQuantityRules(int required) => FuelQuantityRules(1).Replace("manufacture: [",
        $"items: [{{type: INERT_A, battleType: 1, clipSize: 1}}]\nmanufacture: [{{name: MATERIAL_PRODUCT, time: 2, requiredItems: {{SUPPLY: {required}}}, producedItems: {{INERT_A: 1}}}}, ", StringComparison.Ordinal);

    private static CampaignSnapshot SharedMaterialQuantitySnapshot(RuntimeContent content, int stock)
    {
        var initial = FuelQuantitySnapshot(content, stock);
        return initial with
        {
            Bases = [initial.Bases[0] with
            {
                Productions = [initial.Bases[0].Productions[0], new("MATERIAL_PRODUCT", 1, 1, 3, true, false, false, new Dictionary<string, int>())],
            }],
        };
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
