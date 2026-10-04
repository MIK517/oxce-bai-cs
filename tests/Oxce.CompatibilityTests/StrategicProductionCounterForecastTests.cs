using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicProductionCounterForecastTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrepaidFirstUnitAndPossibleFollowupsBoundCraftIdsWithoutMutation(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-production");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
            facilities:
              - {type: FORECAST_WORKSHOP, mapName: LOGISTICS, size: 1, workshops: 10}
            manufacture:
              - name: FORECAST_CRAFT
                category: STR_CRAFT
                time: 2
                producedItems: {SHIP: 1}
            """);
        var initial = CreateSnapshot(content, "Craft counter forecast");
        var owner = initial.Bases[0] with { Facilities = [.. initial.Bases[0].Facilities, new("FORECAST_WORKSHOP", 5, 5, 0, 0, false, false, false)] };
        var floor = owner.Crafts.Where(craft => craft.RuleId == "SHIP").Max(craft => craft.Id) + 1L;
        foreach (var assigned in new[] { 1, 2, 6 })
        {
            var snapshot = initial with
            {
                Bases = [owner with { Productions = [new("FORECAST_CRAFT", assigned, 0, 3, false, false, false, new Dictionary<string, int>())] }],
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            var before = campaign.Capture();
            var count = assigned / 2;
            Assert.Equal(count == 0 ? (1L, 1L) : (floor + 1, floor + count),
                campaign.ProductionPreflight.CraftCounter("SHIP", CampaignTimeTrigger.OneHour, (1, 1)));
            Assert.Equal((1L, 1L), campaign.ProductionPreflight.CraftCounter("SHIP", CampaignTimeTrigger.ThirtyMinutes, (1, 1)));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            Reload(content, campaign, 17, "production-id-forecast.sav");
        }
    }
}
