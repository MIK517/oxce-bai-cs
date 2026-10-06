using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false, "fuel")]
    [InlineData(true, "fuel")]
    [InlineData(false, "material")]
    [InlineData(true, "material")]
    [InlineData(false, "reuse")]
    [InlineData(true, "reuse")]
    [InlineData(false, "captive")]
    [InlineData(true, "captive")]
    [InlineData(false, "overflow")]
    [InlineData(true, "overflow")]
    public void RelevantOrUnsafeRandomVariantsStopBeforeMutation(bool cache, string effect)
    {
        var overlay = VariantProductionRules(0, 0);
        if (effect == "fuel") overlay = overlay.Replace("refuelItem: SUPPLY", "refuelItem: INERT_A", StringComparison.Ordinal);
        if (effect == "reuse") overlay = overlay.Replace("type: INERT_A, battleType: 1", "type: INERT_A, battleType: 0", StringComparison.Ordinal);
        if (effect == "captive") overlay = overlay.Replace("type: INERT_A, battleType: 1", "type: INERT_A, liveAlien: true, battleType: 1", StringComparison.Ordinal);
        if (effect == "material") overlay = overlay.Replace("requiredItems: {SUPPLY: 2}", "requiredItems: {INERT_A: 1}", StringComparison.Ordinal);
        var content = Load(cache, overlay);
        var initial = VariantProductionSnapshot(content, false);
        if (effect == "overflow") initial = initial with
        {
            Bases = [initial.Bases[0] with { Items = new Dictionary<string, int> { ["SUPPLY"] = 1, ["INERT_A"] = int.MaxValue } }],
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "variant-guard.sav");
        AssertTimeBlocked(campaign, effect == "overflow" ? "Production outputs exceed the supported stock range."
            : "UFO retargeting requires production fuel-stock forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InertRandomOutputsPreserveNativeFuelAndRealHistory(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionVariantRows").Where(row => row[1].GetInt32() == 0).ToArray();
        foreach (var variant in rows.GroupBy(row => row[2].GetInt32()))
        {
            var content = Load(cache, VariantProductionRules(variant.Key, 0));
            foreach (var sell in new[] { false, true })
            {
                HashSet<int> selected = [];
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    var initial = VariantProductionSnapshot(content, sell) with { RandomState = seed };
                    var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "variant-before.sav");
                    var result = campaign.Execute(new AdvanceCampaignTime(1));
                    Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                    var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                    var after = campaign.Capture();
                    var history = Assert.Single(after.Bases[0].Productions).RandomProductionInfo;
                    var choice = variant.Key == 0 ? history.GetValueOrDefault("INERT_B") == 2 ? 2 : 1
                        : history.GetValueOrDefault("INERT_A") == 1 ? 1 : 2;
                    selected.Add(choice);
                    var row = Assert.Single(variant, row => row[0].GetBoolean() == sell && row[3].GetInt32() == choice);
                    var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
                    var stop = progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0;
                    var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                    {
                        progress.Produced, stop, after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                        after.Bases[0].Items.GetValueOrDefault("INERT_A"), after.Bases[0].Items.GetValueOrDefault("INERT_B"),
                        after.Bases[0].Transfers.Count, history.GetValueOrDefault("SUPPLY"), history.GetValueOrDefault("INERT_A"),
                        history.GetValueOrDefault("INERT_B"), craft.Status, craft.Fuel,
                        after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
                    });
                    var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
                    Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                    Reload(content, campaign, seed, "variant-after.sav");
                }
                Assert.Equal(2, selected.Count);
            }
        }
    }

    private static string VariantProductionRules(int variant, int delay) =>
        $"items: [{{type: INERT_A, battleType: 1, clipSize: 1}}, {{type: INERT_B, battleType: 1, clipSize: 1}}]\n" +
        "crafts: [{type: VARIANT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}]\n" +
        $"manufacture: [{{name: VARIANT_PRODUCT, time: 2, requiredItems: {{SUPPLY: 2}}, producedItems: {{SUPPLY: 1}}, randomProducedItems: [[1, {{SUPPLY: 2, INERT_A: 1}}], [1, {{SUPPLY: 2, {(variant == 0 ? "INERT_B" : "INERT_A")}: {(variant == 2 ? 0 : 2)}}}]], transferTimes: [{delay}]}}]\n" +
        "facilities: [{type: VARIANT_WORKSHOP, mapName: LOGISTICS, workshops: 6}]\n";

    private static CampaignSnapshot VariantProductionSnapshot(Oxce.Mods.Rulesets.Content.RuntimeContent content, bool sell)
    {
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "VARIANT_HUNT",
            Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        return initial with
        {
            Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Soldiers = [], Items = new Dictionary<string, int> { ["SUPPLY"] = 1 },
                Facilities = [.. initial.Bases[0].Facilities, new("VARIANT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("VARIANT_PRODUCT", 1, 1, 2, true, sell, false, new Dictionary<string, int>())],
            }],
        };
    }
}
