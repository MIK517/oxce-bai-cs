using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ItemReadersMatchNativeDefaultsSignedValuesAndNulls(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "itemReaders");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var code = "var ptr RuleItem absent null; var int value; ";
            foreach (var property in rows[stage].EnumerateObject())
                code += $"item_rule.{property.Name} value; if neq value {property.Value.GetInt32()}; div cost_current 0; end; " +
                    $"absent.{property.Name} value; if neq value 0; div cost_current 0; end; ";
            AssertPrice(Load(cache, global, code + "return 7;", stage == 0 ? "" : """
                accuracyAimed: -7
                accuracyAuto: 2147483647
                accuracyMelee: 41
                accuracyMindControl: -11
                accuracyPanic: -2147483648
                accuracySnap: 83
                accuracyThrow: 120
                accuracyUse: 27
                armor: 0
                meleePower: 2147483647
                power: -9
                waypoints: -1
                twoHanded: true
                blockBothHands: true
                underwaterOnly: true
                """));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ItemCategoriesUseNativeOrderedReplacement(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "itemCategoryReplacement");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var code = "var ptr RuleItem absent null; var int value; ";
            foreach (var row in rows[stage].EnumerateArray())
                code += $"{(row[0].GetString() == "NULL" ? "absent" : "item_rule")}.hasCategory value \"{row[0].GetString()}\"; " +
                    $"if neq value {row[1].GetInt32()}; div cost_current 0; end; ";
            var categories = stage == 2 ? "[𐀀]" : "[A, B, C, FREE, A]";
            var definitions = stage switch
            {
                0 => "itemCategories: [{type: C, replaceBy: A}, {type: A, replaceBy: B}, {type: B, replaceBy: C}, {type: FREE}]",
                1 => "itemCategories: [{type: C, replaceBy: B}, {type: B, replaceBy: A}, {type: A}, {type: FREE}]",
                _ => "itemCategories: [{type: 𐀀, replaceBy: }, {type: , replaceBy: A}, {type: A}]",
            };
            AssertPrice(Load(cache, global, code + "return 7;", $"categories: {categories}", definitions));
        }
    }

    private static RuntimeContent Load(bool cache, bool global, string code, string itemFields = "",
        string extraRules = "", string extraExtended = "", string extraItems = "")
    {
        var item = "items:\n  - type: PRICE_META_ITEM\n    costSell: 7\n" + Indent(itemFields, 4) +
            (global ? "" : "    scripts:\n      sellCostItem: |\n" + Indent(code, 8)) + Indent(extraItems, 2);
        var extended = extraExtended.Length == 0 && !global ? "" : "extended:\n" + Indent(extraExtended, 2) +
            (global ? "  scripts:\n    sellCostItem:\n      - new: price-metadata\n        offset: 1\n        code: |\n" + Indent(code, 10) : "");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache,
            item + extended + "manufacture: [{name: PRICE_META_PRODUCT, time: 1, producedItems: {PRICE_META_ITEM: 1}}]\n" +
            "facilities: [{type: PRICE_META_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n" + extraRules + "\n");
    }

    private static string Indent(string text, int count) => text.Length == 0 ? "" :
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => new string(' ', count) + line)) + "\n";

    private static CampaignState Create(RuntimeContent content)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 59, 55), "HUNTER", 2200).Capture();
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = 0.45,
                Latitude = 0.21,
                Destination = null
            },
        }) with
        { Funds = [0] };
        initial = initial with
        {
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = true }] },
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("PRICE_META_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("PRICE_META_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
            }],
        };
        return CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
    }

    private static void AssertPrice(RuntimeContent content, int expected = 7)
    {
        var campaign = Reload(content, Create(content), 81, "price-metadata-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(expected, campaign.Capture().Funds[^1]);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "price-metadata-after.sav");
    }
}
