using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
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
