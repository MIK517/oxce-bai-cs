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
        var moved = Ship(loaded.Capture()).Logistics!;
        var expected = WorldGeometry.Move(new WorldPosition(before.Logistics.Longitude, before.Logistics.Latitude),
            new WorldPosition(0.2001, 0.1), WorldGeometry.RadianSpeed(moved.Speed));
        Assert.Equal(expected.Longitude, moved.Longitude);
        Assert.Equal(expected.Latitude, moved.Latitude);
        Assert.IsType<CraftDestinationChanged>(Assert.Single(
            loaded.Execute(new RecallCraft(baseId, "SHIP", 1)).Events));
        var returned = loaded.Execute(new AdvanceCampaignTime(1000));
        Assert.Equal(1000, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(returned.Events)).Summary.TickCount);
        var final = Ship(loaded.Capture()).Logistics!;
        Assert.Null(final.Destination);
        Assert.Equal("STR_READY", final.Status);
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

    [Fact]
    public void ReadyAutoPatrolFlagDoesNotLaunchWithoutRefuelling()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var ship = Ship(snapshot);
        var flagged = ship with
        {
            Logistics = ship.Logistics! with
            {
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            }
        };
        var restored = CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts.Select(existing =>
                existing.RuleId == "SHIP" ? flagged : existing)] }],
        }, content, new SplitMix64RandomSource(33));

        var elapsed = restored.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
        Assert.Equal("STR_READY", Ship(restored.Capture()).Logistics!.Status);
        Assert.Empty(restored.Capture().World.Waypoints);
    }

    [Fact]
    public void UnsupportedCraftDestinationBlocksBeforeTimeAdvances()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var ship = Ship(snapshot);
        var following = ship with
        {
            Logistics = ship.Logistics! with
            {
                Status = "STR_OUT",
                Speed = 6000,
                Destination = new WorldTargetReference(WorldTargetKind.Craft, "SHIP", ship.Id, 0.2, 0.1),
            }
        };
        var restored = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts.Select(existing =>
                existing.RuleId == "SHIP" ? following : existing)] }],
        }, content, new SplitMix64RandomSource(34));

        var elapsed = restored.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(elapsed.Events[0]).Summary.TickCount);
        Assert.Equal("Craft pursuit and landing require world simulation.",
            Assert.IsType<CampaignActionBlocked>(elapsed.Events[^1]).Reason);
        Assert.Equal(snapshot.Time, restored.Capture().Time);
    }

    [Fact]
    public void AirborneCraftAtUnplacedBaseReportsItsPreflightReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var craft = Ship(snapshot);
        var unplaced = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with
            {
                Name = "",
                Crafts = [.. snapshot.Bases[0].Crafts.Select(existing => existing.RuleId == "SHIP"
                    ? craft with { Logistics = craft.Logistics! with { Status = "STR_OUT" } }
                    : existing)],
            }],
        }, content, new SplitMix64RandomSource(49));

        var before = unplaced.Capture();
        var result = unplaced.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("An airborne craft belongs to a base that is not placed.",
            Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, unplaced.Capture(), strict: true);
    }

    [Theory]
    [InlineData("patrol", "Craft auto-patrol coordinates are invalid.")]
    [InlineData("takeoff", "Airborne craft fuel or takeoff state is out of range.")]
    [InlineData("position", "Airborne craft coordinates are invalid.")]
    public void InvalidCraftFlightStateReportsItsPreflightReason(string condition, string reason)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var craft = Ship(snapshot);
        var logistics = craft.Logistics! with { Status = "STR_OUT" };
        logistics = condition switch
        {
            "patrol" => logistics with { IsAutoPatrolling = true, AutoPatrolLongitude = 7 },
            "takeoff" => logistics with { Takeoff = -1 },
            "position" => logistics with { Longitude = 7 },
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };
        var restored = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts.Select(existing =>
                existing.RuleId == "SHIP" ? craft with { Logistics = logistics } : existing)] }],
        }, content, new SplitMix64RandomSource(60));

        AssertTimeBlocked(restored, reason);
    }

    [Fact]
    public void CraftSpeedOverflowReportsItsPreflightReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-speed-range.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var craft = Ship(snapshot);
        var restored = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts.Select(existing =>
                existing.RuleId == "SHIP" ? craft with
                { Logistics = craft.Logistics! with
                    { Status = "STR_OUT", Weapons = [new CraftWeaponSnapshot("FIXED", 0), null] } } : existing)] }],
        }, content, new SplitMix64RandomSource(61));

        AssertTimeBlocked(restored, "Craft speed exceeds the supported range.");
    }

    [Fact]
    public void DispatchRejectsItemCountAndStorageOveragesBeforeMakingWaypoint()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-capacity.rul");
        var rule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value;
        Assert.Equal(1, rule.MaximumItems);
        Assert.Equal(1, rule.MaximumStorageSpace);
        var fixedWeapon = content.RuntimeRules.CraftWeapons[
            content.RuntimeRules.CraftWeapons.GetRequired("FIXED")].Value;
        Assert.Equal(2, fixedWeapon.BonusStats["maxItems"]);
        Assert.Equal(2, fixedWeapon.BonusStorageSpace);
        foreach (var (items, allowed) in new[]
        {
            (new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 4 }, false),
            (new Dictionary<string, int>(StringComparer.Ordinal) { ["BULKY"] = 2 }, false),
            (new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 3 }, true),
        })
        {
            var ready = ReadyCampaign(content);
            var snapshot = ready.Capture();
            var ship = Ship(snapshot);
            var restored = CampaignState.Restore(snapshot with
            {
                Bases = [snapshot.Bases[0] with
                {
                    Crafts = [.. snapshot.Bases[0].Crafts.Select(existing => existing.RuleId == "SHIP"
                        ? ship with
                        {
                            Logistics = ship.Logistics! with
                            {
                                Items = items,
                                Weapons = [new CraftWeaponSnapshot("FIXED", 0), null],
                            },
                        }
                        : existing)],
                }],
            }, content, new SplitMix64RandomSource(50));
            var before = restored.Capture();

            var result = restored.Execute(new DispatchCraftToWaypoint(snapshot.Bases[0].Id,
                "SHIP", ship.Id, 0.3, 0.1));

            if (allowed)
            {
                Assert.True(Assert.Single(result.Events) is CraftDestinationChanged,
                    $"Expected dispatch, got {Assert.Single(result.Events)}.");
            }
            else
            {
                Assert.Equal("The craft carries too many items.",
                    Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Reason);
                Assert.Equivalent(before, restored.Capture(), strict: true);
            }
        }
    }

    [Fact]
    public void DispatchRechecksArmorOnRestoredCrew()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-armor.rul");
        var ready = ReadyCampaign(content);
        var snapshot = ready.Capture();
        var baseId = snapshot.Bases[0].Id;
        var soldier = snapshot.Bases[0].Soldiers.First(s => s.Personal is not null);
        var campaign = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with
            {
                Soldiers = [.. snapshot.Bases[0].Soldiers.Select(existing => existing.Id == soldier.Id
                    ? existing with { Personal = existing.Personal! with { CraftType = "SHIP", CraftId = 1 } }
                    : existing)],
            }],
        }, content, new SplitMix64RandomSource(55));

        var blocked = campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.3, 0.1));
        Assert.Equal("The craft carries armor forbidden by its rules.",
            Assert.IsType<CampaignActionBlocked>(Assert.Single(blocked.Events)).Reason);
        Assert.Empty(campaign.Capture().World.Waypoints);

        Assert.IsType<CampaignPersonnelChanged>(Assert.Single(
            campaign.Execute(new AssignSoldierToCraft(baseId, soldier.Id)).Events));
        Assert.IsType<CraftDestinationChanged>(Assert.Single(
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.3, 0.1)).Events));
    }

    [Fact]
    public void ZeroSpeedStationaryPatrolCanAdvanceTime()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-slow.rul");
        var campaign = ReadyCampaign(content);
        var baseId = campaign.Capture().Bases[0].Id;
        campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.3, 0.1));
        var beforePatrol = Ship(campaign.Capture()).Logistics!;
        campaign.Execute(new PatrolCraft(baseId, "SHIP", 1));
        Assert.Equal(0, Ship(campaign.Capture()).Logistics!.Speed);
        Assert.Equal(beforePatrol.AutoPatrolLongitude, Ship(campaign.Capture()).Logistics!.AutoPatrolLongitude);

        var elapsed = campaign.Execute(new AdvanceCampaignTime(3));

        Assert.Equal(3, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
        Assert.Equal("STR_OUT", Ship(campaign.Capture()).Logistics!.Status);
    }

    [Fact]
    public void WaypointRemainsUntilItsLastCraftLeaves()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var baseId = campaign.Capture().Bases[0].Id;
        campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.4, 0.1));
        var snapshot = campaign.Capture();
        var first = Ship(snapshot);
        var second = first with { Id = 2 };
        var restored = CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts, second] }],
        }, content, new SplitMix64RandomSource(35));

        restored.Execute(new RecallCraft(baseId, "SHIP", 1));
        restored.Execute(new AdvanceCampaignTime(1));
        Assert.Single(restored.Capture().World.Waypoints);

        restored.Execute(new RecallCraft(baseId, "SHIP", 2));
        restored.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(restored.Capture().World.Waypoints);
    }

    [Fact]
    public void FlightAllocationsScaleWithCraftCount()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var four = FlightAllocations(4);
        var sixteen = FlightAllocations(16);
        Assert.True(sixteen < four * 5,
            $"Four crafts allocated {four} bytes; sixteen allocated {sixteen} bytes.");

        long FlightAllocations(int count)
        {
            var campaign = ReadyCampaign(content);
            var baseId = campaign.Capture().Bases[0].Id;
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.4, 0.1));
            var snapshot = campaign.Capture();
            var first = Ship(snapshot);
            var crafts = snapshot.Bases[0].Crafts.Concat(Enumerable.Range(2, count - 1)
                .Select(id => first with { Id = id })).ToArray();
            var restored = CampaignState.Restore(snapshot with
            {
                Bases = [snapshot.Bases[0] with { Crafts = crafts }],
            }, content, new SplitMix64RandomSource(36));
            restored.Execute(new AdvanceCampaignTime(61)); // finish takeoff and warm the movement path
            var before = GC.GetAllocatedBytesForCurrentThread();
            var elapsed = restored.Execute(new AdvanceCampaignTime(10));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
            return allocated;
        }
    }

    [Fact]
    public void DispatchRequiresPilotsOnBoard()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-pilots.rul");
        Assert.Equal(1, content.RuntimeRules.Crafts[
            content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.Pilots);
        var campaign = ReadyCampaign(content, unarmed: true);
        var baseId = campaign.Capture().Bases[0].Id;
        var crew = campaign.Capture().Bases[0].Soldiers
            .Where(soldier => soldier.Personal!.CraftType == "SHIP").Select(soldier => soldier.Id).ToArray();
        Assert.NotEmpty(crew);
        foreach (var soldier in crew) campaign.Execute(new AssignSoldierToCraft(baseId, soldier));
        var snapshot = campaign.Capture();
        Assert.All(snapshot.Bases[0].Soldiers, soldier => Assert.NotEqual("SHIP", soldier.Personal!.CraftType));

        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.3, 0.1)).Events));

        Assert.Equal("The craft does not have enough pilots.", blocked.Reason);
        Assert.Empty(campaign.Capture().World.Waypoints);
        Assert.Equal("STR_READY", Ship(campaign.Capture()).Logistics!.Status);

        Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
            new AssignSoldierToCraft(baseId, crew[0], "SHIP", 1)).Events));

        Assert.IsType<CraftDestinationChanged>(Assert.Single(
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.3, 0.1)).Events));
        Assert.Equal("STR_OUT", Ship(campaign.Capture()).Logistics!.Status);
    }

    [Fact]
    public void MotionlessCraftKeepsTimeMovingAndStaysRecallable()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-slow.rul");
        Assert.Equal(0, content.RuntimeRules.Crafts[
            content.RuntimeRules.Crafts.GetRequired("INTERCEPTOR")].Value.SpeedMaximum);
        var restored = ReadyInterceptor(content);
        var baseId = restored.Capture().Bases[0].Id;
        Assert.IsType<CraftDestinationChanged>(Assert.Single(
            restored.Execute(new DispatchCraftToWaypoint(baseId, "INTERCEPTOR", 1, 0.3, 0.1)).Events));

        // Craft::getFuelLimit has undefined C++ behavior at zero speed; the port keeps this craft recallable.
        var elapsed = restored.Execute(new AdvanceCampaignTime(200));

        Assert.Equal(200, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
        var stuck = Interceptor(restored.Capture());
        Assert.Equal("STR_OUT", stuck.Status);
        Assert.Equal(0.2, stuck.Longitude);
        Assert.Equal(0.1, stuck.Latitude);
        Assert.Equal(100, stuck.Fuel);
        Assert.False(stuck.LowFuel);
        Assert.Equal(WorldTargetKind.Waypoint, stuck.Destination!.Kind);

        Assert.IsType<CraftDestinationChanged>(Assert.Single(
            restored.Execute(new RecallCraft(baseId, "INTERCEPTOR", 1)).Events));
        restored.Execute(new AdvanceCampaignTime(200));
        // The craft never left its base, so Craft::think lands it the moment it is sent home.
        var recalled = Interceptor(restored.Capture());
        Assert.Null(recalled.Destination);
        Assert.Equal("STR_READY", recalled.Status);
        Assert.Equal(0.2, recalled.Longitude);
        Assert.Empty(restored.Capture().World.Waypoints);
    }

    [Fact]
    public void PatrollingCraftDoNotAllocatePerTick()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var two = PatrolAllocations(2);
        var thirtyTwo = PatrolAllocations(32);
        Assert.True(thirtyTwo < two * 2,
            $"Two patrolling crafts allocated {two} bytes; thirty-two allocated {thirtyTwo} bytes.");

        long PatrolAllocations(int count)
        {
            var campaign = ReadyCampaign(content);
            var baseId = campaign.Capture().Bases[0].Id;
            campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.4, 0.1));
            campaign.Execute(new AdvanceCampaignTime(61));
            campaign.Execute(new PatrolCraft(baseId, "SHIP", 1));
            var snapshot = campaign.Capture();
            var first = Ship(snapshot);
            Assert.Null(first.Logistics!.Destination);
            var crafts = snapshot.Bases[0].Crafts.Concat(Enumerable.Range(2, count - 1)
                .Select(id => first with { Id = id })).ToArray();
            var restored = CampaignState.Restore(snapshot with
            {
                Bases = [snapshot.Bases[0] with { Crafts = crafts }],
            }, content, new SplitMix64RandomSource(38));
            restored.Execute(new AdvanceCampaignTime(1)); // warm the movement path
            var before = GC.GetAllocatedBytesForCurrentThread();
            var elapsed = restored.Execute(new AdvanceCampaignTime(10));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
            return allocated;
        }
    }

    /// <summary>The veteran SHIP carries a negative-capacity weapon; <paramref name="unarmed"/>
    /// strips it so crew assignment is about the pilot rule rather than craft capacity.</summary>
    private static CampaignState ReadyCampaign(RuntimeContent content, int fuel = 100, bool unarmed = false)
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
                Weapons = unarmed
                    ? Array.AsReadOnly(new CraftWeaponSnapshot?[craft.Logistics!.Weapons.Count])
                    : craft.Logistics!.Weapons,
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

    private static CampaignState ReadyInterceptor(RuntimeContent content)
    {
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var craft = Assert.Single(snapshot.Bases[0].Crafts, existing => existing.RuleId == "INTERCEPTOR");
        var ready = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_READY",
                Fuel = 100,
                Damage = 0,
                Longitude = 0.2,
                Latitude = 0.1,
            },
        };
        return CampaignState.Restore(snapshot with
        {
            Bases = [snapshot.Bases[0] with
            {
                Crafts = [.. snapshot.Bases[0].Crafts.Select(existing =>
                    existing.RuleId == "INTERCEPTOR" ? ready : existing)],
            }],
        }, content, new SplitMix64RandomSource(37));
    }

    private static void AssertTimeBlocked(CampaignState campaign, string reason)
    {
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(reason, Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static CraftLogisticsState Interceptor(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "INTERCEPTOR").Logistics!;

    private static CraftSnapshot Ship(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "SHIP");
}
