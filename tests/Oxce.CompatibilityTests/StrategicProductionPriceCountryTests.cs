using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
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
    public void PriceCountryPactsAndXcomHistoriesStayStableBeforeMonthRollover(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "countryList");
        var code = "var int count; var int value; var ptr Country absent null; " +
            "absent.getPact value; if neq value 0; div cost_current 0; end; absent.getCurrentActivityXcom value; " +
            "if neq value 0; div cost_current 0; end; loop var country geoscape_game.getCountries.list; ";
        for (var index = 0; index < 2; index++)
            code += $"if eq count {index}; country.getPact value; if neq value {rows[index][5].GetInt32()}; div cost_current 0; end; " +
                $"country.getCurrentActivityXcom value; if neq value {rows[index][4].GetInt32()}; div cost_current 0; end; end; ";
        code += "add count 1; end; if neq count 2; div cost_current 0; end; return 7;";
        AssertPrice(Load(cache, global, code, extraRules: "countries: [{type: PRICE_COUNTRY_A}, {type: PRICE_COUNTRY_B}]"), snapshot: PriceCountries);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CurrentAndHourlyAlienActivityPricesExecuteWithConsumedOnlyReads(bool cache, bool global, bool consumed)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "countryList");
        var code = "var int count; var int value; var ptr Country absent null; absent.getCurrentActivityAlien value; " +
            "if neq value 0; div cost_current 0; end; loop var country geoscape_game.getCountries.list; " +
            $"if eq 1 {(consumed ? 1 : 0)}; ";
        for (var index = 0; index < 2; index++)
            code += $"if eq count {index}; country.getCurrentActivityAlien value; if neq value {rows[index][3].GetInt32()}; div cost_current 0; end; end; ";
        code += "end; add count 1; end; return 7;";
        var content = Load(cache, global, code, extraRules: "countries: [{type: PRICE_COUNTRY_A}, {type: PRICE_COUNTRY_B}]");
        var campaign = Reload(content, Create(content, initial => PriceStock(PriceCountries(initial))), 81, "country-current-price.sav");
        var before = campaign.Capture();
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(before.Bases[0].Id, LogisticsOperation.Sell)).Events)).Quote;
        Assert.Equal(7, Assert.Single(quote.Rows, row => row.RuleId == "PRICE_META_ITEM").UnitCost);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AssertPrice(content, snapshot: PriceCountries);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HourlyPricesReadActivityBeforeHalfHourUfoScores(bool cache, bool global)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world");
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var content = Load(cache, global,
            "var int activity; loop var country geoscape_game.getCountries.list; country.getCurrentActivityAlien activity; end; return activity;",
            extraRules: "countries: [{type: PRICE_COUNTRY_A, areas: [[0, 30, -20, 20]]}]\nufos: [{type: HUNTER, missionScore: 5}]\n");
        var campaign = Reload(content, Create(content, initial => initial with
        {
            Countries = [new("PRICE_COUNTRY_A", [2000], [9], [7], false, false, false, [])],
        }), 81, "hourly-country-score-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(7, after.Funds[^1]);
        Assert.Equal(12, Assert.Single(after.Countries).ActivityAlien[^1]);
        Reload(content, campaign, 81, "hourly-country-score-after.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConsumedDailyAlienActivityStillRequiresItsWorldProjection(bool cache, bool global)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var content = Load(cache, global,
            "var int value; loop var country geoscape_game.getCountries.list; country.getCurrentActivityAlien value; end; return 7;",
            extraRules: "countries: [{type: PRICE_COUNTRY_A}, {type: PRICE_COUNTRY_B}]");
        var campaign = Reload(content, Create(content, initial => PriceCountries(initial) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) }), 81, "daily-country-price-before.sav");
        AssertTimeBlocked(campaign,
            $"Price {(global ? "event" : "script")} failed: Script binding 'Country.getCurrentActivityAlien' failed: Production prices require world alien-activity forecasts..");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PriceCountryTagsRetainTypedOwnersSavedValuesAndNullDefaults(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var value = TestFixtures.Rows(oracle.RootElement, "tagValues")[2][1].GetInt32();
        var code = "var ptr Country absent null; var ptr RuleCountry rule; var ptr RuleCountry missing null; " +
            "var Country.Tag selected Tag.PRICE_SAVED_COUNTRY; var Country.Tag alias selected; var int value; var int count; " +
            "loop var country geoscape_game.getCountries.list; country.getTag value alias; " +
            $"if neq value {value}; div cost_current 0; end; country.getTag value Tag.PRICE_COUNTRY_MISSING; if neq value 0; div cost_current 0; end; " +
            "country.getRuleCountry rule; rule.getTag value Tag.PRICE_COUNTRY_RULE; " +
            "if neq value 37; div cost_current 0; end; add count 1; end; " +
            "absent.getTag value alias; if neq value 0; div cost_current 0; end; " +
            "missing.getTag value Tag.PRICE_COUNTRY_RULE; if neq value 0; div cost_current 0; end; " +
            "if neq count 2; div cost_current 0; end; return 7;";
        var content = Load(cache, global, code,
            extraRules: "countries: [{type: PRICE_COUNTRY_A, tags: {PRICE_COUNTRY_RULE: 37}}, {type: PRICE_COUNTRY_B, tags: {PRICE_COUNTRY_RULE: 37}}]",
            extraExtended: "tags:\n  Country: {PRICE_SAVED_COUNTRY: int, PRICE_COUNTRY_MISSING: int}\n  RuleCountry: {PRICE_COUNTRY_RULE: int}");
        AssertPrice(content, snapshot: initial => PriceCountries(initial) with
        {
            Countries = PriceCountries(initial).Countries.Select(country => country with { ScriptValues = [new("PRICE_SAVED_COUNTRY", "int", value)] }).ToArray(),
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CountryPriceLoopsPreserveSavedOrderFundingAndCanonicalRules(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "countryList");
        foreach (var cap in TestFixtures.Rows(oracle.RootElement, "countryRules"))
        {
            var code = "var int count; var int value; var ptr Country absent null; var ptr RuleCountry rule; var ptr RuleCountry alias; " +
                "absent.getCurrentFunding value; if neq value 0; div cost_current 0; end; " +
                "absent.getRuleCountry rule; rule.getFundingCap value; if neq value 0; div cost_current 0; end; " +
                "loop var country geoscape_game.getCountries.list; ";
            for (var index = 0; index < 2; index++)
                code += $"if eq count {index}; country.getCurrentFunding value; if neq value {rows[index][2].GetInt32()}; div cost_current 0; end; end; ";
            code += $"country.getRuleCountry rule; country.getRuleCountry alias; if neq rule alias; div cost_current 0; end; " +
                $"rule.getFundingCap value; if neq value {cap[0].GetInt32()}; div cost_current 0; end; add count 1; end; " +
                "if neq count 2; div cost_current 0; end; return 7;";
            var content = Load(cache, global, code, extraRules: $"countries: [{{type: PRICE_COUNTRY_A, fundingCap: {cap[0].GetInt32()}}}, {{type: PRICE_COUNTRY_B, fundingCap: {cap[0].GetInt32()}}}]");
            AssertPrice(content, snapshot: initial => PriceCountries(initial));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmptyNullAndNestedPriceCountryLoopsStayBounded(bool cache, bool empty)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var code = "var int count; var ptr GeoscapeGame absent null; " +
            "loop var ignored absent.getCountries.list; add count 100; end; " +
            "loop var outer geoscape_game.getCountries.list; loop var inner geoscape_game.getCountries.list; add count 1; end; end; " +
            $"if neq count {(empty ? 0 : 4)}; div cost_current 0; end; return 7;";
        var content = Load(cache, false, code, extraRules: "countries: [{type: PRICE_COUNTRY_A}, {type: PRICE_COUNTRY_B}]");
        AssertPrice(content, snapshot: initial => empty ? initial with { Countries = [] } : PriceCountries(initial));
    }

    private static CampaignSnapshot PriceCountries(CampaignSnapshot initial) => initial with
    {
        Countries =
        [
            new("PRICE_COUNTRY_B", [1, 3000], [2, 13], [3, 11], true, false, true, []),
            new("PRICE_COUNTRY_A", [1, 2000], [2, 9], [3, 7], false, true, false, []),
        ],
    };
}
