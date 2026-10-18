using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseEscortFuelParityTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void UnequalEscortBurnsPreserveEligibilityAndNativeSavedOrder(bool reverse, int takeoff)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = Content();
        var deletedContent = Content("CORE");
        var survivedContent = Content("");
        foreach (var (fuel, low) in new[] { (100, false), (149, false), (200, false), (1500, false), (150, true) })
        {
            HashSet<bool> outcomes = [];
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = Snapshot(content, seed, reverse, takeoff, fuel, low);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                outcomes.Add(deleted);
                var leader = after.Bases[0].Crafts.Single(craft => craft.Id == 1).Logistics!;
                var follower = after.Bases[0].Crafts.Single(craft => craft.Id == 2).Logistics!;
                var expected = TestFixtures.Rows(oracle.RootElement, "optionalEscortFuelRows").Single(row =>
                    row[0].GetInt32() == fuel && row[1].GetBoolean() == low && row[2].GetBoolean() == deleted && row[3].GetBoolean() == !reverse);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    fuel, low, deleted, !reverse, leader.Fuel, leader.LowFuel, leader.Speed,
                    follower.Fuel, follower.LowFuel, follower.Speed, follower.Destination!.Kind == WorldTargetKind.Base,
                });
                TestFixtures.AssertJsonEqual(expected, actual);
                var from = new WorldPosition(1, 0.6);
                var home = new WorldPosition(0.4, 0.2);
                var leaderPosition = takeoff > 0 ? from : WorldGeometry.Move(from, home, WorldGeometry.RadianSpeed(leader.Speed));
                Assert.Equal(leaderPosition, new(leader.Longitude, leader.Latitude));
                var target = follower.Destination.Kind == WorldTargetKind.Base ? home : reverse ? from : leaderPosition;
                Assert.Equal(WorldGeometry.Move(from, target, WorldGeometry.RadianSpeed(2100)), new(follower.Longitude, follower.Latitude));
                Assert.True(Assert.Single(after.World.Ufos).Hunting);
                if (follower.LowFuel) Assert.Equal(1, after.World.Ufos[0].Destination!.Id);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                campaign = ReloadRepeatedly(content, campaign, seed, "optional-escort-fuel-parity.sav", 3, after);
                AssertContinuesLike(control, campaign, 1);
            }
            Assert.Equal(2, outcomes.Count);
        }
    }

    [Fact]
    public void EscortLowFuelDifferencesRemainAtomic()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content();
        foreach (var fuel in new[] { 150, 160 })
        {
            var rows = TestFixtures.Rows(oracle.RootElement, "optionalEscortFuelRows").Where(row =>
                row[0].GetInt32() == fuel && !row[1].GetBoolean() && row[3].GetBoolean()).ToArray();
            Assert.Equal(2, rows.Length);
            Assert.NotEqual(rows[0][8].GetBoolean(), rows[1][8].GetBoolean());
            AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, false, 0, fuel, false), content, new SplitMix64RandomSource(17)),
                "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
        }
        var malformed = Snapshot(content, 17, false, 0, 1000, false)
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 0 } })
            .WithCraft("OPTIONAL_NORMAL_ESCORT", craft => craft with { Logistics = craft.Logistics! with { Speed = -1 } });
        AssertTimeBlocked(CampaignState.Restore(malformed, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
    }

    internal static CampaignSnapshot WithBoundaryFuel(CampaignSnapshot initial, RuntimeContent content, int returnBurn)
    {
        var owner = initial.Bases[0];
        var craft = owner.Crafts.Single(craft => craft.Id == 2);
        var state = craft.Logistics!;
        var rules = content.RuntimeRules;
        var maximum = CraftLogistics.EffectiveSpeedMaximum(rules.Crafts[rules.Crafts.GetRequired(craft.RuleId)].Value, state.Weapons, rules);
        var threshold = WorldFlight.FuelLimit(false, maximum, WorldGeometry.Distance(
            new(state.Longitude, state.Latitude), new(owner.Longitude, owner.Latitude)));
        return initial.WithCraft(craft.RuleId, craft => craft with { Logistics = craft.Logistics! with { Fuel = threshold + returnBurn } });
    }

    internal static RuntimeContent Content(string code = "BONUS") =>
        StrategicWorldOptionalBaseCraftTests.Content(code, itemFuel: true, escortSpeedMaximum: 2100);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool reverse, int takeoff, int fuel, bool low)
    {
        var initial = StrategicWorldOptionalBaseAirborneCraftTests.Snapshot(content, seed, takeoff, false, true)
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 1000 } });
        var leader = initial.Bases[0].Crafts[0];
        var follower = leader with
        {
            RuleId = "OPTIONAL_NORMAL_ESCORT",
            Id = 2,
            Logistics = leader.Logistics! with
            {
                Fuel = fuel,
                LowFuel = low,
                Speed = 2100,
                Takeoff = 0,
                Destination = new(WorldTargetKind.Craft, "SHIP", 1, 1, 0.6),
            },
        };
        return initial with
        {
            Bases = [initial.Bases[0] with
            { Longitude = 0.4, Latitude = 0.2, Crafts = reverse ? [follower, leader] : [leader, follower] }]
        };
    }
}
