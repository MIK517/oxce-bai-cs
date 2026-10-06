using Oxce.TestSupport;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CurrentBuyPricesComposeNativeMetadataAndEventOrderWithoutMutation(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var native = TestFixtures.Rows(oracle.RootElement, "conversionRows")
            .Single(row => row[0].GetInt32() == 20 && row[1].GetInt32() == 39)[2][3].GetInt32();
        var code = "var ptr RuleDamageType damage; var ptre RandomState random null; var ptr Time time; var int value; var int power; " +
            "geoscape_game.getTime time; time.getSecond value; if neq value 55; div cost_current 0; end; " +
            "item_rule.getTag value Tag.PRICE_BUY_TAG; if neq value -7; div cost_current 0; end; " +
            "item_rule.getPower power; item_rule.getDamageType damage; damage.getRandomDamage value power random; " +
            "if neq value 0; div cost_current 0; end; damage.getDamageToHealth value power; " +
            $"if neq value {native}; div cost_current 0; end; add cost_current value; return;";
        var events = global ? "" : "scripts:\n  buyCostItem:\n" +
            "    - {new: price-buy-before, offset: -1, code: 'add cost_current 1; return;'}\n" +
            "    - {new: price-buy-after, offset: 1, code: 'div cost_current 3; return;'}\n";
        var content = Load(cache, global, code, "costBuy: 7\npower: 39\ndamageAlter: {ToHealth: 0.5}\ntags: {PRICE_BUY_TAG: -7}",
            extraExtended: "tags:\n  RuleItem: {PRICE_BUY_TAG: int}\n" + events, buying: true);
        var campaign = Reload(content, Create(content, snapshot => PriceStock(snapshot) with { Funds = [10_000] }), 81, "price-buy-before.sav");
        var before = campaign.Capture();
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(before.Bases[0].Id, LogisticsOperation.Purchase)).Events)).Quote;
        var row = Assert.Single(quote.Rows, row => row.RuleId == "PRICE_META_ITEM");
        Assert.Equal(global ? 7 + native : (8 + native) / 3, row.UnitCost);
        Assert.Null(row.UnavailableReason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Reload(content, campaign, 81, "price-buy-after.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullRandomDamagePreservesNativeShortCircuitAndCurrentPricePurity(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var rows = TestFixtures.Rows(oracle.RootElement, "nullRandomDamage");
        Assert.Equal(0, oracle.RootElement.GetProperty("unusedRandomCalls").GetInt32());
        var code = "var ptr RuleDamageType damage; var ptr RuleDamageType absent null; var ptre RandomState random null; " +
            "var ptre RandomState copy; var int value; set copy random; item_rule.getDamageType damage; " +
            $"damage.getRandomDamage value 2147483647 copy; if neq value {rows[0].GetInt32()}; div cost_current 0; end; " +
            $"absent.getRandomDamage value -2147483648 random; if neq value {rows[1].GetInt32()}; div cost_current 0; end; " +
            $"absent.getRandomDamage value cost_current copy; if neq value {rows[2].GetInt32()}; div cost_current 0; end; return 7;";
        var content = Load(cache, global, code, "damageAlter: {ToHealth: 1e100}");
        var campaign = Reload(content, Create(content, PriceStock), 81, "price-null-random-before.sav");
        var before = campaign.Capture();
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(before.Bases[0].Id, LogisticsOperation.Sell)).Events)).Quote;
        var row = Assert.Single(quote.Rows, row => row.RuleId == "PRICE_META_ITEM");
        Assert.Equal(7, row.UnitCost);
        Assert.Null(row.UnavailableReason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 1);
        var constant = Create(Load(cache, global, "return 7;", "damageAlter: {ToHealth: 1e100}"), PriceStock);
        AdvanceUnblocked(constant, 1);
        Assert.Equal(constant.Capture().RandomState, campaign.Capture().RandomState);
        Assert.Equal(7, campaign.Capture().Funds[^1]);
        Reload(content, campaign, 81, "price-null-random-after.sav");
    }

    private static CampaignSnapshot PriceStock(CampaignSnapshot snapshot) => snapshot with
    {
        Bases = [snapshot.Bases[0] with
        {
            Items = new Dictionary<string, int>(snapshot.Bases[0].Items, StringComparer.Ordinal) { ["PRICE_META_ITEM"] = 1 },
        }],
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CurrentAndForecastPricesShareOneBoundedEventExecutionBudget(bool cache, bool aggregate)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-damage");
        var code = aggregate ? "loop var i 150000; add cost_current 0; end; return;" :
            "var ptr RuleDamageType absent null; var ptre RandomState random null; var int value; " +
            "loop var i 2147483647; absent.getRandomDamage value cost_current random; end; return;";
        var events = !aggregate ? "" : "scripts:\n  sellCostItem:\n" +
            "    - new: price-budget-before\n      offset: -1\n      code: '" + code + "'\n" +
            "    - new: price-budget-after\n      offset: 1\n      code: '" + code + "'";
        if (aggregate) AssertPrice(Load(cache, false, code)); // Each program fits; their combined execution does not.
        var content = Load(cache, false, code, extraExtended: events);
        var campaign = Reload(content, Create(content, PriceStock), 81, "price-budget-before.sav");
        var before = campaign.Capture();
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(before.Bases[0].Id, LogisticsOperation.Sell)).Events)).Quote;
        var row = Assert.Single(quote.Rows, row => row.RuleId == "PRICE_META_ITEM");
        Assert.Contains("limit", row.UnavailableReason!);
        Assert.Equal(0, row.MaximumQuantity);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains("limit", Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static readonly string[] PriceDamageReaders =
        ["getDamageToArmorPre", "getDamageToArmor", "getDamageToEnergy", "getDamageToHealth",
         "getDamageToItem", "getDamageToMana", "getDamageToMorale", "getDamageToStun",
         "getDamageToTile", "getDamageToTime", "getDamageToWound"];

    private const string PriceCustomDamage = """
        damageAlter:
          ToArmorPre: -0.125
          ToArmor: 0.375
          ToEnergy: -1.125
          ToHealth: 0.5
          ToItem: 1.25
          ToMana: -0.75
          ToMorale: 2.125
          ToStun: 0.025
          ToTile: -0.01
          ToTime: 0.1
          ToWound: 0.33333333
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PriceDamageConversionsPreserveNativeFloatTruncationAndNulls(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var nulls = TestFixtures.Rows(oracle.RootElement, "nullConversions");
        Assert.Equal(PriceDamageReaders.Length, nulls.Length);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "conversionRows"))
        {
            var type = row[0].GetInt32();
            var fields = type == 20 ? PriceCustomDamage : type == -1 ? "" : $"damageType: {type}";
            var code = "var ptr RuleDamageType damage; var ptr RuleDamageType absent null; var ptr RuleDamageType copy; " +
                $"var int power {row[1].GetInt32()}; var int value; item_rule.getDamageType damage; set copy damage; ";
            Assert.Equal(PriceDamageReaders.Length, row[2].GetArrayLength());
            for (var index = 0; index < PriceDamageReaders.Length; index++)
                code += $"copy.{PriceDamageReaders[index]} value power; if neq value {row[2][index].GetInt32()}; div cost_current 0; end; " +
                    $"absent.{PriceDamageReaders[index]} value 2147483647; if neq value {nulls[index].GetInt32()}; div cost_current 0; end; ";
            AssertPrice(Load(cache, global, code + "return 7;", fields));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PriceArmorPercentAndFloatBoundaryOutputsMatchNative(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "armorPercent"))
            AssertPrice(Load(cache, global, "var ptr RuleDamageType damage; var int value; item_rule.getDamageType damage; " +
                "damage.getArmorEffectiveness value; return value;", $"damageAlter: {{ArmorEffectiveness: {row[0].GetRawText()}}}"), row[1].GetInt32());
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "floatBoundaries"))
            AssertPrice(Load(cache, global, "var ptr RuleDamageType damage; var int value; item_rule.getDamageType damage; " +
                $"damage.getDamageToHealth value {row[0].GetInt32()}; return value;", $"damageAlter: {{ToHealth: {row[1].GetRawText()}}}"), row[2].GetInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OnlyConsumedUnsafePriceDamageConversionsStopBeforeMutation(bool cache, bool global)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-damage");
        foreach (var (method, fields, power) in new[]
        {
            ("getDamageToHealth", "ToHealth: 1", "2147483647"),
            ("getDamageToHealth", "ToHealth: 1e100", "1"),
            ("getArmorEffectiveness", "ArmorEffectiveness: 1e100", ""),
            ("getDamageToHealth", "ToHealth: -2", "2147483647"),
        })
            foreach (var consumed in new[] { false, true })
            {
                var code = "var ptr RuleDamageType damage; var ptr RuleDamageType absent null; var int value; item_rule.getDamageType damage; " +
                    $"if eq 1 {(consumed ? 1 : 0)}; damage.{method} value {power}; end; " +
                    $"absent.{method} value {power}; if neq value 0; div cost_current 0; end; return 7;";
                var content = Load(cache, global, code, $"damageAlter: {{{fields}}}");
                if (!consumed) AssertPrice(content);
                else
                {
                    var campaign = Reload(content, Create(content), 81, "price-damage-guard.sav");
                    AssertTimeBlocked(campaign, $"Price {(global ? "event" : "script")} failed: Script binding 'RuleDamageType.{method}' failed: Price damage query exceeds the supported integer range..");
                }
            }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageProfileIdentityAndClassificationMatchNativePriceQueries(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var type = row[0].GetInt32();
            var code = "var ptr RuleItem other; var ptr RuleItem absent null; var ptr RuleDamageType damage; " +
                "var ptr RuleDamageType same; var ptr RuleDamageType melee; var ptr RuleDamageType different; var ptr RuleDamageType missing; " +
                "var int value; rules.getRuleItem other \"PRICE_OTHER_DAMAGE\"; " +
                "item_rule.getDamageType damage; item_rule.getDamageType same; item_rule.getMeleeDamageType melee; " +
                "other.getDamageType different; absent.getDamageType missing; " +
                "if neq damage same; div cost_current 0; end; if eq damage melee; div cost_current 0; end; " +
                "if eq damage different; div cost_current 0; end; if neq missing null; div cost_current 0; end; ";
            foreach (var (method, index) in new[] { ("getRandomType", 2), ("getResistType", 3), ("isDirect", 4), ("isAreaOfEffect", 5), ("getArmorIgnore", 6) })
                code += $"damage.{method} value; if neq value {row[index].GetInt32()}; div cost_current 0; end; " +
                    $"missing.{method} value; if neq value 0; div cost_current 0; end; ";
            code += $"item_rule.isSingleTarget value; if neq value {row[8].GetInt32()}; div cost_current 0; end; return 7;";
            AssertPrice(Load(cache, global, code, type == -1 ? "" : $"damageType: {type}", extraItems: "- {type: PRICE_OTHER_DAMAGE}"));
        }
    }
}
