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
    [InlineData(false)]
    [InlineData(true)]
    public void AmmunitionUncertaintyIgnoresUnconsumedProductionMaterials(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionAmmunitionMaterialRows");
        foreach (var input in new[] { 1, 2 })
        {
            var content = LoadSavingMaterialContent(cache, input);
            foreach (var group in rows.Where(row => row[1].GetInt32() == input &&
                (row[2].GetInt32() == 1 || row[3].GetInt32() == 0)).GroupBy(row =>
                (Stock: row[0].GetInt32(), Amount: row[2].GetInt32(), Assigned: row[3].GetInt32())))
            {
                var initial = SavingMaterialSnapshot(content, group.Key.Stock, group.Key.Amount, group.Key.Assigned);
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                    81, "saving-material-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                AssertSavingMaterialResult(campaign.Capture(), group, result.Events);
                Reload(content, campaign, 81, "saving-material-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmmunitionUncertaintyRetainsConsumedProductionGuard(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = LoadSavingMaterialContent(cache, 2);
        var initial = SavingMaterialSnapshot(content, stock: 1, amount: 3, assigned: 1);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            81, "saving-material-uncertain.sav");
        AssertTimeBlocked(campaign, "UFO retargeting requires stochastic production material forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmmunitionMaterialBoundsAdmitEqualEligibilityOutcomes(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionAmmunitionMaterialRows");
        foreach (var input in new[] { 1, 2 })
        {
            var content = LoadSavingMaterialContent(cache, input);
            foreach (var group in rows.Where(row => row[1].GetInt32() == input && row[2].GetInt32() == 3 && row[3].GetInt32() != 0)
                .GroupBy(row => (Stock: row[0].GetInt32(), Amount: row[2].GetInt32(), Assigned: row[3].GetInt32())))
            {
                var initial = SavingMaterialSnapshot(content, group.Key.Stock, group.Key.Amount, group.Key.Assigned);
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                    81, "saving-material-bounds-before.sav");
                // Native progress/result must agree across both controlled saving
                // outcomes. Stock quantities themselves are allowed to differ.
                if (group.Select(row => (row[7].GetInt32(), row[8].GetInt32(), row[9].GetInt32())).Distinct().Count() != 1)
                {
                    AssertTimeBlocked(campaign, "UFO retargeting requires stochastic production material forecasts.");
                    continue;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                AssertSavingMaterialResult(campaign.Capture(), group, result.Events);
                Reload(content, campaign, 81, "saving-material-bounds-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmmunitionMaterialBoundsFollowSavedProductionOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionAmmunitionSequenceRows");
        foreach (var input in new[] { 1, 2 })
            foreach (var nextInput in new[] { 1, 2 })
            {
                var content = LoadSavingMaterialContent(cache, input, nextInput);
                foreach (var group in rows.Where(row => row[1].GetInt32() == input && row[2].GetInt32() == nextInput)
                    .GroupBy(row => row[0].GetInt32()))
                {
                    var initial = SavingMaterialSnapshot(content, group.Key, amount: 3, assigned: 2);
                    initial = initial with
                    {
                        Bases = [initial.Bases[0] with
                        {
                            Productions = [.. initial.Bases[0].Productions,
                                new("SAVING_NEXT_PRODUCT", 2, 0, 3, false, false, false, new Dictionary<string, int>())],
                        }],
                    };
                    var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                        81, "saving-sequence-before.sav");
                    if (group.Select(row => (row[4].GetInt32(), row[5].GetInt32(), row[7].GetInt32(), row[8].GetInt32())).Distinct().Count() != 1)
                    {
                        AssertTimeBlocked(campaign, "UFO retargeting requires stochastic production material forecasts.");
                        continue;
                    }
                    var result = campaign.Execute(new AdvanceCampaignTime(1));
                    Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                    var after = campaign.Capture();
                    var native = group.First(row => row[10].GetInt32() == after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                    AssertMaterialProgress(after, result.Events, "SAVING_MATERIAL_PRODUCT", native[4].GetInt32(), native[5].GetInt32(), native[6].GetInt32());
                    AssertMaterialProgress(after, result.Events, "SAVING_NEXT_PRODUCT", native[7].GetInt32(), native[8].GetInt32(), native[9].GetInt32());
                    var gun = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SAVING_MATERIAL_CRAFT").Logistics!.Weapons[0]!;
                    Assert.Equal(native[11].GetInt32(), gun.Ammo);
                    Assert.Equal(native[12].GetBoolean(), gun.Rearming);
                    Assert.True(Assert.Single(after.World.Ufos).Hunting);
                    Reload(content, campaign, 81, "saving-sequence-after.sav");
                }
            }
    }

    [Fact]
    public void ProductionMaterialProjectionRejectsExhaustedWorkWithoutCampaignMutation()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = LoadSavingMaterialContent(false, 1);
        var campaign = CampaignState.Restore(SavingMaterialSnapshot(content, 4, 3, 2), content, new SplitMix64RandomSource(81));
        var before = campaign.Capture();
        var crafts = campaign.BaseStates.ToDictionary(owner => owner.Id, owner => owner.Crafts.ToList());
        var stocks = campaign.BaseStates.ToDictionary(owner => owner.Id, owner => new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items));
        var uncertainty = campaign.BaseStates.ToDictionary(owner => owner.Id, _ =>
            new Dictionary<RuleHandle<ItemRuleFamily>, int> { [content.RuntimeRules.Items.GetRequired("SUPPLY")] = 1 });
        long work = 999_999;
        var reason = campaign.ProductionPreflight.ForecastStocks(crafts, stocks, uncertainty,
            null, false, null, CampaignTimeTrigger.OneHour, ref work);
        Assert.Contains("work limit", reason!);
        Assert.True(work > 1_000_000);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static RuntimeContent LoadSavingMaterialContent(bool cache, int input, int? nextInput = null) => Load(cache,
        "items: [{type: SUPPLY, clipSize: 4}]\n" +
        "craftWeapons: [{type: SAVING_MATERIAL_GUN, ammoMax: 10, rearmRate: 4, bulletSaving: true, launcher: SUPPLY, clip: SUPPLY}]\n" +
        "crafts: [{type: SAVING_MATERIAL_CRAFT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, weapons: 1}]\n" +
        $"manufacture:\n  - {{name: SAVING_MATERIAL_PRODUCT, time: 1, requiredItems: {{SUPPLY: {input}}}, producedItems: {{SUPPLY: 1}}}}\n" +
        (nextInput is null ? "" : $"  - {{name: SAVING_NEXT_PRODUCT, time: 1, requiredItems: {{SUPPLY: {nextInput}}}, producedItems: {{SUPPLY: 1}}}}\n") +
        "facilities: [{type: SAVING_MATERIAL_WORKSHOP, mapName: LOGISTICS, workshops: 10}]\n");

    private static CampaignSnapshot SavingMaterialSnapshot(RuntimeContent content, int stock, int amount, int assigned)
    {
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var owner = initial.Bases[0];
        var craft = owner.Crafts.Single(value => value.RuleId == "SHIP");
        return initial with
        {
            Bases = [owner with
            {
                Soldiers = [],
                Items = stock == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = stock },
                Facilities = [.. owner.Facilities, new("SAVING_MATERIAL_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Crafts = [craft, craft with
                {
                    RuleId = "SAVING_MATERIAL_CRAFT", Id = 2,
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REARMING", Fuel = 100, Speed = 0, Destination = null,
                        Weapons = [new("SAVING_MATERIAL_GUN", 9, Rearming: true)],
                    },
                }],
                Productions = [new("SAVING_MATERIAL_PRODUCT", assigned, 0, amount, false, false, false, new Dictionary<string, int>())],
            }],
        };
    }

    private static void AssertSavingMaterialResult(CampaignSnapshot after, IEnumerable<JsonElement> rows, IReadOnlyList<ICampaignEvent> events)
    {
        var gun = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SAVING_MATERIAL_CRAFT").Logistics!.Weapons[0]!;
        var stock = after.Bases[0].Items.GetValueOrDefault("SUPPLY");
        var native = rows.First(row => row[10].GetInt32() == stock);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { gun.Ammo, gun.Rearming, stock });
        var expected = JsonSerializer.SerializeToUtf8Bytes(new[] { native[5], native[6], native[10] });
        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
        AssertMaterialProgress(after, events, "SAVING_MATERIAL_PRODUCT", native[7].GetInt32(), native[9].GetInt32(), native[8].GetInt32());
        Assert.True(Assert.Single(after.World.Ufos).Hunting);
        Assert.Equal(1, after.World.Ufos[0].Destination!.Id);
    }

    private static void AssertMaterialProgress(CampaignSnapshot after, IReadOnlyList<ICampaignEvent> events,
        string rule, int units, int result, int spent)
    {
        var progress = events.OfType<CampaignProductionProgress>().SingleOrDefault(value => value.RuleId == rule);
        if (units == 0) Assert.Null(progress);
        else
        {
            Assert.NotNull(progress);
            Assert.Equal(units, progress.Produced);
            Assert.Equal(result switch { 0 => null, 1 => "STR_PRODUCTION_COMPLETE", 3 => "STR_NOT_ENOUGH_MATERIALS", _ => throw new InvalidDataException("Unexpected native material result.") }, progress.StopReason);
        }
        var project = after.Bases[0].Productions.SingleOrDefault(value => value.RuleId == rule);
        if (result != 0) Assert.Null(project);
        else Assert.Equal(spent, Assert.IsType<ProductionSnapshot>(project).Spent);
    }
}
