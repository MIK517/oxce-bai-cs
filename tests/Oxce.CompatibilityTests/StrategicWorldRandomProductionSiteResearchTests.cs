using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRandomProductionSiteResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VariableInertProductionCannotSuppressUnavailableSitePayload(bool presence)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionInertSiteResearchRows").Where(row => row[0].GetInt32() == (presence ? 1 : 0) && row[1].GetInt32() == 0).ToArray();
        Assert.Equal(2, rows.Select(row => row[7].GetInt32()).Distinct().Count());
        var content = Content(presence);
        var initial = Snapshot(content, 0);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "inert-site-event-before.sav");
        AssertEventTickRuns(campaign, popped: ["STOCK_EVENT"]);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    public void InertProductionSiteResearchUsesActualItemOutcome(bool presence, int stock, bool eventPayload)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(presence, eventPayload);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Restore(Snapshot(content, stock) with { RandomState = seed }, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var owner = after.Bases[0];
            var history = Assert.Single(owner.Productions).RandomProductionInfo.GetValueOrDefault("TOKEN");
            var choice = history == 0 ? 1 : 2;
            choices.Add(choice);
            var row = TestFixtures.Rows(oracle.RootElement, "productionInertSiteResearchRows").Single(row => row[0].GetInt32() == (presence ? 1 : 0) && row[1].GetInt32() == stock && row[2].GetInt32() == choice);
            var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
            var craft = Assert.Single(owner.Crafts, craft => craft.RuleId == "SHIP_FUEL_ITEM").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { presence ? 1 : 0, stock, choice, progress.Produced, string.IsNullOrEmpty(progress.StopReason) ? 0 : 3,
                owner.Items.GetValueOrDefault("TOKEN"), history, after.CompletedResearch.Contains("AUTO") ? 1 : 0, craft.Fuel });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            Assert.Equal(owner.Items.GetValueOrDefault("TOKEN") != 0, after.ResearchDiary.Any(entry => entry.RuleId == "AUTO"));
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Events);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "inert-site-research-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    private static RuntimeContent Content(bool presence, bool eventPayload = true) => StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules(presence).Replace("    despawnEvents: {STOCK_EVENT: 1}", eventPayload ? "    despawnEvents: {STOCK_EVENT: 1}" : "", StringComparison.Ordinal));

    private static string Rules(bool presence) => $$"""
        items:
          - {type: TOKEN, battleType: 1, clipSize: 1}
        research:
          - {name: CORE, cost: 1}
          - {name: AUTO, cost: 0, dependencies: [CORE], needItem: true, neededItem: TOKEN}
        manufacture:
          - {name: ITEM_PRODUCT, time: 1, producedItems: {SUPPLY: 1}, randomProducedItems: [[1, {{(presence ? "{}" : "{TOKEN: 0}")}}], [1, {TOKEN: 2}]]}
        facilities:
          - {type: STORES, workshops: 2}
        alienDeployments:
          - type: DEPLOY_DIARY
            markerName: DIARY_SITE
            unlockedResearchOnDespawn: CORE
            counterDespawn: SITE_COUNT
            counterAll: SITE_COUNT
            despawnPenalty: 7
            despawnEvents: {STOCK_EVENT: 1}
        events:
          - {name: STOCK_EVENT, interruptResearch: AUTO}
        """;

    private static CampaignSnapshot Snapshot(RuntimeContent content, int stock)
    {
        var initial = StrategicWorldSiteResearchItemTests.Snapshot(content, "production", 1);
        return initial with
        {
            Bases = [initial.Bases[0] with
            {
                Items = stock == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["TOKEN"] = stock },
                Productions = [initial.Bases[0].Productions[0] with { Infinite = true, Amount = 3 }],
            }],
        };
    }
}
