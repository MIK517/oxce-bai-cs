using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

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
        var atBoundary = saved.WithCraft("SHIP", _ => refuelling) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
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
    public void PatrolWithFuelFliesAtHalfSpeedAndBurnsFuelEveryTenMinutes()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = ReadyCampaign(content).Capture();
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            RuleId = "SHIP_SLOW_REFUEL",
            Logistics = craft.Logistics! with
            {
                Fuel = 300,
                Weapons = [null, null],
                Items = new Dictionary<string, int>(),
                Vehicles = [],
            },
        });
        var campaign = CampaignState.Restore(snapshot with { Bases = [snapshot.Bases[0] with { Soldiers = [] }] },
            content, new SplitMix64RandomSource(39));
        var rule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP_SLOW_REFUEL")].Value;
        Assert.False(rule.PatrolWithoutFuel);
        var baseId = snapshot.Bases[0].Id;
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(baseId, "SHIP_SLOW_REFUEL", 1, 0.4, 0.1)).Events));
        Assert.Equal(6000, Craft(campaign.Capture()).Speed);
        campaign.Execute(new AdvanceCampaignTime(61));

        // Craft::setDestination(0) halves speedMax; consumeFuel then burns floor(speed / 100).
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
            new PatrolCraft(baseId, "SHIP_SLOW_REFUEL", 1)).Events));
        var patrolling = Craft(campaign.Capture());
        Assert.Null(patrolling.Destination);
        Assert.Equal(3000, patrolling.Speed);

        // Exactly one ten-minute boundary falls inside any 120 consecutive ticks.
        AdvanceUnblocked(campaign, 120);
        var after = Craft(campaign.Capture());
        Assert.Equal("STR_OUT", after.Status);
        Assert.Equal(patrolling.Fuel - 30, after.Fuel);

        static CraftLogisticsState Craft(CampaignSnapshot snapshot) =>
            Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "SHIP_SLOW_REFUEL").Logistics!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftReturningHomeRejectsEveryFlightCommand(bool missionComplete)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = ReadyCampaign(content).Capture();
        var baseId = snapshot.Bases[0].Id;
        var campaign = CampaignState.Restore(snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                LowFuel = !missionComplete,
                MissionComplete = missionComplete,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
                Destination = new WorldTargetReference(WorldTargetKind.Base,
                    WorldTargetReference.BaseType, baseId, 0.2, 0.1),
            },
        }), content, new SplitMix64RandomSource(41));
        var before = campaign.Capture();

        // InterceptState and GeoscapeCraftState offer no command for a craft heading home.
        foreach (ICampaignCommand command in new ICampaignCommand[]
        {
            new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.4, 0.1),
            new PatrolCraft(baseId, "SHIP", 1),
            new RecallCraft(baseId, "SHIP", 1),
        })
        {
            Assert.Equal("The craft is returning to base.",
                Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Reason);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData("STR_OUT")]
    [InlineData("STR_REPAIRS")]
    public void DestroyedCraftStopsTimeBeforeItsRemoval(string status)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = ReadyCampaign(content).Capture();
        var rule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value;
        Assert.Equal(100, rule.DamageMaximum);
        var campaign = CampaignState.Restore(snapshot.WithCraft("SHIP", craft => craft with
        {
            // Every veteran SHIP weapon is removed so the rule's damageMax is the whole limit.
            Logistics = craft.Logistics! with { Status = status, Damage = 100, Weapons = [null, null] },
        }), content, new SplitMix64RandomSource(42));

        AssertTimeBlocked(campaign, "Destroyed craft removal requires world simulation.");

        var damaged = CampaignState.Restore(snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = status, Damage = 99, Weapons = [null, null] },
        }), content, new SplitMix64RandomSource(43));
        Assert.DoesNotContain(damaged.Execute(new AdvanceCampaignTime(1)).Events,
            item => item is CampaignActionBlocked);
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
        var restored = CampaignState.Restore(snapshot.WithCraft("SHIP", _ => flagged) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        }, content, new SplitMix64RandomSource(33));

        AdvanceOne(restored);

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
        var restored = CampaignState.Restore(snapshot.WithCraft("SHIP", _ => following), content,
            new SplitMix64RandomSource(34));

        AssertTimeBlocked(restored, "Craft pursuit and landing require world simulation.");
    }

    [Fact]
    public void AirborneCraftAtUnplacedBaseReportsItsPreflightReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var craft = Ship(snapshot);
        var airborne = snapshot.WithCraft("SHIP", _ => craft with { Logistics = craft.Logistics! with { Status = "STR_OUT" } });
        var unplaced = CampaignState.Restore(airborne with { Bases = [airborne.Bases[0] with { Name = "" }] },
            content, new SplitMix64RandomSource(49));

        AssertTimeBlocked(unplaced, "An airborne craft belongs to a base that is not placed.");
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
        var restored = CampaignState.Restore(snapshot.WithCraft("SHIP", _ => craft with { Logistics = logistics }),
            content, new SplitMix64RandomSource(60));

        AssertTimeBlocked(restored, reason);
    }

    [Fact]
    public void CraftSpeedOverflowReportsItsPreflightReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-speed-range.rul");
        var campaign = ReadyCampaign(content);
        var snapshot = campaign.Capture();
        var craft = Ship(snapshot);
        var restored = CampaignState.Restore(snapshot.WithCraft("SHIP", _ => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_OUT", Weapons = [new CraftWeaponSnapshot("FIXED", 0), null] },
        }), content, new SplitMix64RandomSource(61));

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
            var restored = CampaignState.Restore(snapshot.WithCraft("SHIP", _ => ship with
            {
                Logistics = ship.Logistics! with
                {
                    Items = items,
                    Weapons = [new CraftWeaponSnapshot("FIXED", 0), null],
                },
            }), content, new SplitMix64RandomSource(50));
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
    public void FlightAllocationPerAdditionalCraftIsBounded()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var four = FlightAllocations(4);
        var sixteen = FlightAllocations(16);
        // Fixed per-command costs cancel. A moving craft replaces its immutable state
        // once per tick; anything beyond that is a per-craft inner-loop allocation.
        var perCraftTick = (sixteen - four) / (12 * 10.0);
        Assert.True(perCraftTick <= MaximumFlightBytesPerCraftTick,
            $"{perCraftTick:F1} B per additional craft and tick ({four} B for 4 crafts, {sixteen} B for 16).");

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
            CampaignCommandResult? elapsed = null;
            var allocated = AllocatedBytes(() => elapsed = restored.Execute(new AdvanceCampaignTime(10)));
            Assert.Equal(10, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed!.Events)).Summary.TickCount);
            Assert.All(restored.Capture().Bases[0].Crafts.Where(craft => craft.RuleId == "SHIP"),
                craft => Assert.NotEqual(0.2, craft.Logistics!.Longitude));
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
        // Fixed per-command costs cancel; a stationary patrol must not allocate per craft.
        Assert.True(thirtyTwo - two <= 0,
            $"Two patrolling crafts allocated {two} B over ten ticks; thirty-two allocated {thirtyTwo} B.");

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
            CampaignCommandResult? elapsed = null;
            var allocated = AllocatedBytes(() => elapsed = restored.Execute(new AdvanceCampaignTime(10)));
            Assert.Equal(10, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed!.Events)).Summary.TickCount);
            return allocated;
        }
    }

    // One CraftSnapshot and one CraftLogisticsState record per moving craft and tick (208 B measured).
    private const double MaximumFlightBytesPerCraftTick = 256;

    /// <summary>The veteran SHIP carries a negative-capacity weapon; <paramref name="unarmed"/>
    /// strips it so crew assignment is about the pilot rule rather than craft capacity.</summary>
    private static CampaignState ReadyCampaign(RuntimeContent content, int fuel = 100, bool unarmed = false)
    {
        var snapshot = CreateSnapshot(content, "World operations");
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
        return CampaignState.Restore(snapshot.WithCraft("SHIP", _ => ready), content,
            new SplitMix64RandomSource(19));
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
        return CampaignState.Restore(snapshot.WithCraft("INTERCEPTOR", _ => ready), content,
            new SplitMix64RandomSource(37));
    }

    private static CraftLogisticsState Interceptor(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "INTERCEPTOR").Logistics!;

    private static CraftSnapshot Ship(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "SHIP");
}
