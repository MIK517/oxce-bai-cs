using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldWaypointTests
{
    private const string IdReason = "Waypoint IDs are exhausted or collide with a saved waypoint.";

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void DispatchRejectsCollisionOrExhaustionWithoutMutation(int nextId)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Dispatch IDs").WithNextId(WorldTargetReference.WaypointType, nextId)
            .WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                { Status = "STR_READY", Fuel = 100, Weapons = [] }
            });
        snapshot = snapshot with { World = snapshot.World with { Waypoints = [new WaypointSnapshot(1, 0.8, 0.1)] } };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Equal(IdReason, Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(snapshot.Bases[0].Id, "SHIP", 1, 0.3, 0.1)).Events)).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData("refuelling", false)]
    [InlineData("refuelling", true)]
    [InlineData("rearming", false)]
    [InlineData("rearming", true)]
    [InlineData("transfer", false)]
    [InlineData("transfer", true)]
    public void RelaunchReservesIdsForAllCraftBeforeServicingOrTransfers(string source, bool collision)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Relaunch IDs") with
        { Time = new CampaignTime(1, 1, 1, 1999, 1, source == "refuelling" ? 29 : 59, 55) };
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts.First(c => c.RuleId == "SHIP");
        craft = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = source == "rearming" ? "STR_REARMING" : "STR_REFUELLING",
                Fuel = 1,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            }
        };
        snapshot = snapshot.WithCraft("SHIP", _ => craft);
        owner = snapshot.Bases[0];
        var second = craft with { Id = 9 };
        snapshot = snapshot.WithNextId(WorldTargetReference.WaypointType, collision ? 1 : int.MaxValue - 1) with
        {
            Bases = [owner with
            {
                Crafts = source == "transfer" ? owner.Crafts : [.. owner.Crafts, second],
                Transfers = source == "transfer" ? [new TransferSnapshot(77, 1,
                    CampaignTransferKind.Craft, "SHIP", 1, Craft: second)] : [],
            }],
            World = snapshot.World with { Waypoints = collision ? [new WaypointSnapshot(2, 0.8, 0.1)] : [] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, IdReason);

        // With a valid two-ID range, the same servicing/arrival paths actually relaunch both craft.
        campaign = CampaignState.Restore(snapshot.WithNextId(WorldTargetReference.WaypointType, 10),
            content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal<int>([10, 11], campaign.Capture().World.Waypoints.Select(w => w.Id));
        Assert.All(campaign.Capture().Bases[0].Crafts.Where(c => c.RuleId == "SHIP"),
            c => Assert.Equal("STR_OUT", c.Logistics!.Status));
        Reload(content, campaign, 99, "waypoint-reservation.sav");
    }

    [Fact]
    public void ArrivingFuelCanRelaunchAReadyCraftAndMustReserveAWaypoint()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Fuel-triggered relaunch")
            .WithNextId(WorldTargetReference.WaypointType, int.MaxValue)
            .WithCraft("SHIP", c => c with
            {
                RuleId = "SHIP_FUEL_ITEM",
                Logistics = c.Logistics! with
                {
                    Status = "STR_READY",
                    Fuel = 1,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.3,
                    AutoPatrolLatitude = 0.1,
                },
            });
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            Bases = [snapshot.Bases[0] with
            {
                Items = new Dictionary<string, int>(),
                Transfers = [new TransferSnapshot(77, 1, CampaignTransferKind.Item, "SUPPLY", 1)],
            }],
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)), IdReason);
        var campaign = CampaignState.Restore(snapshot.WithNextId(WorldTargetReference.WaypointType, 10),
            content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(10, Assert.Single(campaign.Capture().World.Waypoints).Id);
        Assert.Equal("STR_OUT", Assert.Single(campaign.Capture().Bases[0].Crafts,
            c => c.RuleId == "SHIP_FUEL_ITEM").Logistics!.Status);
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
    }

    [Fact]
    public void LastUsableWaypointIdWorksAndExhaustionDoesNotBlockUnrelatedTicks()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Last waypoint ID")
            .WithNextId(WorldTargetReference.WaypointType, int.MaxValue - 1)
            .WithCraft("SHIP", c => c with
            {
                Logistics = c.Logistics! with
                { Status = "STR_READY", Fuel = 100, Weapons = [], IsAutoPatrolling = true }
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new DispatchCraftToWaypoint(snapshot.Bases[0].Id, "SHIP", 1, 0.3, 0.1));
        Assert.Equal(int.MaxValue - 1, Assert.IsType<CraftDestinationChanged>(Assert.Single(result.Events)).Destination!.Id);
        Assert.Equal(int.MaxValue, campaign.Capture().NextIds[WorldTargetReference.WaypointType]);
        AdvanceOne(campaign); // Hourly boundary, but this craft is already airborne.
        Reload(content, campaign, 99, "last-waypoint.sav");
    }
}
