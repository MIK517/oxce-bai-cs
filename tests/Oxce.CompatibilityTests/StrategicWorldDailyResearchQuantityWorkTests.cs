using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchQuantityWorkTests
{
    [Fact]
    public void FailedCachedWorldProjectionStillConsumesItsCallerWorkBudget()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var labels = string.Join(", ", Enumerable.Range(0, 257).Select(index => "WORK_REWARD_" + index));
        var rewards = string.Join("\n", Enumerable.Range(0, 257).Select(index => $"  - {{name: WORK_REWARD_{index}, cost: 1, points: 0, spawnedItem: SUPPLY, spawnedItemCount: {1 + index % 2}}}"));
        var padding = string.Join("\n", Enumerable.Range(0, 800).Select(index => $"  - {{name: WORK_PADDING_{index}, cost: 1, points: 0}}"));
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
            crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: '', autoPatrol: true}]
            facilities: [{type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}]
            ufos: [{type: UFO_HUNTER, radarRange: 20000}]
            research:
              - {name: UNLOCK, cost: 1, points: 0}
              - {name: CORE, cost: 1, points: 0, getOneFree: [{{labels}}], spawnedItem: SUPPLY}
            {{rewards}}
            {{padding}}
            """, extraRulesAfterOverlays: true);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        long work = 0;
        Assert.False(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out _));
        Assert.True(work > 1_000_000);
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
        AssertTimeBlocked(campaign, "UFO retargeting exceeds the supported work limit.");
        AssertTimeBlocked(campaign, "UFO retargeting exceeds the supported work limit.");
        Assert.Empty(campaign.Execute(new ConfigureResearchProject(initial.Bases[0].Id, "CORE", 0, Cancel: true)).Events.OfType<CampaignActionBlocked>());
        var canceled = campaign.Capture();
        var control = CampaignState.Restore(canceled, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        AdvanceUnblocked(control, 1);
        Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
    }
}
