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

public sealed class StrategicWorldOptionalBaseFuelParityTests
{
    [Theory]
    [InlineData(0, 100, false)]
    [InlineData(5, 100, false)]
    [InlineData(0, 149, false)]
    [InlineData(5, 149, false)]
    [InlineData(0, 200, false)]
    [InlineData(5, 200, false)]
    [InlineData(0, 1500, false)]
    [InlineData(5, 1500, false)]
    [InlineData(0, 150, true)]
    [InlineData(5, 150, true)]
    public void UnequalFuelBurnsPreserveNativeEligibilityAndBothFlightOutcomes(int takeoff, int fuel, bool low)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        using var worldOracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        var limit = TestFixtures.Rows(worldOracle.RootElement, "craft").Single(row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 2100 && row[2].GetInt32() == 1000)[5].GetInt32();
        Assert.Equal(139, limit);
        var content = Content();
        var deletedContent = Content("CORE");
        var survivedContent = Content("");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, takeoff, fuel, low);
            Assert.Equal(limit, WorldFlight.FuelLimit(false, 2100, WorldGeometry.Distance(new(1, 0.6), new(0.4, 0.2))));
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var state = after.Bases[0].Crafts[0].Logistics!;
            var expected = TestFixtures.Rows(oracle.RootElement, "optionalReturnFuelRows").Single(row =>
                row[0].GetInt32() == limit && row[1].GetInt32() == fuel && row[2].GetBoolean() == low && row[3].GetBoolean() == deleted);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                limit, fuel, low, deleted, state.Fuel, state.LowFuel, state.Speed, state.Destination!.Kind == WorldTargetKind.Base,
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.Equal(Math.Max(0, takeoff - 1), state.Takeoff);
            var position = takeoff > 0 ? new WorldPosition(1, 0.6) :
                WorldGeometry.Move(new(1, 0.6), new(0.4, 0.2), WorldGeometry.RadianSpeed(state.Speed));
            Assert.Equal(position, new(state.Longitude, state.Latitude));
            Assert.Equal(!state.LowFuel, Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-fuel-parity.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Fact]
    public void DifferentLowFuelEligibilityRetainsTheAtomicBoundary()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content();
        foreach (var fuel in new[] { 150, 160 })
        {
            var rows = TestFixtures.Rows(oracle.RootElement, "optionalReturnFuelRows").Where(row =>
                row[0].GetInt32() == 139 && row[1].GetInt32() == fuel && !row[2].GetBoolean()).ToArray();
            Assert.Equal(2, rows.Length);
            Assert.NotEqual(rows[0][5].GetBoolean(), rows[1][5].GetBoolean());
            AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, 0, fuel, false), content, new SplitMix64RandomSource(17)),
                "Alien base self-destruction requires craft speed alternatives for uncertain research.");
        }
    }

    internal static RuntimeContent Content(string code = "BONUS") =>
        StrategicWorldOptionalBaseCraftTests.Content(code, speedMaximum: 2100);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int takeoff, int fuel, bool low)
    {
        var initial = StrategicWorldOptionalBaseAirborneCraftTests.Snapshot(content, seed, takeoff, false, true)
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 1000, Fuel = fuel, LowFuel = low } });
        return initial with { Bases = [initial.Bases[0] with { Longitude = 0.4, Latitude = 0.2 }] };
    }
}
