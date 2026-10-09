using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseGroundedEscortTests
{
    [Theory]
    [InlineData(false, "STR_READY", false, false)]
    [InlineData(true, "STR_READY", false, false)]
    [InlineData(false, "STR_REPAIRS", false, false)]
    [InlineData(true, "STR_REPAIRS", false, false)]
    [InlineData(false, "STR_REARMING", false, false)]
    [InlineData(true, "STR_REARMING", false, false)]
    [InlineData(false, "STR_REFUELLING", false, false)]
    [InlineData(true, "STR_REFUELLING", false, false)]
    [InlineData(false, "STR_READY", true, false)]
    [InlineData(true, "STR_READY", true, false)]
    [InlineData(false, "STR_REPAIRS", true, false)]
    [InlineData(true, "STR_REPAIRS", true, false)]
    [InlineData(false, "STR_REARMING", true, false)]
    [InlineData(true, "STR_REARMING", true, false)]
    [InlineData(false, "STR_REFUELLING", true, false)]
    [InlineData(true, "STR_REFUELLING", true, false)]
    [InlineData(false, "STR_READY", false, true)]
    [InlineData(true, "STR_READY", false, true)]
    [InlineData(false, "STR_REFUELLING", true, true)]
    [InlineData(true, "STR_REFUELLING", true, true)]
    public void GroundedEscortServicingEitherIgnoresLeaderSpeedOrRelaunchesIndependently(bool cache, string status, bool auto, bool parked)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "maintenanceRows"));
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "hourlyMaintenanceRows"));
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var deletedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "CORE", true);
        var survivedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "", true);
        HashSet<bool> outcomes = [];
        for (var order = 0; order < 2; order++)
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
                    StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, seed, order != 0, 0))
                    .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = parked ? 0 : 500 } });
                initial = initial.WithCraft("OPTIONAL_NORMAL_ESCORT", craft => craft with
                {
                    Logistics = craft.Logistics! with
                    {
                        Status = status,
                        Speed = 500,
                        Takeoff = 7,
                        Longitude = 0.2,
                        Latitude = 0.1,
                        Damage = status == "STR_REPAIRS" ? 4 : 0,
                        Fuel = status == "STR_REFUELLING" ? 950 : 1000,
                        IsAutoPatrolling = auto,
                        AutoPatrolLongitude = 1.5,
                        AutoPatrolLatitude = 0.7,
                    }
                });
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                outcomes.Add(deleted);
                var leader = after.Bases[0].Crafts.Single(craft => craft.Id == 1).Logistics!;
                var follower = after.Bases[0].Crafts.Single(craft => craft.Id == 2).Logistics!;
                var launched = auto && status is "STR_REARMING" or "STR_REFUELLING";
                Assert.Equal(999, leader.Fuel);
                Assert.Equal(deleted ? 6000 : parked ? 0 : 500, leader.Speed);
                Assert.Equal(launched ? "STR_OUT" : status == "STR_REPAIRS" ? status : "STR_READY", follower.Status);
                Assert.Equal(launched ? 980 : 1000, follower.Fuel);
                Assert.Equal(launched ? 2000 : 500, follower.Speed);
                Assert.Equal(launched ? 59 : 7, follower.Takeoff);
                Assert.Equal(new WorldPosition(0.2, 0.1), new(follower.Longitude, follower.Latitude));
                Assert.Equal(auto, follower.IsAutoPatrolling);
                Assert.False(follower.LowFuel);
                Assert.Equal(launched ? WorldTargetKind.Waypoint : WorldTargetKind.Craft, follower.Destination!.Kind);
                if (launched) Assert.Equal(new WorldPosition(1.5, 0.7), follower.Destination.Position);
                else Assert.Equal(1, follower.Destination.Id);
                Assert.True(Assert.Single(after.World.Ufos).Hunting);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "optional-base-grounded-escort.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            }
        Assert.Equal(2, outcomes.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentRelaunchStillReservesWaypointIdentityBeforeResearch(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
            StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, 17, false, 0))
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 500 } })
            .WithCraft("OPTIONAL_NORMAL_ESCORT", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 950,
                    IsAutoPatrolling = true,
                    Longitude = 0.2,
                    Latitude = 0.1,
                    AutoPatrolLongitude = 1.5,
                    AutoPatrolLatitude = 0.7
                }
            })
            .WithNextId(WorldTargetReference.WaypointType, int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Waypoint IDs are exhausted or collide with a saved waypoint.");
    }

}
