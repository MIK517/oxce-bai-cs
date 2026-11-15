using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleDamageTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S8, the launch: <c>BattlescapeGame::launchAction</c> and <c>ProjectileFlyBState</c>'s waypoint cascade (<c>_range</c>, the next leg from the waypoint it reached,
/// <c>targetFloor</c>, the ammunition spent at the last impact), on the flat arena of the damage tests. The soldier holds a launcher (waypoints 3) with a rocket of power 40.
/// </summary>
public sealed class BattleLaunchTests
{
    private const string Launcher = "costAimed: {time: 30}\naccuracyAimed: 100\nwaypoints: 3";
    private const string Rocket = "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}";

    internal static (Arena Arena, BattleItem Gun, BattleItem Clip) Load(string gun = Launcher)
    {
        var arena = Duel(gun: gun, clip: Rocket);
        arena.Stand(arena.Alien, 15, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Firing = 100 };
        arena.Soldier.TimeUnits = 100;
        var armed = Shot(arena);
        armed.Clip.AmmoQuantity = 3;
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0);
        return (arena, armed.Gun, armed.Clip);
    }

    private static CampaignReason? Launch(Arena arena, BattleItem gun, params BattlePosition[] waypoints) =>
        arena.Battle.LaunchAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, waypoints);

    private static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    [Fact]
    public void ALaunchToOneWaypointPaysOnceSpendsTheRocketAtTheImpactAndBlastsThere()
    {
        var (arena, gun, clip) = Load();
        Assert.Null(Launch(arena, gun, At(15, 10)));
        var events = arena.Battle.Events.Drain();
        var fired = Assert.Single(events.OfType<UnitFired>());
        Assert.Equal((BattleAimKind.Launch, clip.Id), (fired.Kind, fired.AmmoItemId));
        Assert.Contains(events, static @event => @event is BattleExploded);
        Assert.Equal((70, 2), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.True(arena.Alien.Health < 100);
        // A launch is no shot: the statistics of the guns do not count it.
        Assert.Equal(0, arena.Soldier.Statistics.ShotsFiredCounter);
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
    }

    [Fact]
    public void AMissileFliesFromWaypointToWaypointForOneCostAndBlastsAtTheLastOne()
    {
        var (arena, gun, clip) = Load();
        Assert.Null(Launch(arena, gun, At(12, 10), At(15, 10)));
        var events = arena.Battle.Events.Drain();
        // Two legs (each raises its flight), a single payment and a single rocket, and one blast, at the end.
        Assert.Equal(2, events.OfType<UnitFired>().Count());
        var blast = Assert.Single(events.OfType<BattleExploded>());
        Assert.Equal((15, 10), (blast.Center.X / 16, blast.Center.Y / 16));
        Assert.Equal((70, 2), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.True(arena.Alien.Health < 100);
        // The first leg's flight ends at the first waypoint, in the air: no impact.
        Assert.Equal((int)BattleVoxelType.Empty, events.OfType<UnitFired>().First().Impact);
    }

    [Fact]
    public void TwoWaypointsOnOneTileSendTheLastLegToTheFloorOfThatTile()
    {
        var (arena, gun, clip) = Load();
        Assert.Null(Launch(arena, gun, At(12, 10), At(12, 10)));
        var events = arena.Battle.Events.Drain();
        Assert.Equal(2, events.OfType<UnitFired>().Count());
        // The first leg ends in the air at the waypoint (4 above the middle of the tile); the second leg starts there and aims at the floor, 10 below the middle.
        var legs = events.OfType<UnitFired>().ToList();
        Assert.Equal((12, 10, 16), (legs[0].Trajectory[^1].X / 16, legs[0].Trajectory[^1].Y / 16, legs[0].Trajectory[^1].Z));
        Assert.Equal((12, 10), (legs[1].Trajectory[0].X / 16, legs[1].Trajectory[0].Y / 16));
        Assert.True(legs[1].Trajectory[^1].Z < 12);
        // (This arena's floor has no voxels: the missile leaves the map, so it does not blast, and the rocket is spent all the same.)
        Assert.DoesNotContain(events, static @event => @event is BattleExploded);
        Assert.Equal(2, clip.AmmoQuantity);
    }

    [Fact]
    public void TheLastLegToTheFloorBlastsOnTheFloorTwoStepsBackAndSpendsTheRocket()
    {
        var (arena, gun, clip) = Load();
        // A floor with voxels (its lower layers are solid): the last leg, aimed at the floor of the waypoint's tile, hits it.
        arena.Put(12, 10, 0, BattleTilePart.Floor, LowWall);
        Assert.Null(Launch(arena, gun, At(12, 10), At(12, 10)));
        var events = arena.Battle.Events.Drain();
        var legs = events.OfType<UnitFired>().ToList();
        Assert.Equal(2, legs.Count);
        Assert.Equal((int)BattleVoxelType.Floor, legs[1].Impact);
        var blast = Assert.Single(events.OfType<BattleExploded>());
        // The blast is two steps back along the flight, on that tile and above the floor's top voxels (the target is 10 below the middle of the tile).
        Assert.Equal((12, 10), (blast.Center.X / 16, blast.Center.Y / 16));
        Assert.Equal(BattleSight.PositionFromEnd(legs[1].Trajectory, -2), blast.Center);
        Assert.True(blast.Center.Z is > 2 and < 16, $"{blast.Center}");
        Assert.Equal((70, 2), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
    }

    [Fact]
    public void ALegThatHitsSomethingBeforeItsWaypointBlastsThereAndDropsTheRestOfTheWaypoints()
    {
        var (arena, gun, clip) = Load();
        // A block in the way of the first leg to (14, 10).
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        Assert.Null(Launch(arena, gun, At(14, 10), At(15, 10)));
        var events = arena.Battle.Events.Drain();
        var fired = Assert.Single(events.OfType<UnitFired>());
        Assert.NotEqual((int)BattleVoxelType.Empty, fired.Impact);
        var blast = Assert.Single(events.OfType<BattleExploded>());
        Assert.True(blast.Center.X / 16 <= 12, $"{blast.Center}");
        Assert.Equal((70, 2), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
    }

    [Fact]
    public void ALaterWaypointOutOfTheWeaponsRangeFromTheUnitStopsTheMissileWithNoRocketSpent()
    {
        var (arena, gun, clip) = Load(gun: Launcher + "\nmaxRange: 8");
        Assert.Null(Launch(arena, gun, At(12, 10), At(25, 10)));
        var events = arena.Battle.Events.Drain();
        // The first leg flew (and paid); the second is out of range from the unit, so the action stops and nothing explodes.
        Assert.Single(events.OfType<UnitFired>());
        Assert.Equal("STR_OUT_OF_RANGE", Assert.Single(events.OfType<UnitAttackStopped>()).Reason);
        Assert.DoesNotContain(events, static @event => @event is BattleExploded);
        Assert.Equal((70, 3), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
    }

    [Fact]
    public void AHostileUnitWithAnExplosiveShotDoesNotAnswerALoneLauncherAtTheEndOfTheLaunch()
    {
        // (The alien's gun takes the launcher's rule: it needs a snap shot cost for the alien to react with it.)
        var (arena, gun, _) = Load(gun: Launcher + "\ncostSnap: {time: 20}");
        Assert.NotNull(Shot(arena, arena.Alien).Gun);
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Reactions = 100 };
        arena.Alien.TimeUnits = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 1 };
        Assert.Null(Launch(arena, gun, At(12, 10), At(15, 10)));
        // The alien sees the launch (the check ran, and it is a reactor), but tryReaction: its shot is an explosive one, and explosiveEfficacy for a lone
        // target of a healthy hostile unit is 0, so no reaction state is pushed.
        var fired = arena.Battle.Events.Drain().OfType<UnitFired>().ToList();
        Assert.Equal([arena.Soldier.Id, arena.Soldier.Id], fired.Select(static shot => shot.UnitId).ToArray());
        Assert.Equal(100, arena.Alien.TimeUnits);
        Assert.Contains(arena.Soldier.Id, arena.Alien.VisibleUnits);
    }

    [Fact]
    public void AnArcingLauncherFliesTheArcAndSpendsNothingUntilTheLastImpact()
    {
        var (arena, gun, clip) = Load(gun: Launcher + "\narcingShot: true");
        Assert.Null(Launch(arena, gun, At(15, 10)));
        var events = arena.Battle.Events.Drain();
        var fired = Assert.Single(events.OfType<UnitFired>());
        Assert.True(fired.Trajectory.Max(static voxel => voxel.Z) > fired.Trajectory[0].Z);
        Assert.Contains(events, static @event => @event is BattleExploded);
        Assert.Equal((70, 2), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
    }

    [Fact]
    public void ALaterLegStartsFromTheMiddleOfTheWaypointsTileTwoThirdsUpWhateverTheDeviationOfTheLegBefore()
    {
        var (arena, gun, _) = Load();
        arena.Soldier.Stats = arena.Soldier.Stats with { Firing = 20 };
        // A launch's accuracy is 60 whatever the shooter's: the draws put the first leg's target off by a few voxels.
        arena.Battle.Random = new ScriptedDraws(100, 8, 3, 5);
        Assert.Null(Launch(arena, gun, At(12, 10), At(13, 10)));
        var legs = arena.Battle.Events.Drain().OfType<UnitFired>().ToList();
        Assert.Equal(2, legs.Count);
        // The first leg's flight is off its waypoint (it deviates), the second starts at the waypoint's own voxel: x, y in the middle of the tile, z 16.
        var expected = new BattlePosition(12 * 16 + 8, 10 * 16 + 8, 16);
        Assert.NotEqual(expected, legs[0].Trajectory[^1]);
        Assert.Equal(expected, legs[1].Trajectory[0]);
    }

    [Fact]
    public void ALaunchTurnsTheUnitToTheFirstWaypointAndPaysForTheTurn()
    {
        var (arena, gun, _) = Load();
        Assert.Null(Launch(arena, gun, At(10, 14)));
        var events = arena.Battle.Events.Drain();
        var turned = Assert.IsType<UnitTurned>(events[0]);
        Assert.Equal(arena.Soldier.Id, turned.UnitId);
        Assert.Equal(70 - turned.TimeUnitsSpent, arena.Soldier.TimeUnits);
    }

    [Fact]
    public void TheLaunchRefusalsAreTheMenusAndTheWaypointLimits()
    {
        var (arena, gun, clip) = Load();
        // More waypoints than the launcher allows, none at all, one off the map.
        Assert.Equal("battle.attack-waypoints-invalid", Launch(arena, gun, At(11, 10), At(12, 10), At(13, 10), At(14, 10))?.Code);
        Assert.Equal("battle.attack-waypoints-invalid", Launch(arena, gun)?.Code);
        Assert.Equal("battle.attack-target-outside", Launch(arena, gun, At(99, 10))?.Code);
        // A weapon without waypoints offers no launch; an unknown item is not held.
        var (plain, rifle, _) = Load(gun: "costAimed: {time: 30}\naccuracyAimed: 100");
        Assert.Equal("battle.attack-action-not-offered", Launch(plain, rifle, At(15, 10))?.Code);
        Assert.Equal("battle.attack-weapon-unavailable", arena.Battle.LaunchAction(BattleResearchGate.Everything, arena.Soldier.Id, 9999, [At(15, 10)])?.Code);
        // Out of time units: nothing starts (the turn is not needed, so nothing changed).
        arena.Soldier.TimeUnits = 5;
        Assert.Equal("battle.attack-refused", Launch(arena, gun, At(15, 10))?.Code);
        Assert.Equal(3, clip.AmmoQuantity);
    }

    [Fact]
    public void ALaunchTheUnitCannotPayOrLoadIsRefusedBeforeItTurnsForTheFirstWaypoint()
    {
        // The action menu checks the time units and the ammunition before the targeting starts: a unit that would have to turn does not turn, and pays nothing.
        var (arena, gun, clip) = Load();
        var direction = arena.Soldier.Direction;
        arena.Soldier.TimeUnits = 5;
        Assert.Equal("battle.attack-refused", Launch(arena, gun, At(10, 14))?.Code);
        Assert.Equal((direction, 5), (arena.Soldier.Direction, arena.Soldier.TimeUnits));
        Assert.Empty(arena.Battle.Events.Drain());

        arena.Soldier.TimeUnits = 100;
        clip.AmmoQuantity = 0;
        Assert.Equal("battle.attack-refused", Launch(arena, gun, At(10, 14))?.Code);
        Assert.Equal((direction, 100), (arena.Soldier.Direction, arena.Soldier.TimeUnits));
        Assert.Empty(arena.Battle.Events.Drain());
    }

    [Fact]
    public void AnEmptyLauncherRefusesTheLaunchAndPaysNothing()
    {
        var (arena, gun, clip) = Load();
        clip.AmmoQuantity = 0;
        Assert.Equal("battle.attack-refused", Launch(arena, gun, At(15, 10))?.Code);
        Assert.Equal(100, arena.Soldier.TimeUnits);
    }

    [Fact]
    public void ALauncherWhoseAmmunitionAllowsMoreWaypointsTakesThemUnlimited()
    {
        // waypoints -1 on the weapon: any number of waypoints.
        var (arena, gun, _) = Load(gun: "costAimed: {time: 30}\naccuracyAimed: 100\nwaypoints: -1");
        Assert.Null(Launch(arena, gun, At(11, 12), At(12, 12), At(13, 12), At(14, 11), At(15, 10)));
        Assert.Equal(5, arena.Battle.Events.Drain().OfType<UnitFired>().Count());
    }

    [Fact]
    public void TheLaunchCommandRunsThroughTheRuntimeAndATrialCopyKeepsTheBattleOnRefusal()
    {
        var (arena, gun, clip) = Load();
        var runtime = new BattleRuntime(arena.Battle, arena.Rules, new ScriptedDraws(0), null);
        var result = runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.Launch, Waypoints: [At(12, 10), At(15, 10)]));
        Assert.Equal(2, result.Events.OfType<UnitFired>().Count());
        Assert.Contains(result.Events, static @event => @event is BattleExploded);
        Assert.Equal((70, 2), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        var revision = runtime.Revision;
        Assert.Equal("battle.attack-waypoints-invalid", Refusal(runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.Launch))));
        Assert.Equal(revision, runtime.Revision);
        Assert.Null(runtime.Fault);
    }
}
