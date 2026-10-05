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
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void FallbackStaffingBoundsRetainTheNoCompletionAlternative(bool cache, bool arrivingEngineers, bool completingWorkshop)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-production");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
            facilities:
              - {type: FORECAST_WORKSHOP, mapName: LOGISTICS, size: 1, workshops: 10}
            manufacture:
              - name: FORECAST_FALLBACK
                category: STR_CRAFT
                time: 2
                producedItems: {SHIP: 1}
            """);
        var initial = CreateSnapshot(content, "Fallback craft counter") with
        {
            Time = completingWorkshop ? new(1, 1, 1, 1999, 23, 59, 55) : new(1, 1, 1, 1999, 1, 59, 55),
        };
        var owner = initial.Bases[0];
        var next = owner.Crafts.Where(craft => craft.RuleId == "SHIP").Max(craft => craft.Id) + 1;
        initial = initial with
        {
            Bases = [owner with
            {
                Engineers = arrivingEngineers ? 0 : 2,
                Transfers = arrivingEngineers ? [new(700, 1, CampaignTransferKind.Engineer, "", 2)] : [],
                Facilities = [.. owner.Facilities, new("FORECAST_WORKSHOP", 5, 5, completingWorkshop ? 1 : 0, 0, false, false, false)],
                Productions = [new("FORECAST_FALLBACK", 0, 0, 1, false, false, true, new Dictionary<string, int>())],
            }],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Equal((1L, next + 1L), campaign.ProductionPreflight.CraftCounter("SHIP",
            completingWorkshop ? CampaignTimeTrigger.OneDay : CampaignTimeTrigger.OneHour, (1, 1)));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 1);
        Assert.Contains(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "SHIP" && craft.Id == next);
        Reload(content, campaign, 17, "fallback-produced-craft.sav");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void CraftConsumptionKeepsThePersistedCounterAsTheGuaranteedLowerBound(bool cache, int quantity)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-production");
        TestFixtures.LoadVerifiedManifest("equipment-production-rules");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
            facilities:
              - {type: FORECAST_WORKSHOP, mapName: LOGISTICS, size: 1, workshops: 10}
            manufacture:
              - name: FORECAST_CONSUME
                time: 1
                requiredItems: {SHIP: {{quantity}}}
                producedItems: {SUPPLY: 1}
              - name: FORECAST_REPLACE
                category: STR_CRAFT
                time: 1
                producedItems: {SHIP: 1}
            """);
        var initial = CreateSnapshot(content, "Consumed craft counter").WithNextId("SHIP", 1) with
        {
            Time = new(1, 1, 1, 1999, 1, 59, 55),
        };
        var owner = initial.Bases[0];
        var floor = owner.Crafts.Where(craft => craft.RuleId == "SHIP").Max(craft => craft.Id) + 1L;
        initial = initial with
        {
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities, new("FORECAST_WORKSHOP", 5, 5, 0, 0, false, false, false)],
                Productions =
                [
                    new("FORECAST_CONSUME", 2, 0, 2, false, false, false, new Dictionary<string, int>()),
                    new("FORECAST_REPLACE", 1, 0, 1, false, false, false, new Dictionary<string, int>()),
                ],
            }],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var range = campaign.ProductionPreflight.CraftCounter("SHIP", CampaignTimeTrigger.OneHour, (1, 1));
        Assert.Equal((2L, floor + 1), range);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.InRange((long)after.NextIds["SHIP"], range.Minimum, range.Maximum);
        Assert.Empty(after.Bases[0].Productions);
        Reload(content, campaign, 17, "consumed-produced-craft.sav");
    }

}
