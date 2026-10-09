using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseRecallArrivalTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void EmptyFuelRecallConvergesBeforeNativeArrival(bool cache, int takeoff)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicWorldOptionalBaseFuelParityTests.Content(cache);
        var deletedContent = StrategicWorldOptionalBaseFuelParityTests.Content(cache, "CORE");
        var survivedContent = StrategicWorldOptionalBaseFuelParityTests.Content(cache, "");
        foreach (var fuel in new[] { 0, 1, 10 })
        {
            HashSet<bool> outcomes = [];
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = Snapshot(content, seed, takeoff, fuel, false);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                outcomes.Add(deleted);
                var state = after.Bases[0].Crafts[0].Logistics!;
                var expected = TestFixtures.Rows(oracle.RootElement, "optionalRecallArrivalRows").Single(row =>
                    row[0].GetInt32() == fuel && row[1].GetInt32() == takeoff && row[2].GetBoolean() == deleted);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    fuel, takeoff, deleted, state.Status != "STR_OUT", state.Fuel, state.Status,
                    state.Speed, state.Takeoff, state.LowFuel, state.MissionComplete, state.Destination is null,
                });
                Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
                Assert.Equal(new WorldPosition(0.4, 0.2), new(state.Longitude, state.Latitude));
                Assert.False(Assert.Single(after.World.Ufos).Hunting);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "optional-recall-arrival.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            }
            Assert.Equal(2, outcomes.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncertainRecallAndAlreadyLowFuelRemainAtomic(bool cache)
    {
        var content = StrategicWorldOptionalBaseFuelParityTests.Content(cache);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, 0, 21, false), content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft speed alternatives for uncertain research.");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, 5, 0, true), content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft arrival alternatives for uncertain research.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int takeoff, int fuel, bool low) =>
        StrategicWorldOptionalBaseFuelParityTests.Snapshot(content, seed, takeoff, fuel, low)
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Longitude = 0.4, Latitude = 0.2 } });
}
