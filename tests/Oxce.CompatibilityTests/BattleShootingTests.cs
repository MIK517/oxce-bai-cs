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
/// Phase 7 branch 5 S7: shooting (<c>BattlescapeGame::primaryAction</c>'s targeting part, <c>UnitTurnBState</c>'s targeting form, <c>ProjectileFlyBState</c> for the aimed,
/// snap and auto shots, close quarters combat, the shotgun's pellets, the statistics of a shot) and the attack command, on the flat arena of the damage tests. The
/// soldier (facing east, a firing statistic of 100) holds a GUN with a CLIP of 12 rounds of power 10 as a flat roll; the alien (100 health, facing west) stands five tiles
/// east. A <see cref="ScriptedDraws"/> source orders the draws: with every draw 0 a shot lands where it is aimed.
/// </summary>
public sealed class BattleShootingTests
{
    private const string Rifle = "costAimed: {time: 30}\ncostSnap: {time: 20}\ncostAuto: {time: 40}\naccuracyAimed: 100\naccuracySnap: 100\naccuracyAuto: 100";
    private const string Bullet = "power: 10\ndamageType: 1\ndamageAlter: {RandomType: 3}";

    internal static (Arena Arena, BattleItem Gun, BattleItem Clip) Load(string gun = Rifle, string clip = Bullet, string alienExtra = "", string global = "",
        int distance = 5, Func<string, string>? more = null)
    {
        var arena = Duel(gun: gun, clip: clip, global: global, alienExtra: alienExtra, more: more);
        arena.Stand(arena.Alien, 10 + distance, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Firing = 100 };
        arena.Soldier.TimeUnits = 100;
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 60, Stamina = 60 };
        arena.Alien.TimeUnits = 60;
        arena.Alien.Energy = 60;
        var armed = Shot(arena);
        armed.Clip.AmmoQuantity = 12;
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0);
        return (arena, armed.Gun, armed.Clip);
    }

    private static CampaignReason? Fire(Arena arena, BattleItem gun, BattleAimKind kind = BattleAimKind.AimedShot, int x = 15, int y = 10, int z = 0, bool force = false,
        BattleResearchGate? research = null) =>
        arena.Battle.ShootAction(research ?? BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, kind, new BattlePosition(x, y, z), force);

    private static readonly int[] Wild = [100, .. Enumerable.Repeat(999, 60)];

    // ---- a shot ----

    [Fact]
    public void AnAimedShotPaysTheCostSpendsARoundHitsTheUnitAndCountsItInTheStatistics()
    {
        var (arena, gun, clip) = Load();
        Assert.Null(Fire(arena, gun));
        Assert.Equal(70, arena.Soldier.TimeUnits);
        Assert.Equal(11, clip.AmmoQuantity);
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
        var events = arena.Battle.Events.Drain();
        var fired = Assert.IsType<UnitFired>(events[0]);
        Assert.Equal((arena.Soldier.Id, gun.Id, clip.Id, BattleAimKind.AimedShot, 4), (fired.UnitId, fired.ItemId, fired.AmmoItemId, fired.Kind, fired.Impact));
        Assert.NotEmpty(fired.Trajectory);
        var hit = Assert.IsType<UnitHit>(events[1]);
        Assert.Equal((arena.Alien.Id, arena.Soldier.Id), (hit.UnitId, hit.AttackerId));
        Assert.True(arena.Alien.Health < 100);
        Assert.Equal(2, events.Count);
        Assert.Equal(1, arena.Soldier.Statistics.ShotsFiredCounter);
        Assert.Equal(1, arena.Soldier.Statistics.ShotsLandedCounter);
        Assert.Equal(1, arena.Alien.Statistics.ShotAtCounter);
        Assert.Equal(1, arena.Alien.Statistics.HitCounter);
        // The hit unit's tile becomes known to the enemies, and the shooter's own too.
        Assert.Equal(arena.Tile(15, 10), arena.Alien.TileLastSpottedBy[(int)BattleFaction.Hostile]);
        Assert.Equal(arena.Tile(10, 10), arena.Soldier.TileLastSpottedBy[(int)BattleFaction.Hostile]);
    }

    [Fact]
    public void AShotThatMissesFliesOnAndHurtsNobody()
    {
        var (arena, gun, clip) = Load(gun: Rifle.Replace("accuracyAimed: 100", "accuracyAimed: 10", StringComparison.Ordinal));
        arena.Battle.Random = new ScriptedDraws(Wild);
        Assert.Null(Fire(arena, gun));
        var events = arena.Battle.Events.Drain();
        var fired = Assert.IsType<UnitFired>(events[0]);
        Assert.NotEqual(4, fired.Impact);
        Assert.DoesNotContain(events, static @event => @event is UnitHit);
        Assert.Equal((100, 70, 11), (arena.Alien.Health, arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.Equal(0, arena.Soldier.Statistics.ShotsLandedCounter);
        Assert.Equal(1, arena.Alien.Statistics.ShotAtCounter);
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
    }

    [Fact]
    public void ASnapShotUsesItsOwnCostAndAnAutoShotFiresTheBurstOnOneCost()
    {
        var (arena, gun, clip) = Load();
        Assert.Null(Fire(arena, gun, BattleAimKind.SnapShot));
        Assert.Equal((80, 11), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.Single(arena.Battle.Events.Drain().OfType<UnitFired>());

        var (burst, autoGun, autoClip) = Load();
        Assert.Null(Fire(burst, autoGun, BattleAimKind.AutoShot));
        // One payment, three rounds, and each shot is followed by its hit.
        Assert.Equal((60, 9, 3), (burst.Soldier.TimeUnits, autoClip.AmmoQuantity, burst.Soldier.Statistics.ShotsFiredCounter));
        var events = burst.Battle.Events.Drain();
        Assert.Equal([typeof(UnitFired), typeof(UnitHit), typeof(UnitFired), typeof(UnitHit), typeof(UnitFired), typeof(UnitHit)], events.Select(static e => e.GetType()));
        Assert.Equal(3, burst.Alien.Statistics.ShotAtCounter);
        Assert.Equal(BattleUnitStatus.Standing, burst.Soldier.Status);
    }

    [Fact]
    public void ABurstStopsWhenTheClipRunsOutAndTheSpentClipLeavesTheWeapon()
    {
        var (arena, gun, clip) = Load();
        clip.AmmoQuantity = 2;
        Assert.Null(Fire(arena, gun, BattleAimKind.AutoShot));
        Assert.Equal(2, arena.Battle.Events.Drain().OfType<UnitFired>().Count());
        Assert.Null(gun.AmmoSlots[0]);
        Assert.DoesNotContain(clip, arena.Battle.Items);
        Assert.False(clip.IsAmmo);
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
    }

    [Fact]
    public void ABurstGivesTheShotsOfTheWeaponsConfigAndAnItemWithoutClipSizeNeverRunsDry()
    {
        var (arena, gun, clip) = Load(gun: Rifle + "\nconfAuto: {shots: 2}", more: static rules => Replaced(rules, "clipSize: 12", "clipSize: -1"));
        Assert.Null(Fire(arena, gun, BattleAimKind.AutoShot));
        Assert.Equal(2, arena.Battle.Events.Drain().OfType<UnitFired>().Count());
        Assert.Equal(255, arena.Battle.AmmoQuantityOf(clip));
        Assert.Same(clip, arena.Battle.Items.First(item => item.Id == clip.Id));
    }

    [Fact]
    public void ExplosiveAmmunitionBlastsAtTheImpact()
    {
        var (arena, gun, _) = Load(clip: "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}");
        Assert.Null(Fire(arena, gun));
        var events = arena.Battle.Events.Drain();
        Assert.IsType<UnitFired>(events[0]);
        var blast = Assert.Single(events.OfType<BattleExploded>());
        Assert.False(blast.Terrain);
        Assert.True(arena.Alien.Health < 100);
    }

    [Fact]
    public void AnArcingShotFliesTheArcAndPaysAndSpendsLikeAnyShot()
    {
        var (arena, gun, clip) = Load(gun: Rifle + "\narcingShot: true");
        Assert.Null(Fire(arena, gun));
        var events = arena.Battle.Events.Drain();
        var fired = Assert.IsType<UnitFired>(events[0]);
        Assert.NotEmpty(fired.Trajectory);
        // The arc climbs above the line a straight shot flies.
        Assert.True(fired.Trajectory.Max(static voxel => voxel.Z) > fired.Trajectory[0].Z);
        Assert.Equal((70, 11), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.Equal(1, arena.Soldier.Statistics.ShotsFiredCounter);
    }

    // ---- turning ----

    [Fact]
    public void TheUnitTurnsToTheTargetPayingEachEighthThenFires()
    {
        var (arena, gun, _) = Load();
        arena.Stand(arena.Alien, 15, 5, 6);
        arena.See();
        var turn = arena.Soldier.TurnCost(arena.Rules);
        Assert.Null(Fire(arena, gun, x: 15, y: 5));
        Assert.Equal(1, arena.Soldier.Direction);
        Assert.Equal(100 - turn - 30, arena.Soldier.TimeUnits);
        var events = arena.Battle.Events.Drain();
        Assert.Equal(new UnitTurned(arena.Soldier.Id, 1, 1, turn, BattleTurnStop.Arrived), events[0]);
        Assert.IsType<UnitFired>(events[1]);
        Assert.Contains(events, static @event => @event is UnitHit);
    }

    [Fact]
    public void ATurnTheUnitCannotFinishStopsTheActionWithItsReasonAsAnEvent()
    {
        var (arena, gun, clip) = Load();
        arena.Stand(arena.Alien, 10, 5, 6);
        arena.See();
        var turn = arena.Soldier.TurnCost(arena.Rules);
        arena.Soldier.TimeUnits = turn;
        // North of an east-facing unit is two eighths: the first is paid, the second is not, and the shot has nothing to pay with.
        Assert.Null(Fire(arena, gun, x: 10, y: 5));
        var events = arena.Battle.Events.Drain();
        // The paid eighth is reported, then the stop.
        Assert.Equal(new UnitTurned(arena.Soldier.Id, 1, 1, turn, BattleTurnStop.NotEnoughTimeUnits), events[0]);
        Assert.Equal(new UnitAttackStopped(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, "STR_NOT_ENOUGH_TIME_UNITS"), events[1]);
        Assert.Equal(2, events.Count);
        Assert.Equal((0, 1, BattleUnitStatus.Standing, 12), (arena.Soldier.TimeUnits, arena.Soldier.Direction, arena.Soldier.Status, clip.AmmoQuantity));
        Assert.Equal(100, arena.Alien.Health);
    }

    // ---- checks that fail before anything changed ----

    [Fact]
    public void AShotTheUnitCannotPayForIsRefusedWhenItDidNotTurn()
    {
        var (arena, gun, clip) = Load();
        arena.Soldier.TimeUnits = 29;
        var refusal = Fire(arena, gun);
        Assert.Equal(("battle.attack-refused", "STR_NOT_ENOUGH_TIME_UNITS"), (refusal!.Code, refusal.TextKey));
        Assert.Equal((29, 12, 100), (arena.Soldier.TimeUnits, clip.AmmoQuantity, arena.Alien.Health));
        Assert.Empty(arena.Battle.Events.Drain());
    }

    [Fact]
    public void AWeaponWithoutAmmunitionOrBeyondItsRangeOrBehindAWallIsRefused()
    {
        var (arena, gun, clip) = Load();
        gun.AmmoSlots[0] = null;
        Assert.Equal("STR_NO_AMMUNITION_LOADED", Fire(arena, gun)!.TextKey);
        Assert.Equal((100, 12), (arena.Soldier.TimeUnits, clip.AmmoQuantity));

        var (far, farGun, _) = Load(gun: Rifle + "\nmaxRange: 3");
        Assert.Equal("STR_OUT_OF_RANGE", Fire(far, farGun)!.TextKey);
        Assert.Equal(100, far.Soldier.TimeUnits);
        // A range of 3 reaches a target three tiles away.
        Assert.Null(Fire(far, farGun, x: 13));

        var (walled, walledGun, _) = Load();
        walled.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        walled.See();
        Assert.Equal("STR_NO_LINE_OF_FIRE", Fire(walled, walledGun)!.TextKey);
        Assert.Equal((100, 100), (walled.Soldier.TimeUnits, walled.Alien.Health));
    }

    [Fact]
    public void ATargetOffTheMapAWeaponTheUnitDoesNotHoldAShotTheWeaponDoesNotOfferAndAnUnusableWeaponAreRefused()
    {
        var (arena, gun, _) = Load();
        Assert.Equal("battle.attack-target-outside", Fire(arena, gun, x: 500)!.Code);
        Assert.Equal("battle.attack-weapon-unavailable", arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, 9999, BattleAimKind.AimedShot, new BattlePosition(15, 10, 0), false)!.Code);
        Assert.Equal("battle.attack-action-not-offered", Fire(arena, gun, BattleAimKind.Throw)!.Code);
        var (plain, plainGun, _) = Load(gun: "costAimed: {time: 30}");
        Assert.Equal("battle.attack-action-not-offered", Fire(plain, plainGun, BattleAimKind.SnapShot)!.Code);
        Assert.Null(Fire(plain, plainGun));

        var (locked, lockedGun, _) = Load(gun: Rifle + "\nrequires: [STR_ALIEN_ALLOYS]", more: static rules => rules + "\nresearch:\n  - name: STR_ALIEN_ALLOYS\n    cost: 1\n");
        var reason = Fire(locked, lockedGun, research: new BattleResearchGate(static _ => false, false));
        Assert.Equal(("battle.attack-refused", "STR_UNABLE_TO_USE_ALIEN_ARTIFACT_UNTIL_RESEARCHED"), (reason!.Code, reason.TextKey));
        Assert.Equal(100, locked.Soldier.TimeUnits);
    }

    [Fact]
    public void AUnitThatIsNotOnTurnOrHasNoTileCannotShoot()
    {
        var (arena, gun, _) = Load();
        arena.Battle.Side = BattleFaction.Hostile;
        var offTurn = Fire(arena, gun);
        Assert.Equal(arena.Battle.SelectionUnit(arena.Soldier.Id, out _)!.Code, offTurn!.Code);
        Assert.Equal((100, 100), (arena.Soldier.TimeUnits, arena.Alien.Health));

        var (placed, placedGun, _) = Load();
        placed.Soldier.Position = null;
        Assert.Equal(BattleSelectionReasons.UnitUnknown.Code, Fire(placed, placedGun)!.Code);
        Assert.Equal(100, placed.Alien.Health);
    }

    // ---- aiming at things that are not a seen unit ----

    [Fact]
    public void ForcedFireAimsAtTheMiddleOfTheTileWhateverIsThere()
    {
        var (arena, gun, _) = Load();
        Assert.Null(Fire(arena, gun, x: 13, force: true));
        var fired = Assert.IsType<UnitFired>(arena.Battle.Events.Drain()[0]);
        // The line passes the middle of the tile (8, 8, 12) it was aimed at.
        Assert.Contains(fired.Trajectory, static voxel => voxel == new BattlePosition(13 * 16 + 8, 10 * 16 + 8, 12));
        Assert.Equal(70, arena.Soldier.TimeUnits);
    }

    [Fact]
    public void AShotAtTheUnitsOwnTileGoesToTheFloorUnderIt()
    {
        var (arena, gun, _) = Load();
        Assert.Null(Fire(arena, gun, x: 10));
        var fired = Assert.IsType<UnitFired>(arena.Battle.Events.Drain()[0]);
        Assert.NotEqual(4, fired.Impact);
        Assert.Equal(100, arena.Soldier.Health);
        // The shooter's own tile is not a shot at it.
        Assert.Equal(0, arena.Soldier.Statistics.ShotAtCounter);
    }

    // ---- the shotgun ----

    [Fact]
    public void ShotgunPelletsEachHitAndTheExperienceTheyAddIsCutToOne()
    {
        var (single, singleGun, _) = Load();
        Fire(single, singleGun);
        var one = 100 - single.Alien.Health;
        var firing = single.Soldier.Experience.Firing;
        Assert.True(one > 0);

        // The second behavior aims every pellet at the first one's impact with the choke and the spread alone (a spread of 0 is a tight cluster).
        var (arena, gun, _) = Load(clip: Bullet + "\nshotgunPellets: 4\nshotgunBehavior: 1\nshotgunSpread: 0");
        Assert.Null(Fire(arena, gun));
        Assert.Equal(one * 4, 100 - arena.Alien.Health);
        Assert.Equal(4, arena.Alien.Statistics.HitCounter);
        // One shot, one round, and the pellets' experience is nerfed to one more than the first hit's.
        Assert.Equal((1, 11), (arena.Soldier.Statistics.ShotsFiredCounter, arena.Battle.Items.First(item => item.Id == gun.AmmoSlots[0]).AmmoQuantity));
        Assert.True(arena.Soldier.Experience.Firing <= firing + 1);

        // The vanilla formula takes five accuracy points off per pellet, so with every draw 0 the later pellets all miss to one side.
        var (wide, wideGun, _) = Load(clip: Bullet + "\nshotgunPellets: 4");
        Assert.Null(Fire(wide, wideGun));
        Assert.Equal((one, 1), (100 - wide.Alien.Health, wide.Alien.Statistics.HitCounter));
    }

    [Fact]
    public void TheExperienceNerfKeepsEachStatisticToOneOverTheRememberedValue()
    {
        var (arena, _, _) = Load();
        var unit = arena.Soldier;
        unit.Experience = new BattleUnitStats(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        unit.RememberExperience();
        unit.Experience = new BattleUnitStats(1, 5, 3, 9, 5, 6, 7, 20, 9, 10, 11, 12);
        unit.NerfExperience();
        Assert.Equal(new BattleUnitStats(1, 3, 3, 5, 5, 6, 7, 9, 9, 10, 11, 12), unit.Experience);
    }

    // ---- close quarters combat ----

    private const string CloseQuarters = "\nenableCloseQuartersCombat: 1\ncloseQuartersTuCostGlobal: 12\ncloseQuartersEnergyCostGlobal: 8\n";

    [Fact]
    public void ANeighbourWhoCanStrikeMakesTheShooterRollAndAFailedRollSpendsItsTimeAndTurnsTheShot()
    {
        // The roll is a draw of 99: it fails, the next draw (0 to 5) is 3, a turn of 7 eighths to the left of the facing: the shooter (facing east) looks north-east.
        var (arena, gun, clip) = Load(global: CloseQuarters, distance: 1);
        arena.Battle.Random = new ScriptedDraws(0, 99, 3);
        Assert.Null(Fire(arena, gun, x: 11));
        Assert.Equal((48, 52), (arena.Alien.TimeUnits, arena.Alien.Energy));
        Assert.Equal(1, arena.Soldier.Direction);
        Assert.Equal(100, arena.Alien.Health);
        Assert.Equal((70, 11), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        var events = arena.Battle.Events.Drain();
        // The failed check is reported with the tile the shot turned to, then the shot, and the check's message closes the action (the player's turn shows it).
        Assert.Equal(new UnitCloseQuartersFailed(arena.Soldier.Id, arena.Alien.Id, new BattlePosition(11, 9, 0), 1, 12, 8), events[0]);
        Assert.IsType<UnitFired>(events[1]);
        Assert.Equal(new UnitAttackStopped(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, "STR_FAILED_CQB_CHECK"), events[^1]);
    }

    [Fact]
    public void APassedCloseQuartersRollLeavesTheShotAndTheNeighbourAlone()
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1);
        arena.Battle.Random = new ScriptedDraws(0);
        Assert.Null(Fire(arena, gun, x: 11));
        Assert.Equal((60, 60, 2), (arena.Alien.TimeUnits, arena.Alien.Energy, arena.Soldier.Direction));
        Assert.True(arena.Alien.Health < 100);
    }

    [Fact]
    public void CloseQuartersCombatIsOffByDefaultAndSkippedByAUnitWithNoTimeUnitsToStrike()
    {
        var (off, offGun, _) = Load(distance: 1);
        off.Battle.Random = new ScriptedDraws(0, 99, 3);
        Fire(off, offGun, x: 11);
        Assert.Equal((60, 60, 2), (off.Alien.TimeUnits, off.Alien.Energy, off.Soldier.Direction));
        var (tired, tiredGun, _) = Load(global: CloseQuarters, distance: 1);
        tired.Alien.TimeUnits = 11;
        tired.Battle.Random = new ScriptedDraws(0, 99, 3);
        Fire(tired, tiredGun, x: 11);
        Assert.Equal((11, 2), (tired.Alien.TimeUnits, tired.Soldier.Direction));
    }

    // ---- the end of the action ----

    [Fact]
    public void AnOpposingUnitThatSeesAShotAnswersItAfterTheShootersStateEnded()
    {
        var (arena, gun, _) = Load();
        var reactor = Shot(arena, arena.Alien).Gun;
        Assert.NotNull(reactor);
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Reactions = 100 };
        arena.Alien.TimeUnits = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 1 };
        Assert.Null(Fire(arena, gun));
        // The soldier's shot, then the alien's snap shots at it (its reaction states ran behind the shooter's): the reactor is asked again while its score
        // reaches the shooter's, so it fires until its time units are gone, each time paying the snap shot's 20.
        var fired = arena.Battle.Events.Drain().OfType<UnitFired>().Select(static shot => shot.UnitId).ToArray();
        Assert.Equal(arena.Soldier.Id, fired[0]);
        Assert.True(fired.Length > 1);
        Assert.All(fired.Skip(1), id => Assert.Equal(arena.Alien.Id, id));
        Assert.Equal(100 - 20 * (fired.Length - 1), arena.Alien.TimeUnits);
    }

    [Fact]
    public void AReactionThatKillsTheShooterEndsItsActionWithoutFurtherShotsFromTheReactor()
    {
        var (arena, gun, _) = Load();
        Assert.NotNull(Shot(arena, arena.Alien).Gun);
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Reactions = 100 };
        arena.Alien.TimeUnits = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 1, Health = 5 };
        arena.Soldier.Health = 5;
        Assert.Null(Fire(arena, gun));
        var events = arena.Battle.Events.Drain();
        // The soldier's shot hurts the alien; the alien's snap shot kills the soldier, and the reactions behind it find the target dead.
        Assert.True(arena.Soldier.IsOut);
        Assert.Contains(events, @event => @event is UnitDied died && died.UnitId == arena.Soldier.Id);
        Assert.Equal([arena.Soldier.Id, arena.Alien.Id], events.OfType<UnitFired>().Select(static shot => shot.UnitId));
        Assert.Equal(80, arena.Alien.TimeUnits);
        Assert.True(arena.Alien.Health < 100);
    }

    [Fact]
    public void WhenNoOneCouldAnswerTheUnitsThatSeeTheShooterSpotIt()
    {
        var (arena, gun, _) = Load();
        arena.Alien.UnitsSpottedThisTurn.Clear();
        Array.Fill(arena.Soldier.TurnsSinceSpotted, (byte)255);
        Assert.Null(Fire(arena, gun));
        Assert.True(arena.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile] <= 1);
    }

    [Fact]
    public void AKilledTargetIsReportedAndTheShotsAfterItStillFire()
    {
        var (arena, gun, _) = Load(clip: "power: 200\ndamageType: 1\ndamageAlter: {RandomType: 3}");
        Assert.Null(Fire(arena, gun, BattleAimKind.AutoShot));
        var events = arena.Battle.Events.Drain();
        Assert.Contains(events, static @event => @event is UnitDied);
        Assert.NotEmpty(events.OfType<UnitFired>());
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
    }

    // ---- the command ----

    private static BattleRuntime RuntimeOf(Arena arena) => new(arena.Battle, arena.Rules, new ScriptedDraws(0), null);

    [Fact]
    public void TheAttackCommandReturnsTheShotThenItsHitAndRefusesWithoutFaultingTheBattle()
    {
        var (arena, gun, clip) = Load();
        var runtime = RuntimeOf(arena);
        var result = runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, Target: new BattlePosition(15, 10, 0)));
        Assert.IsType<UnitFired>(result.Events[0]);
        Assert.IsType<UnitHit>(result.Events[1]);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal((70, 11), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.True(runtime.Revision > 0);

        // The launch is a command of its own: this gun is no launcher, and a launch needs waypoints.
        Assert.Equal("battle.attack-action-not-offered", Refusal(runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.Launch, Waypoints: [new BattlePosition(15, 10, 0)]))));
        // A refused shot returns its reason alone and changes nothing.
        arena.Soldier.TimeUnits = 5;
        var revision = runtime.Revision;
        Assert.Equal("battle.attack-refused", Refusal(runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, Target: new BattlePosition(15, 10, 0)))));
        Assert.Equal(revision, runtime.Revision);
        Assert.Equal((5, 11), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Assert.Null(runtime.Fault);
    }

    [Fact]
    public void AShotThatTurnedTheUnitAndStoppedIsACommandResultNotARefusal()
    {
        var (arena, gun, _) = Load();
        arena.Stand(arena.Alien, 10, 5, 6);
        arena.See();
        arena.Soldier.TimeUnits = arena.Soldier.TurnCost(arena.Rules);
        var runtime = RuntimeOf(arena);
        var result = runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, Target: new BattlePosition(10, 5, 0)));
        Assert.Equal([typeof(UnitTurned), typeof(UnitAttackStopped)], Events(result).Select(static @event => @event.GetType()));
        Assert.Equal(new UnitAttackStopped(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, "STR_NOT_ENOUGH_TIME_UNITS"), Events(result)[1]);
        Assert.Equal(1, arena.Soldier.Direction);
    }

    [Fact]
    public void TheForceFireFlagOfTheCommandReachesTheShot()
    {
        var (arena, gun, _) = Load();
        var result = RuntimeOf(arena).Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, Target: new BattlePosition(13, 10, 0), ForceFire: true));
        var fired = Assert.IsType<UnitFired>(Events(result)[0]);
        Assert.Contains(fired.Trajectory, static voxel => voxel == new BattlePosition(13 * 16 + 8, 10 * 16 + 8, 12));
    }

    // ---- the pellets' power and the explosive's centre ----

    private static int DamageOf(string clip)
    {
        var (arena, gun, _) = Load(clip: clip);
        Assert.Null(Fire(arena, gun));
        return 100 - arena.Alien.Health;
    }

    [Fact]
    public void APelletsPowerLosesTheRangeReductionAsAFractionBeforeItIsTruncated()
    {
        // 0.5 power a tile past the threshold 0 at about 4.5 tiles is a reduction of 2.25: 10 - 2.25 = 7.75 truncates to 7 (the reduction truncated first would give 8).
        var seven = DamageOf("power: 7\ndamageType: 1\ndamageAlter: {RandomType: 3}");
        var eight = DamageOf("power: 8\ndamageType: 1\ndamageAlter: {RandomType: 3}");
        var ten = DamageOf(Bullet);
        Assert.True(seven < eight && eight < ten);
        var (arena, gun, _) = Load(clip: Bullet + "\nshotgunPellets: 2\nshotgunBehavior: 1\nshotgunSpread: 0\npowerRangeReduction: 0.5");
        Assert.Null(Fire(arena, gun));
        // The first hit has no reduction (a shotgun's blast range is 0), the pellet has it.
        Assert.Equal(ten + seven, 100 - arena.Alien.Health);
    }

    [Fact]
    public void AWeaponThatIgnoresItsAmmunitionsPowerGivesItsOwnToThePellets()
    {
        var six = DamageOf("power: 6\ndamageType: 1\ndamageAlter: {RandomType: 3}");
        var (single, singleGun, _) = Load(gun: Rifle + "\nignoreAmmoPower: true\npower: 6");
        Assert.Null(Fire(single, singleGun));
        var first = 100 - single.Alien.Health;

        var (arena, gun, _) = Load(gun: Rifle + "\nignoreAmmoPower: true\npower: 6", clip: Bullet + "\nshotgunPellets: 2\nshotgunBehavior: 1\nshotgunSpread: 0");
        Assert.Null(Fire(arena, gun));
        Assert.Equal(first + six, 100 - arena.Alien.Health);
    }

    [Fact]
    public void AnExplosiveShotBlastsTwoVoxelsBackFromAWallAndAtTheVoxelOfAUnit()
    {
        const string Explosive = "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}";
        var (arena, gun, _) = Load(clip: Explosive);
        Assert.Null(Fire(arena, gun));
        var events = arena.Battle.Events.Drain();
        var fired = events.OfType<UnitFired>().Single();
        Assert.Equal(4, fired.Impact);
        Assert.Equal(fired.Trajectory[^1], events.OfType<BattleExploded>().Single().Center);

        var (wall, wallGun, _) = Load(clip: Explosive);
        wall.Put(13, 10, 0, BattleTilePart.ObjectPart, Wall);
        wall.See();
        Assert.Null(Fire(wall, wallGun, x: 13, force: true));
        var wallEvents = wall.Battle.Events.Drain();
        var wallShot = wallEvents.OfType<UnitFired>().Single();
        Assert.Equal(3, wallShot.Impact);
        Assert.Equal(wallShot.Trajectory[^3], wallEvents.OfType<BattleExploded>().First().Center);
    }

    [Fact]
    public void AnExplosiveShotsPowerFallsWithTheDistanceItFlew()
    {
        const string Explosive = "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}";
        var (plain, plainGun, _) = Load(clip: Explosive);
        Assert.Null(Fire(plain, plainGun));
        var full = plain.Battle.Events.Drain().OfType<BattleExploded>().Single().Power;
        var (arena, gun, _) = Load(clip: Explosive + "\npowerRangeReduction: 1");
        Assert.Null(Fire(arena, gun));
        var reduced = arena.Battle.Events.Drain().OfType<BattleExploded>().Single().Power;
        // About 4.5 tiles at one a tile.
        Assert.True(reduced < full && reduced >= full - 6, $"{reduced} against {full}");
    }

    // ---- shooting at things ----

    [Fact]
    public void AShotAtAnObjectDestroysItAndForceFireAtAUnitsTileHitsTheUnit()
    {
        var (arena, gun, _) = Load(clip: "power: 100\ndamageType: 1\ndamageAlter: {RandomType: 3}");
        arena.Put(13, 10, 0, BattleTilePart.ObjectPart, BaseModule);
        arena.See();
        Assert.NotNull(arena.Map.MapData(arena.Tile(13, 10), BattleTilePart.ObjectPart));
        Assert.Null(Fire(arena, gun, x: 13, force: true));
        Assert.Equal(3, arena.Battle.Events.Drain().OfType<UnitFired>().Single().Impact);
        Assert.Null(arena.Map.MapData(arena.Tile(13, 10), BattleTilePart.ObjectPart));

        var (forced, forcedGun, _) = Load();
        Assert.Null(Fire(forced, forcedGun, force: true));
        var events = forced.Battle.Events.Drain();
        Assert.Equal(4, events.OfType<UnitFired>().Single().Impact);
        Assert.True(forced.Alien.Health < 100);
        Assert.Equal(1, forced.Alien.Statistics.ShotAtCounter);
    }

    [Fact]
    public void ATurretUnitTurnsOnlyItsTurretOneTimeUnitAnEighthAndIsNotHeldByCloseQuarters()
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1);
        arena.Soldier.TurretType = 2;
        arena.Stand(arena.Alien, 11, 11, 6);
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0, 99, 3);
        Assert.Null(Fire(arena, gun, x: 11, y: 11));
        // South-east of an east-facing turret: one eighth, one time unit.
        Assert.Equal((2, 3), (arena.Soldier.Direction, arena.Soldier.DirectionTurret));
        Assert.Equal(100 - 1 - 30, arena.Soldier.TimeUnits);
        var turned = arena.Battle.Events.Drain().OfType<UnitTurned>().Single();
        Assert.Equal((3, 1, 1), (turned.Direction, turned.Eighths, turned.TimeUnitsSpent));
        // The neighbour made no check on a turret unit.
        Assert.Equal((60, 60), (arena.Alien.TimeUnits, arena.Alien.Energy));
    }

    [Fact]
    public void AShotThatCannotStartAfterTheTurnStopsTheActionWithItsReason()
    {
        // Out of range, no ammunition and a wall in the line, each found after the unit turned to the north.
        var (far, farGun, _) = Load(gun: Rifle + "\nmaxRange: 3");
        far.Stand(far.Alien, 10, 5, 6);
        far.See();
        Assert.Null(Fire(far, farGun, x: 10, y: 5));
        AssertStopped(far, farGun, "STR_OUT_OF_RANGE");

        var (empty, emptyGun, _) = Load();
        empty.Stand(empty.Alien, 10, 5, 6);
        empty.See();
        emptyGun.AmmoSlots[0] = null;
        Assert.Null(Fire(empty, emptyGun, x: 10, y: 5));
        AssertStopped(empty, emptyGun, "STR_NO_AMMUNITION_LOADED");

        var (walled, walledGun, _) = Load();
        walled.Stand(walled.Alien, 10, 5, 6);
        walled.Put(10, 7, 0, BattleTilePart.ObjectPart, Wall);
        walled.See();
        Assert.Null(Fire(walled, walledGun, x: 10, y: 5));
        AssertStopped(walled, walledGun, "STR_NO_LINE_OF_FIRE");

        static void AssertStopped(Arena arena, BattleItem gun, string reason)
        {
            var events = arena.Battle.Events.Drain();
            Assert.IsType<UnitTurned>(events[0]);
            Assert.Equal(new UnitAttackStopped(arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, reason), events[1]);
            Assert.Equal(2, events.Count);
            Assert.Equal(0, arena.Soldier.Direction);
            Assert.Equal(100, arena.Alien.Health);
        }
    }

    [Fact]
    public void AnArcingShotWithNoArcToTheTargetIsRefusedWithNoTrajectory()
    {
        var (arena, gun, _) = Load(gun: Rifle + "\narcingShot: true");
        // Walls on the three tiles in front of the shooter and a ceiling over all of them: a low arc hits the wall, a high one the ceiling.
        for (var y = 9; y <= 11; y++)
        {
            arena.Put(11, y, 0, BattleTilePart.ObjectPart, Wall);
            arena.Put(11, y, 1, BattleTilePart.Floor, Floor);
        }
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.See();
        Assert.Equal("STR_NO_TRAJECTORY", Fire(arena, gun)!.TextKey);
        Assert.Equal((100, 12), (arena.Soldier.TimeUnits, arena.Battle.AmmoQuantityOf(arena.Battle.Items.First(item => item.Id == gun.AmmoSlots[0]))));
    }

    // ---- the statistics of a hit ----

    [Fact]
    public void AHitOnAFriendCountsForBothAndAHitOnAnUnaimedUnitIsNotALandedShot()
    {
        var (arena, gun, _) = Load();
        arena.Alien.OriginalFaction = BattleFaction.Player;
        Assert.Null(Fire(arena, gun));
        Assert.Equal((1, 1), (arena.Alien.Statistics.ShotByFriendlyCounter, arena.Soldier.Statistics.ShotFriendlyCounter));

        // Aimed at the empty tile behind the alien, the shot passes through the alien: a hit, not a landed shot, and no shot at.
        var (behind, behindGun, _) = Load();
        Assert.Null(Fire(behind, behindGun, x: 17, force: true));
        Assert.Equal((1, 0, 0), (behind.Alien.Statistics.HitCounter, behind.Soldier.Statistics.ShotsLandedCounter, behind.Alien.Statistics.ShotAtCounter));
        Assert.Equal((0, 0), (behind.Alien.Statistics.ShotByFriendlyCounter, behind.Soldier.Statistics.ShotFriendlyCounter));
    }

    [Fact]
    public void ALandedShotPastThirtyTilesIsALongDistanceHitAndOneWithLessAccuracyThanTilesIsALowAccuracyHit()
    {
        var (arena, gun, _) = Load(gun: Rifle.Replace("accuracyAimed: 100", "accuracyAimed: 10", StringComparison.Ordinal));
        arena.Stand(arena.Soldier, 0, 0, 3);
        arena.Stand(arena.Alien, 28, 28, 7);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Firing = 100 };
        arena.See();
        Assert.Null(Fire(arena, gun, x: 28, y: 28));
        var statistics = arena.Soldier.Statistics;
        Assert.Equal((1, 1, 1), (statistics.ShotsLandedCounter, statistics.LongDistanceHitCounter, statistics.LowAccuracyHitCounter));

        // A close, accurate hit is neither.
        var (near, nearGun, _) = Load();
        Assert.Null(Fire(near, nearGun));
        Assert.Equal((1, 0, 0), (near.Soldier.Statistics.ShotsLandedCounter, near.Soldier.Statistics.LongDistanceHitCounter, near.Soldier.Statistics.LowAccuracyHitCounter));
    }

    // ---- close quarters combat ----

    // (The sneak up of a hostile shooter reads the neighbour's visible units; only the player's units can be commanded to shoot until the AI, so that branch has no command to run it.)

    [Fact]
    public void AnXcomShooterTheAliensSpottedForMoreThanATurnIsNotCheckedWithTheSneakUp()
    {
        var (arena, gun, _) = Load(global: CloseQuarters + "closeQuartersSneakUpGlobal: 100\n", distance: 1);
        arena.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 5;
        arena.Battle.Random = new ScriptedDraws(0, 99, 3);
        Assert.Null(Fire(arena, gun, x: 11));
        Assert.Equal((60, 60), (arena.Alien.TimeUnits, arena.Alien.Energy));

        var (recent, recentGun, _) = Load(global: CloseQuarters + "closeQuartersSneakUpGlobal: 100\n", distance: 1);
        recent.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 1;
        recent.Battle.Random = new ScriptedDraws(0, 99, 3);
        Assert.Null(Fire(recent, recentGun, x: 11));
        Assert.Equal((48, 52), (recent.Alien.TimeUnits, recent.Alien.Energy));
    }

    [Theory]
    [InlineData(BattleFaction.Neutral)] // civilians do not hold the player
    [InlineData(BattleFaction.Player)] // a unit of the shooter's own side does not either
    public void ANeighbourOfTheShootersSideOrACivilianDoesNotMakeItRoll(BattleFaction faction)
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1);
        arena.Alien.Faction = faction;
        arena.Battle.Random = new ScriptedDraws(0, 99, 3);
        Fire(arena, gun, x: 11);
        Assert.Equal((60, 60, 2), (arena.Alien.TimeUnits, arena.Alien.Energy, arena.Soldier.Direction));
    }

    [Fact]
    public void ANeighbourThatCreatesNoMeleeThreatDoesNotMakeItRoll()
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1,
            more: static rules => Replaced(rules, "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n", "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    createsMeleeThreat: false\n"));
        arena.Battle.Random = new ScriptedDraws(0, 99, 3);
        Fire(arena, gun, x: 11);
        Assert.Equal((60, 60, 2), (arena.Alien.TimeUnits, arena.Alien.Energy, arena.Soldier.Direction));
    }

    [Theory]
    [InlineData(0, 10, 10, 0)] // straight down: the shooter's own tile
    [InlineData(1, 10, 10, 1)] // straight up: one level above
    [InlineData(2, 10, 9, 0)] // left 90 degrees of an east-facing unit: north
    [InlineData(5, 10, 11, 0)] // right 90 degrees: south
    public void AFailedCloseQuartersRollTurnsTheShotAsTheRollSays(int roll, int x, int y, int z)
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1);
        arena.Battle.Random = new ScriptedDraws(0, 99, roll);
        Fire(arena, gun, x: 11);
        var failed = arena.Battle.Events.Drain().OfType<UnitCloseQuartersFailed>().Single();
        Assert.Equal(new BattlePosition(x, y, z), failed.Target);
    }

    [Fact]
    public void AFailedRollThatTurnsTheShotOffTheMapFiresAtTheShootersFeet()
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1);
        arena.Stand(arena.Soldier, 10, 0, 2);
        arena.Stand(arena.Alien, 11, 0, 6);
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0, 99, 2);
        Fire(arena, gun, x: 11, y: 0);
        Assert.Equal(new BattlePosition(10, 0, 0), arena.Battle.Events.Drain().OfType<UnitCloseQuartersFailed>().Single().Target);
    }

    [Fact]
    public void TheFirstFailedCheckStopsTheChecksOfTheOtherNeighbours()
    {
        var (arena, gun, _) = Load(global: CloseQuarters, distance: 1);
        var second = new BattleUnit(99, BattleFaction.Hostile, BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = arena.Alien.ArmorId,
            ArmorSize = 1,
            UnitType = arena.Alien.UnitType,
            Health = 100,
            MovementType = 0,
        };
        arena.Battle.AddUnit(second);
        arena.Stand(second, 11, 11, 6);
        second.TimeUnits = 60;
        second.Energy = 60;
        arena.See();
        // Two neighbours roll their sneak up chance first, then the first one's check fails and its roll is drawn.
        arena.Battle.Random = new ScriptedDraws(0, 0, 99, 3);
        Fire(arena, gun, x: 11);
        Assert.Equal((48, 52), (arena.Alien.TimeUnits, arena.Alien.Energy));
        Assert.Equal((60, 60), (second.TimeUnits, second.Energy));
    }

    // ---- the menu rules, an impact off the map and the end of a burst ----

    [Fact]
    public void ALauncherOffersNoAimedShotAndNoShotOfTheSlotItLaunchesFrom()
    {
        var (arena, gun, _) = Load(gun: Rifle + "\nwaypoints: 1");
        Assert.Equal("battle.attack-action-not-offered", Fire(arena, gun)!.Code);
        Assert.Equal("battle.attack-action-not-offered", Fire(arena, gun, BattleAimKind.SnapShot)!.Code);
        Assert.Equal("battle.attack-action-not-offered", Fire(arena, gun, BattleAimKind.AutoShot)!.Code);
        Assert.Equal((100, 12), (arena.Soldier.TimeUnits, arena.Battle.AmmoQuantityOf(arena.Battle.Items.First(item => item.Id == gun.AmmoSlots[0]))));
    }

    [Fact]
    public void AShotThatLeavesTheMapRaisesNoBlast()
    {
        var (arena, gun, _) = Load(gun: Rifle.Replace("accuracyAimed: 100", "accuracyAimed: 1", StringComparison.Ordinal),
            clip: "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}");
        arena.Stand(arena.Soldier, 1, 1, 2);
        arena.Stand(arena.Alien, 4, 1, 6);
        arena.See();
        arena.Battle.Random = new ScriptedDraws(Wild);
        Fire(arena, gun, x: 4, y: 1);
        var events = arena.Battle.Events.Drain();
        var fired = events.OfType<UnitFired>().Single();
        Assert.Equal(5, fired.Impact);
        Assert.DoesNotContain(events, static @event => @event is BattleExploded);
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
    }

    [Fact]
    public void ABurstStopsForAUnitThatWasTakenOut()
    {
        var (taken, takenGun, _) = Load(clip: "power: 200\ndamageType: 2\ndamageAlter: {RandomType: 3}", distance: 1);
        taken.Soldier.Health = 1; // the blast next to it takes the shooter out after the first shot
        FireKind(taken, takenGun, BattleAimKind.AutoShot, 11);
        Assert.Single(taken.Battle.Events.Drain().OfType<UnitFired>());
        Assert.True(taken.Soldier.IsOut);
    }

    private static CampaignReason? FireKind(Arena arena, BattleItem gun, BattleAimKind kind, int x) => Fire(arena, gun, kind, x);

    [Fact]
    public void TheTrialCopyOfACommandKeepsTheLiveUnitsVisibleUnitsWhichTheRestoreWouldRecalculate()
    {
        var (arena, _, _) = Load();
        Assert.Equal([arena.Alien.Id], arena.Soldier.VisibleUnits);
        // The live list was kept up to date step by step and no longer holds the alien (the restore's sight would find it again).
        arena.Soldier.ClearVisibleUnits();
        var battles = Assert.IsType<CampaignBattles>(arena.Campaign.GetQuery<IBattleQuery>());
        BattleState? copy = null;
        var trial = typeof(CampaignBattles).GetMethod("TrialRefusal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Null(trial.Invoke(battles, [arena.Battle, new Func<BattleState, CampaignReason?>(scratch =>
        {
            copy = scratch;
            return null;
        })]));
        Assert.NotNull(copy);
        Assert.NotSame(arena.Battle, copy);
        Assert.Empty(copy.UnitById(arena.Soldier.Id)!.VisibleUnits);
        Assert.Equal([arena.Soldier.Id], copy.UnitById(arena.Alien.Id)!.VisibleUnits);
    }

    // ---- the spray ----

    private const string Spray = "\nsprayWaypoints: 3";

    private static BattlePosition Centre(int x, int y) => new(x * 16 + 8, y * 16 + 8, 12);

    [Fact]
    public void ASprayFiresItsShotsAtTheWaypointsSpacedBetweenThem()
    {
        var (arena, gun, clip) = Load(gun: Rifle + Spray, distance: 8);
        var turn = arena.Soldier.TurnCost(arena.Rules);
        var waypoints = new[] { new BattlePosition(13, 12, 0), new BattlePosition(15, 12, 0) };
        Assert.Null(arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, BattleAimKind.AutoShot, default, false, waypoints));
        var shots = arena.Battle.Events.Drain().OfType<UnitFired>().ToList();
        Assert.Equal(3, shots.Count);
        // Three shots over two waypoints: the first at the first waypoint, the middle one between them, the last at the second.
        Assert.Contains(Centre(13, 12), shots[0].Trajectory);
        Assert.Contains(Centre(14, 12), shots[1].Trajectory);
        Assert.Contains(Centre(15, 12), shots[2].Trajectory);
        // One turn to the first waypoint, one payment for the burst.
        Assert.Equal((100 - turn - 40, 9), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
    }

    [Fact]
    public void ASprayWaypointPastTheRangeIsMovedOntoIt()
    {
        var (arena, gun, _) = Load(gun: Rifle + Spray + "\nmaxRange: 4\nconfAuto: {shots: 2}", distance: 8);
        var waypoints = new[] { new BattlePosition(12, 12, 0), new BattlePosition(18, 10, 0) };
        Assert.Null(arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, BattleAimKind.AutoShot, default, false, waypoints));
        var shots = arena.Battle.Events.Drain().OfType<UnitFired>().ToList();
        Assert.Equal(2, shots.Count);
        // Eight tiles away at a range of 4: half way.
        Assert.Contains(Centre(14, 10), shots[1].Trajectory);
        Assert.DoesNotContain(Centre(18, 10), shots[1].Trajectory);
        Assert.Equal(2, arena.Soldier.Direction);
    }

    [Fact]
    public void ASprayNeedsAnAutoShotWithAWeaponThatSpraysAndNoMoreWaypointsThanItHas()
    {
        var (arena, gun, _) = Load(gun: Rifle + Spray, distance: 8);
        BattlePosition[] two = [new(13, 12, 0), new(15, 12, 0)];
        BattlePosition[] four = [new(13, 12, 0), new(14, 12, 0), new(15, 12, 0), new(16, 12, 0)];
        Assert.Equal("battle.attack-spray-not-offered", arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, default, false, two)!.Code);
        Assert.Equal("battle.attack-spray-not-offered", arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, BattleAimKind.AutoShot, default, false, four)!.Code);
        Assert.Equal("battle.attack-target-outside", arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, BattleAimKind.AutoShot, default, false,
            [new BattlePosition(13, 12, 0), new BattlePosition(99, 12, 0)])!.Code);
        var (plain, plainGun, _) = Load(distance: 8);
        Assert.Equal("battle.attack-spray-not-offered", plain.Battle.ShootAction(BattleResearchGate.Everything, plain.Soldier.Id, plainGun.Id, BattleAimKind.AutoShot, default, false, two)!.Code);
        Assert.Equal((100, 12), (plain.Soldier.TimeUnits, plain.Battle.AmmoQuantityOf(plain.Battle.Items.First(item => item.Id == plainGun.AmmoSlots[0]))));
    }

    [Fact]
    public void TheAttackCommandCarriesTheSprayWaypoints()
    {
        var (arena, gun, _) = Load(gun: Rifle + Spray, distance: 8);
        var result = RuntimeOf(arena).Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.AutoShot, SprayWaypoints: [new BattlePosition(13, 12, 0), new BattlePosition(15, 12, 0)]));
        var shots = Events(result).OfType<UnitFired>().ToList();
        Assert.Equal(3, shots.Count);
        Assert.Contains(Centre(13, 12), shots[0].Trajectory);
    }
}
