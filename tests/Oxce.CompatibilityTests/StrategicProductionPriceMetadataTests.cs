using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ItemReadersMatchNativeDefaultsSignedValuesAndNulls(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "itemReaders");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var code = "var ptr RuleItem absent null; var int value; ";
            foreach (var property in rows[stage].EnumerateObject())
                code += $"item_rule.{property.Name} value; if neq value {property.Value.GetInt32()}; div cost_current 0; end; " +
                    $"absent.{property.Name} value; if neq value 0; div cost_current 0; end; ";
            AssertPrice(Load(global, code + "return 7;", stage == 0 ? "" : """
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
    [InlineData(false)]
    [InlineData(true)]
    public void ItemCategoriesUseNativeOrderedReplacement(bool global)
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
            AssertPrice(Load(global, code + "return 7;", $"categories: {categories}", definitions));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedRuleGlobalAndSavedCampaignTagsRetainNativeValues(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        using var copyOracle = TestFixtures.ReadVerifiedExpected("strategic-detection-value-copy");
        var value = TestFixtures.Rows(oracle.RootElement, "tagValues")[2][1].GetInt32();
        Assert.Equal(value, copyOracle.RootElement.GetProperty("tagCopy").GetInt32());
        var code = "var ptr RuleItem absent null; var ptr RuleResearch research; var ptr RuleResearch absentResearch null; " +
            "var ptr RuleMod absentMod null; var ptr GeoscapeGame absentGame null; var RuleItem.Tag selected; var RuleItem.Tag alias; var int value; " +
            "set selected Tag.PRICE_ITEM; set alias selected; item_rule.getTag value alias; " +
            $"if neq value {value}; div cost_current 0; end; absent.getTag value alias; if neq value 0; div cost_current 0; end; " +
            "item_rule.getTag value Tag.PRICE_MISSING; if neq value 0; div cost_current 0; end; " +
            "rules.getRuleResearch research \"PRICE_TAG_RESEARCH\"; research.getTag value Tag.PRICE_RESEARCH; " +
            $"if neq value {value}; div cost_current 0; end; absentResearch.getTag value Tag.PRICE_RESEARCH; if neq value 0; div cost_current 0; end; " +
            $"rules.getTag value Tag.PRICE_MOD; if neq value {value}; div cost_current 0; end; " +
            "absentMod.getTag value Tag.PRICE_MOD; if neq value 0; div cost_current 0; end; " +
            $"geoscape_game.getTag value Tag.PRICE_GAME; if neq value {value}; div cost_current 0; end; " +
            "absentGame.getTag value Tag.PRICE_GAME; if neq value 0; div cost_current 0; end; return 7;";
        var content = Load(global, code, $"tags: {{PRICE_ITEM: {value}}}",
            $"research: [{{name: PRICE_TAG_RESEARCH, cost: 1, tags: {{PRICE_RESEARCH: {value}}}}}]",
            $"tags:\n  RuleItem: {{PRICE_ITEM: int, PRICE_MISSING: int}}\n  RuleResearch: {{PRICE_RESEARCH: int}}\n  RuleMod: {{PRICE_MOD: int}}\n  GeoscapeGame: {{PRICE_GAME: int}}\nglobals: {{PRICE_MOD: {value}}}");
        AssertPrice(content, snapshot: initial => initial with { ScriptValues = [new("PRICE_GAME", "int", value)] });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModSettingsAndSmokePreserveNativeSignedArithmetic(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "modSettings"))
        {
            var code = "var ptr RuleMod absent null; var int value; rules.getMaxViewDistance value; " +
                $"if neq value {row[0].GetInt32()}; div cost_current 0; end; rules.getMaxDarknessToSeeUnits value; " +
                $"if neq value {row[1].GetInt32()}; div cost_current 0; end; " +
                "absent.getMaxViewDistance value; if neq value 0; div cost_current 0; end; " +
                "absent.getMaxDarknessToSeeUnits value; if neq value 0; div cost_current 0; end; return 7;";
            AssertPrice(Load(global, code, extraRules: $"maxViewDistance: {row[0].GetInt32()}\nmaxDarknessToSeeUnits: {row[1].GetInt32()}"));
        }
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "smokeReduction"))
            AssertPrice(Load(global, $"var ptr RuleMod alias rules; var int value {row[1].GetInt32()}; " +
                "alias.getSmokeReduction value; return value;", extraRules: $"maxViewDistance: {row[0].GetInt32()}"), row[2].GetInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OnlyConsumedNullModSmokeCallsStopBeforeMutation(bool global, bool consumed)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var code = "var ptr RuleMod absent null; var int value 15; " +
            $"if eq 1 {(consumed ? 1 : 0)}; absent.getSmokeReduction value; end; return 7;";
        var content = Load(global, code);
        if (!consumed) AssertPrice(content);
        else
        {
            var campaign = Reload(content, Create(content), 81, "null-mod-price.sav");
            AssertTimeBlocked(campaign, $"Price {(global ? "event" : "script")} failed: Script binding 'RuleMod.getSmokeReduction' failed: Price smoke reduction requires a nonnull Mod receiver..");
        }
    }

    private static RuntimeContent Load(bool global, string code, string itemFields = "",
        string extraRules = "", string extraExtended = "", string extraItems = "", bool buying = false,
        string extraFacilities = "")
    {
        var parser = buying ? "buyCostItem" : "sellCostItem";
        var item = "items:\n  - type: PRICE_META_ITEM\n    costSell: 7\n" + Indent(itemFields, 4) +
            (global ? "" : $"    scripts:\n      {parser}: |\n" + Indent(code, 8)) + Indent(extraItems, 2);
        var extended = extraExtended.Length == 0 && !global ? "" : "extended:\n" + Indent(extraExtended, 2) +
            (global ? $"  scripts:\n    {parser}:\n      - new: price-metadata\n        offset: 1\n        code: |\n" + Indent(code, 10) : "");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], item + extended + "manufacture: [{name: PRICE_META_PRODUCT, time: 1, producedItems: {PRICE_META_ITEM: 1}}]\n" +
            "facilities:\n  - {type: PRICE_META_WORKSHOP, mapName: LOGISTICS, workshops: 1}\n" +
            Indent(extraFacilities, 2) + extraRules + "\n");
    }

    private static string Indent(string text, int count) => text.Length == 0 ? "" :
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => new string(' ', count) + line)) + "\n";

    private static CampaignState Create(RuntimeContent content, Func<CampaignSnapshot, CampaignSnapshot>? snapshot = null)
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
        return CampaignState.Restore(snapshot?.Invoke(initial) ?? initial, content, new SplitMix64RandomSource(81));
    }

    private static void AssertPrice(RuntimeContent content, int expected = 7, Func<CampaignSnapshot, CampaignSnapshot>? snapshot = null)
    {
        var campaign = Reload(content, Create(content, snapshot), 81, "price-metadata-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(expected, campaign.Capture().Funds[^1]);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "price-metadata-after.sav");
    }
}
