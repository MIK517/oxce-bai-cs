using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
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
                AdvanceUnblocked(campaign, 1);
                AssertSavingMaterialResult(campaign.Capture(), group);
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
                AdvanceUnblocked(campaign, 1);
                AssertSavingMaterialResult(campaign.Capture(), group);
                Reload(content, campaign, 81, "saving-material-bounds-after.sav");
            }
        }
    }

    private static RuntimeContent LoadSavingMaterialContent(bool cache, int input) => Load(cache,
        "items: [{type: SUPPLY, clipSize: 4}]\n" +
        "craftWeapons: [{type: SAVING_MATERIAL_GUN, ammoMax: 10, rearmRate: 4, bulletSaving: true, launcher: SUPPLY, clip: SUPPLY}]\n" +
        "crafts: [{type: SAVING_MATERIAL_CRAFT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, weapons: 1}]\n" +
        $"manufacture: [{{name: SAVING_MATERIAL_PRODUCT, time: 1, requiredItems: {{SUPPLY: {input}}}, producedItems: {{SUPPLY: 1}}}}]\n" +
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

    private static void AssertSavingMaterialResult(CampaignSnapshot after, IEnumerable<JsonElement> rows)
    {
        var gun = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SAVING_MATERIAL_CRAFT").Logistics!.Weapons[0]!;
        var stock = after.Bases[0].Items.GetValueOrDefault("SUPPLY");
        var native = rows.First(row => row[10].GetInt32() == stock);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { gun.Ammo, gun.Rearming, stock });
        var expected = JsonSerializer.SerializeToUtf8Bytes(new[] { native[5], native[6], native[10] });
        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
        Assert.True(Assert.Single(after.World.Ufos).Hunting);
        Assert.Equal(1, after.World.Ufos[0].Destination!.Id);
    }
}
