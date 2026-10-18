using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldStockResearchTests
{
    [Fact]
    public void StockPresenceAlternativesStayConsistentAcrossRepeatedGrants()
    {
        var content = StrategicWorldSiteResearchItemTests.Content(2);
        var campaign = CampaignState.Restore(StrategicWorldSiteResearchItemTests.Snapshot(content, "refuel", 2), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var item = content.RuntimeRules.Items.GetRequired("SUPPLY");
        var forecast = campaign.MissionResearch.CreateForecast(firstBaseItems: new Dictionary<RuleHandle<ItemRuleFamily>, int> { [item] = 1 },
            uncertainFirstBaseItems: Enumerable.Repeat(item, 300).ToArray());
        for (var grant = 0; grant < 20; grant++)
        {
            Assert.Null(forecast.Advance("CORE"));
            Assert.Contains("CORE", forecast.GuaranteedResearch);
            Assert.DoesNotContain("AUTO", forecast.GuaranteedResearch);
            Assert.Contains("AUTO", forecast.PossibleResearch);
            Assert.DoesNotContain("AUTO", forecast.PossibleExplicitTopics);
        }
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ExcessiveStockAlternativesAndWorkFailWithoutCampaignMutation()
    {
        var content = StrategicWorldSiteResearchItemTests.Content(2, "items:\n" +
            string.Join('\n', Enumerable.Range(0, 8).Select(index => "  - {type: STOCK_" + index + "}")));
        var campaign = CampaignState.Restore(StrategicWorldSiteResearchItemTests.Snapshot(content, "refuel", 2), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var items = new[] { content.RuntimeRules.Items.GetRequired("SUPPLY") }.Concat(
            Enumerable.Range(0, 8).Select(index => content.RuntimeRules.Items.GetRequired("STOCK_" + index))).ToArray();
        var stocks = items.ToDictionary(item => item, _ => 1);
        var forecast = campaign.MissionResearch.CreateForecast(firstBaseItems: stocks, uncertainFirstBaseItems: items.Take(8).ToArray());
        Assert.Null(forecast.Advance("CORE"));
        forecast = campaign.MissionResearch.CreateForecast(firstBaseItems: stocks, uncertainFirstBaseItems: items);
        Assert.Equal("Mission research exceeds the supported outcome limit.", forecast.Advance("CORE"));
        forecast = campaign.MissionResearch.CreateForecast(firstBaseItems: stocks, precedingWork: 1_000_000, uncertainFirstBaseItems: items);
        Assert.Equal("Mission research exceeds the supported discovery work limit.", forecast.Advance("CORE"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void PossiblyEmptyStocksCannotSuppressUnavailableDespawnOrDueEventEffects()
    {
        const string maintenance = """
            items:
              - {type: SUPPLY, clipSize: 3}
            craftWeapons:
              - {type: UNCERTAIN_GUN, ammoMax: 4, rearmRate: 6, bulletSaving: true, launcher: SUPPLY, clip: SUPPLY}
            """;
        var content = StrategicWorldSiteResearchItemTests.Content(2, maintenance, eventInterrupt: "AUTO");
        var initial = StrategicWorldSiteResearchItemTests.Snapshot(content, "refuel", 2) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial.WithCraft("SHIP_FUEL_ITEM", craft => craft with
        { RuleId = "SHIP", Logistics = craft.Logistics! with { Status = "STR_REARMING", Weapons = [new("UNCERTAIN_GUN", 0, Rearming: true)] } });
        AssertEventTickRuns(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), popped: ["STOCK_EVENT"]);
        content = StrategicWorldSiteResearchItemTests.Content(2, maintenance, eventInterrupt: "CORE");
        AssertEventTickRuns(CampaignState.Restore(initial with
        { World = initial.World with { Events = [new GeoscapeEventSnapshot("STOCK_AUTO_EVENT", 30)] } }, content, new SplitMix64RandomSource(17)), popped: ["STOCK_AUTO_EVENT"]);
        initial = initial with { World = initial.World with { Events = [new GeoscapeEventSnapshot("STOCK_EVENT", 30)] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Events);
    }
}
