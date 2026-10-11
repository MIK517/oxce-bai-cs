using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Price invocation setup is cached per item and parser. Repeated prices, refusals and the
/// script/event failure wording must not depend on whether the setup was already built.
/// </summary>
public sealed class StrategicPriceInvocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedQuotesReuseSetupWithIdenticalResults(bool global)
    {
        var content = Load(global, "var int count; loop var country geoscape_game.getCountries.list; add count 1; end; " +
            "add cost_current count; add cost_current 5; return cost_current;");
        var campaign = Campaign(content);
        var first = PriceRows(campaign);
        var second = PriceRows(campaign);
        Assert.Equal(first, second);
        Assert.All(first.Where(row => row.RuleId != "PRICE_CACHE_PLAIN" || global), row => Assert.Null(row.UnavailableReason));
        // The unscripted item keeps its adjusted price unless the global event applies to it too.
        var plain = first.Single(row => row.RuleId == "PRICE_CACHE_PLAIN");
        var scripted = first.Single(row => row.RuleId == "PRICE_CACHE_A");
        Assert.Equal(global, plain.UnitCost == scripted.UnitCost);
        Assert.Equal(first.Single(row => row.RuleId == "PRICE_CACHE_B").UnitCost, scripted.UnitCost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedBindingIsRefusedEveryTime(bool global)
    {
        var content = Load(global, "rules.getSpriteOffsetBasebits cost_current 0; return cost_current;");
        var campaign = Campaign(content);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal("Price script requires unsupported binding RuleMod.getSpriteOffsetBasebits.",
                PriceRows(campaign).Single(row => row.RuleId == "PRICE_CACHE_A").UnavailableReason);
    }

    [Theory]
    [InlineData(false, "Price script failed: ")]
    [InlineData(true, "Price event failed: ")]
    public void FailureWordingFollowsTheInvocationKind(bool global, string prefix)
    {
        var content = Load(global, "div cost_current 0; return cost_current;");
        var campaign = Campaign(content);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.StartsWith(prefix, PriceRows(campaign).Single(row => row.RuleId == "PRICE_CACHE_B").UnavailableReason);
    }

    private static RuntimeContent Load(bool global, string code)
    {
        var script = $"    scripts:\n      buyCostItem: |\n        {code}\n";
        var rules = "items:\n" +
            "  - type: PRICE_CACHE_A\n    costBuy: 10\n" + (global ? "" : script) +
            "  - type: PRICE_CACHE_B\n    costBuy: 10\n" + (global ? "" : script) +
            "  - type: PRICE_CACHE_PLAIN\n    costBuy: 10\n" +
            (global ? $"extended:\n  scripts:\n    buyCostItem:\n      - new: price-cache\n        offset: 1\n        code: |\n          {code}\n" : "");
        return StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: rules);
    }

    private static CampaignState Campaign(RuntimeContent content) =>
        CampaignState.Restore(CreateSnapshot(content, "Price invocation cache"), content, new SplitMix64RandomSource(7));

    private static LogisticsRow[] PriceRows(CampaignState campaign)
    {
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(campaign.Capture().Bases[0].Id, LogisticsOperation.Purchase)).Events)).Quote;
        return [.. quote.Rows.Where(row => row.RuleId.StartsWith("PRICE_CACHE_", StringComparison.Ordinal))];
    }
}
