using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>Craft::setDestination/think/consumeFuel and GeoscapeState timed movement.</summary>
public sealed class StrategicWorldCraftOperationsTests
{
    [Fact]
    public void DispatchWaitsSixtyTicksThenMovesAndRecallsAcrossSave()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var before = Ship(campaign.Capture());
        var baseId = campaign.Capture().Bases[0].Id;
        Assert.Equal(6000, content.RuntimeRules.Crafts[
            content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.SpeedMaximum);

        var dispatched = Assert.IsType<CraftDestinationChanged>(Assert.Single(
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.2001, 0.1)).Events));
        Assert.Equal(WorldTargetKind.Waypoint, dispatched.Destination!.Kind);
        Assert.Equal(60, Ship(campaign.Capture()).Logistics!.Takeoff);

        var start = campaign.Capture();
        var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(start),
            content, seed: 31, name: "dispatch.sav").Campaign;
        Assert.Equal(start.World.Waypoints, loaded.Capture().World.Waypoints);
        Assert.Equal(60, Ship(loaded.Capture()).Logistics!.Takeoff);

        var takeoff = loaded.Execute(new AdvanceCampaignTime(60));
        Assert.DoesNotContain(takeoff.Events, notification => notification is CampaignActionBlocked);
        Assert.Equal(before.Logistics!.Longitude, Ship(loaded.Capture()).Logistics!.Longitude);
        Assert.Equal(0, Ship(loaded.Capture()).Logistics!.Takeoff);

        loaded.Execute(new AdvanceCampaignTime(1));
        Assert.NotEqual(before.Logistics.Longitude, Ship(loaded.Capture()).Logistics!.Longitude);
        Assert.IsType<CraftDestinationChanged>(Assert.Single(
            loaded.Execute(new RecallCraft(baseId, "SHIP", 1)).Events));
        var returned = loaded.Execute(new AdvanceCampaignTime(1000));
        Assert.Contains(returned.Events, notification => notification is CraftReturnedToBase);
        var final = Ship(loaded.Capture()).Logistics!;
        Assert.Null(final.Destination);
        Assert.Equal("STR_REFUELLING", final.Status);
        Assert.Equal(before.Logistics.Longitude, final.Longitude);
        Assert.Equal(before.Logistics.Latitude, final.Latitude);
        Assert.Empty(loaded.Capture().World.Waypoints);
    }

    [Fact]
    public void LowFuelReturnsCraftAndInvalidDispatchDoesNotCreateWaypoint()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content, fuel: 1);
        var before = campaign.Capture();
        var baseId = before.Bases[0].Id;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 7, 0)));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "CARRIER", 100, 0.3, 0.1)));
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.4, 0.1));
        var elapsed = campaign.Execute(new AdvanceCampaignTime(121));
        Assert.Contains(elapsed.Events, notification => notification is CraftLowFuel);
        Assert.Contains(elapsed.Events, notification => notification is CraftReturnedToBase);
        var flight = Ship(campaign.Capture()).Logistics!;
        Assert.Equal(0, flight.Fuel);
        Assert.False(flight.LowFuel);
        Assert.Null(flight.Destination);
        Assert.Equal("STR_REFUELLING", flight.Status);
    }

    [Fact]
    public void PatrolWithoutFuelAndAutomaticRelaunchFollowReferenceHandlerOrder()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var baseId = campaign.Capture().Bases[0].Id;
        campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.4, 0.1));
        campaign.Execute(new AdvanceCampaignTime(61));
        campaign.Execute(new PatrolCraft(baseId, "SHIP", 1));
        var beforePatrol = Ship(campaign.Capture()).Logistics!;
        Assert.True(beforePatrol.IsAutoPatrolling);
        Assert.Null(beforePatrol.Destination);

        campaign.Execute(new AdvanceCampaignTime(60));
        Assert.Equal(beforePatrol.Fuel, Ship(campaign.Capture()).Logistics!.Fuel);

        var saved = campaign.Capture();
        var ship = Ship(saved);
        var refuelling = ship with
        {
            Logistics = ship.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 1,
                Longitude = saved.Bases[0].Longitude,
                Latitude = saved.Bases[0].Latitude,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            },
        };
        var atBoundary = saved with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Bases = [saved.Bases[0] with
            {
                Crafts = [.. saved.Bases[0].Crafts.Select(existing =>
                    existing.RuleId == "SHIP" ? refuelling : existing)],
            }],
        };
        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(atBoundary),
            content, seed: 32, name: "auto-patrol.sav").Campaign;
        reloaded.Execute(new AdvanceCampaignTime(1));
        var relaunched = Ship(reloaded.Capture()).Logistics!;
        Assert.Equal("STR_OUT", relaunched.Status);
        Assert.True(relaunched.IsAutoPatrolling);
        Assert.Equal(WorldTargetKind.Waypoint, relaunched.Destination!.Kind);
        Assert.Equal(0.3, relaunched.Destination.Longitude);
        Assert.Equal(59, relaunched.Takeoff);
    }

    private static CampaignState ReadyCampaign(RuntimeContent content, int fuel = 100)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "World operations",
            CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        var craft = Ship(snapshot);
        var ready = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_READY",
                Fuel = fuel,
                Damage = 0,
                Longitude = 0.2,
                Latitude = 0.1,
            },
        };
        return CampaignState.Restore(snapshot with
        {
            MonthsPassed = 0,
            Bases = [snapshot.Bases[0] with
            {
                Crafts = [.. snapshot.Bases[0].Crafts.Select(existing =>
                    existing.RuleId == "SHIP" ? ready : existing)],
            }],
        }, content, new SplitMix64RandomSource(19));
    }

    private static CraftSnapshot Ship(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "SHIP");
}
