using System.Text.Json;
using Oxce.Core.Random;
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
    [InlineData(0)]
    [InlineData(5)]
    public void EmptyFuelRecallConvergesBeforeNativeArrival(int takeoff)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicWorldOptionalBaseFuelParityTests.Content();
        var deletedContent = StrategicWorldOptionalBaseFuelParityTests.Content("CORE");
        var survivedContent = StrategicWorldOptionalBaseFuelParityTests.Content("");
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
                TestFixtures.AssertJsonEqual(expected, actual);
                Assert.Equal(new WorldPosition(0.4, 0.2), new(state.Longitude, state.Latitude));
                Assert.False(Assert.Single(after.World.Ufos).Hunting);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                campaign = ReloadRepeatedly(content, campaign, seed, "optional-recall-arrival.sav", 3, after);
                AssertContinuesLike(control, campaign, 1);
            }
            Assert.Equal(2, outcomes.Count);
        }
    }

    [Fact]
    public void UncertainRecallRemainsAtomicAndAlreadyLowFuelArrivalIsOwned()
    {
        var content = StrategicWorldOptionalBaseFuelParityTests.Content();
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, 0, 21, false), content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft speed alternatives for uncertain research.");
        var campaign = CampaignState.Restore(Snapshot(content, 17, 5, 0, true), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(after.CompletedResearch.Contains("BONUS"), after.Bases[0].Crafts[0].Logistics!.Status != "STR_OUT");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int takeoff, int fuel, bool low) =>
        StrategicWorldOptionalBaseFuelParityTests.Snapshot(content, seed, takeoff, fuel, low)
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Longitude = 0.4, Latitude = 0.2 } });
}
