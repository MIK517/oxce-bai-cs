using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S9, second part: <c>TileEngine::checkReactionFire</c> and <c>tryReaction</c>, the reaction states and their scripts. The arena is the turn and
/// kneel tests' one (the soldier at (10, 10) facing east, the alien five tiles east facing west with a gun that does 10 damage); the soldier kneels, which draws the
/// reaction, and the events say who fired.
/// </summary>
public sealed class BattleReactionFireTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";
    private const string ClipText = "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    weight: 1";

    private static Arena Make(Func<string, string>? edit = null)
    {
        string Edit(string rules)
        {
            rules = Replaced(rules, "  - type: SUIT\n", "  - type: SUIT\n    turnCost: 2\n");
            rules = Replaced(rules, "stats: {tu: 50, firing: 61}", "stats: {tu: 50, firing: 61, reactions: 100}");
            rules = Replaced(rules, GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50\n    accuracySnap: 100");
            rules = Replaced(rules, ClipText, ClipText + "\n    power: 10\n    damageType: 1\n    damageAlter: {RandomType: 3}");
            return edit is null ? rules : edit(rules);
        }

        var arena = Flat(distance: 5, edit: Edit);
        var gun = new BattleItem(arena.Battle.TakeItemId(), "GUN") { OwnerUnitId = arena.Alien.Id, Slot = "STR_RIGHT_HAND", IsWeaponWithAmmo = true };
        arena.Battle.AddItem(gun);
        var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 12, IsAmmo = true };
        arena.Battle.AddItem(clip);
        gun.AmmoSlots[0] = clip.Id;
        return arena;
    }

    private static CampaignCommandResult Kneel(Arena arena) => arena.Campaign.Execute(new KneelUnit(arena.Soldier.Id));

    private static int ShotsOf(CampaignCommandResult result, BattleUnit unit) => result.Events.OfType<UnitFired>().Count(fired => fired.UnitId == unit.Id);

    [Fact]
    public void AReactorIsAskedAgainWhileItsScoreReachesTheMoversAndStopsAtTenReactions()
    {
        // A snap shot costs 1 percent of 1000 time units (10), which takes 1.0 off the reactor's score of 100 each time: only the cap of ten (count above 10) stops it.
        var arena = Make(rules => Replaced(rules, "tuSnap: 20", "tuSnap: 1"));
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 1000, Reactions = 100 };
        arena.Alien.TimeUnits = 1000;
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        var result = Kneel(arena);
        Assert.Equal(10, ShotsOf(result, arena.Alien));
        Assert.Equal(900, arena.Alien.TimeUnits);
    }

    [Fact]
    public void AReactionThatKillsTheMoverCancelsTheShotsBehindIt()
    {
        var arena = Make(rules => Replaced(rules, "power: 10", "power: 200"));
        // (An accuracy far above 100 does not deviate, so the first snap shot hits.)
        arena.Alien.Stats = arena.Alien.Stats with { Firing = 1000 };
        var result = Kneel(arena);
        // The first snap shot kills the soldier; the reactions behind it find the target dead (reactionShoot) and stop with no shot and no time units spent.
        Assert.Equal((1, true), (ShotsOf(result, arena.Alien), arena.Soldier.IsOut));
        Assert.Equal(arena.Alien.Stats.TimeUnits - 13, arena.Alien.TimeUnits);
    }

    [Fact]
    public void ANearbyUnitWhoseWeaponIsOutOfRangeSeesTheMoverAndDoesNotShoot()
    {
        var arena = Make(rules => Replaced(rules, "tuSnap: 20", "tuSnap: 20\n    maxRange: 3"));
        var result = Kneel(arena);
        Assert.Equal(0, ShotsOf(result, arena.Alien));
        Assert.Contains(arena.Soldier.Id, arena.Alien.VisibleUnits);
        Assert.Equal(arena.Alien.Stats.TimeUnits, arena.Alien.TimeUnits);
    }

    [Fact]
    public void TheReactionScriptsSeeTheContextAndMayChangeTheChance()
    {
        var arena = Make();
        var hooks = new RecordingHooks();
        arena.Battle.Hooks = hooks;
        var result = Kneel(arena);
        // The scripts ran in the reference's order for each reaction the reactor was asked for (the weapon's is skipped: a kneel has no weapon).
        Assert.NotEmpty(hooks.Calls);
        Assert.Equal(["unit-action", "unit-reaction"], hooks.Calls.Take(2).Select(static call => call.Name).ToArray());
        var first = hooks.Calls[0].Context;
        Assert.Equal((arena.Soldier.Id, arena.Alien.Id, BattleActionType.SnapShot, 1, BattleActionType.Kneel, 0), (first.Target.Id, first.Unit.Id, first.Type, first.Count, first.OriginalType, first.Arc));
        Assert.Equal(80, hooks.Calls[0].Values.Distance);
        Assert.Equal(100, hooks.Calls[0].Values.Chance);
        Assert.Equal(0, ShotsOf(result, arena.Alien));
        Assert.Equal(arena.Alien.Stats.TimeUnits, arena.Alien.TimeUnits);
    }

    [Fact]
    public void AMindControlledHostileUnitDrawsNoReaction()
    {
        // A soldier that the aliens took over (its faction is hostile, its original one X-COM's) is not shot at by the X-COM unit that sees it.
        var arena = Make();
        arena.Alien.Faction = BattleFaction.Player;
        arena.Battle.Side = BattleFaction.Hostile;
        arena.Soldier.Faction = BattleFaction.Hostile;
        arena.Forget();
        var alienUnits = arena.Alien.TimeUnits;
        Assert.False(arena.Battle.CheckReactionFire(arena.Soldier, new BattleReactionOrigin(BattleActionType.Kneel)));
        Assert.Equal(alienUnits, arena.Alien.TimeUnits);
        // A hostile unit that began hostile does draw it.
        arena.Soldier.OriginalFaction = BattleFaction.Hostile;
        Assert.True(arena.Battle.CheckReactionFire(arena.Soldier, new BattleReactionOrigin(BattleActionType.Kneel)));
    }

    [Fact]
    public void AKneelingUnitThatGoesUpIsShotByTheKneelsReactionBeforeItTakesItsStep()
    {
        // moveUpDown: kneel() pushes the reactions into the empty list (the first starts at once) and the walk state goes behind them.
        var arena = Make();
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        arena.Soldier.Kneeled = true;
        var events = Events(arena.Campaign.Execute(new MoveUnitUpDown(arena.Soldier.Id, true))).ToList();
        var kneel = events.FindIndex(static @event => @event is UnitKneeled);
        var shot = events.FindIndex(@event => @event is UnitFired fired && fired.UnitId == arena.Alien.Id);
        var step = events.FindIndex(static @event => @event is UnitStepped);
        Assert.True(kneel >= 0 && kneel < shot && shot < step, $"kneel {kneel}, shot {shot}, step {step}");
        Assert.Equal(1, arena.Soldier.Position!.Value.Z);
    }

    [Fact]
    public void AKneelingUnitThatTheKneelsReactionKillsNeverTakesItsStepUp()
    {
        var arena = Make(rules => Replaced(rules, "power: 10", "power: 200"));
        arena.Alien.Stats = arena.Alien.Stats with { Firing = 1000 };
        arena.Soldier.Kneeled = true;
        var events = Events(arena.Campaign.Execute(new MoveUnitUpDown(arena.Soldier.Id, true)));
        Assert.True(arena.Soldier.IsOut);
        Assert.DoesNotContain(events, static @event => @event is UnitStepped);
        Assert.Equal(BattleWalkStop.Incapacitated, End(events).Stop);
        Assert.Equal(1, ShotsOf(new CampaignCommandResult(events), arena.Alien));
    }

    [Fact]
    public void TheFirstReactionPaysBeforeTheSameReactorIsAskedAgainOnTheKneelButton()
    {
        // The reactor can afford one snap shot (13 of its 25 time units) but not two, and its score (1300 of 2500 after the first reduction) stays above the mover's: statePushBack
        // starts the first state at once, so when the reactor is asked again it has 12 time units left, fails haveTU and the scripts are not run a second time.
        var arena = Make();
        arena.Alien.Stats = arena.Alien.Stats with { Reactions = 5000 };
        arena.Alien.TimeUnits = 25;
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        var hooks = new CountingHooks();
        arena.Battle.Hooks = hooks;
        var result = Kneel(arena);
        // (The command runs on a trial copy first, which shares the scripts: one run asks the scripts once for the one reaction, two runs twice; a second ask per run would make four.)
        Assert.Equal((1, 2, 12), (ShotsOf(result, arena.Alien), hooks.UnitActions, arena.Alien.TimeUnits));
    }

    [Fact]
    public void AUnitThatLandsWithoutBeingTheSelectedOneDrawsReactionsThatCancel()
    {
        // UnitFallBState: tryReaction succeeds for the reactor, but the reaction state cancels at its init because the target is not the selected unit.
        var arena = BattleWalkTests.Make();
        BattleTestSupport.GunFor(arena.Battle, arena.Alien);
        arena.Battle.SetTile(arena.Soldier, BattleWalkTests.At(10, 10, 2));
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        arena.Battle.SelectedUnitId = arena.Alien.Id;
        var alienUnits = arena.Alien.TimeUnits;
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(BattleWalkTests.At(10, 10, 0), arena.Soldier.Position);
        Assert.DoesNotContain(arena.Battle.Events.Drain(), static @event => @event is UnitFired);
        Assert.Equal(alienUnits, arena.Alien.TimeUnits);

        // The same landing of the selected unit is answered.
        var control = BattleWalkTests.Make();
        BattleTestSupport.GunFor(control.Battle, control.Alien);
        control.Battle.SetTile(control.Soldier, BattleWalkTests.At(10, 10, 2));
        control.Battle.ApplyGravity(control.Soldier.Position);
        control.Battle.SelectedUnitId = control.Soldier.Id;
        Assert.Null(control.Battle.ResolveFalls());
        Assert.Contains(control.Battle.Events.Drain(), @event => @event is UnitFired fired && fired.UnitId == control.Alien.Id);
    }

    [Fact]
    public void TheFallsAWalkLeavesPendingRunAfterTheReactionsItDrew()
    {
        // BattlescapeGame::think pushes the fall state only when the list is empty: the walk's reactions play first.
        var arena = BattleWalkTests.Make();
        BattleTestSupport.GunFor(arena.Battle, arena.Alien);
        // The reactor itself has no floor under it and waits to fall (it still sees the walker from above).
        var other = arena.Alien;
        arena.Battle.SetTile(other, BattleWalkTests.At(15, 10, 1));
        arena.Battle.ApplyGravity(other.Position);
        arena.See();
        Assert.Contains(other, arena.Battle.FallingUnits);
        var events = Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, BattleWalkTests.At(11, 10), BattleMoveType.Normal, false))).ToList();
        var shot = events.FindIndex(@event => @event is UnitFired fired && fired.UnitId == arena.Alien.Id);
        var fell = events.FindIndex(@event => @event is UnitFell fall && fall.UnitId == other.Id);
        Assert.True(shot >= 0 && fell > shot, $"shot {shot}, fell {fell}");
        // (UnitFell is raised when the whole fall is done, so the order of the events cannot tell: the shot must have left from the level above, where the reactor still hung.)
        var fired = (UnitFired)events[shot];
        Assert.True(fired.Trajectory[0].Z >= 24, $"the shot left from voxel height {fired.Trajectory[0].Z}");
    }

    [Fact]
    public void AReactionToAShotSeesTheShotsWeaponAndTargetAndAZeroScoreRunsNoUnitScripts()
    {
        var arena = Make();
        arena.Battle.Hooks = new WeaponHooks();
        arena.Alien.Stats = arena.Alien.Stats with { Health = 1000 };
        arena.Alien.Health = 1000;
        var gun = arena.Battle.InventoryOf(arena.Soldier).First(static item => item.ItemType == "GUN");
        var result = arena.Battle.ShootAction(BattleResearchGate.Everything, arena.Soldier.Id, gun.Id, BattleAimKind.SnapShot, new BattlePosition(15, 10, 0), false);
        Assert.Null(result);
        var hooks = (WeaponHooks)arena.Battle.Hooks;
        Assert.Equal((gun.Id, BattleActionType.SnapShot, arena.Alien.Id), (hooks.Weapon, hooks.Original, hooks.Target));

        // A shot the AI scores at nothing (the target is out of the weapon's range): no unit script runs for it.
        var weak = Make(rules => Replaced(rules, "tuSnap: 20", "tuSnap: 20\n    maxRange: 3"));
        var counting = new CountingHooks();
        weak.Battle.Hooks = counting;
        Assert.Equal(0, ShotsOf(Kneel(weak), weak.Alien));
        Assert.Equal(0, counting.UnitActions);
    }

    [Fact]
    public void TheReactorTurnsToTheMoverForFree()
    {
        var arena = Make();
        arena.Alien.SetDirection(7);
        arena.See();
        Assert.Contains(arena.Soldier.Id, arena.Alien.VisibleUnits);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        var units = arena.Alien.TimeUnits;
        var result = Kneel(arena);
        Assert.Equal(6, arena.Alien.Direction);
        Assert.Equal(units - 13 * ShotsOf(result, arena.Alien), arena.Alien.TimeUnits);
    }

    /// <summary>A second hostile unit with a gun of its own, standing at the tile and facing the soldier.</summary>
    private static BattleUnit SecondAlien(Arena arena, int x, int y, int direction = 6)
    {
        var other = new BattleUnit(arena.Alien.Id + 1, BattleFaction.Hostile, BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = arena.Alien.ArmorId,
            UnitType = arena.Alien.UnitType,
            Health = 100,
            MovementType = 0,
        };
        arena.Battle.AddUnit(other);
        other.Morale = (short)100;
        arena.Stand(other, x, y, direction);
        GunFor(arena.Battle, other);
        return other;
    }

    private static void Prepare(BattleUnit unit, int reactions, int timeUnits)
    {
        unit.Stats = unit.Stats with { Reactions = (short)reactions, Firing = 61 };
        unit.TimeUnits = timeUnits;
    }

    [Theory]
    [InlineData(800, 400)]
    [InlineData(400, 800)]
    public void TwoReactorsFireInTheOrderOfTheirScoresEachOnePayingItsOwnReduction(int firstReactions, int secondReactions)
    {
        // Each reactor can afford one snap shot (13 of 25 time units). The higher score goes first; its reduction takes its score below the other's, which fires next.
        var arena = Make();
        var other = SecondAlien(arena, 15, 11);
        Prepare(arena.Alien, firstReactions, 25);
        Prepare(other, secondReactions, 25);
        arena.See();
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        var result = Kneel(arena);
        var shooters = result.Events.OfType<UnitFired>().Select(static fired => fired.UnitId).ToArray();
        var (first, second) = firstReactions > secondReactions ? (arena.Alien, other) : (other, arena.Alien);
        Assert.Equal([first.Id, second.Id], shooters);
        Assert.Equal(12, first.TimeUnits);
        Assert.Equal(12, second.TimeUnits);
    }

    [Fact]
    public void AHostileReactorHoldingAnExplosiveShotAnswersWhenTheAiWouldSetItOffThere()
    {
        // damageType 2 is the high explosive: explosiveEfficacy of the lone mover is above 0 for this reactor (the other side, a lone target the AI would not blast, is the
        // launch test's), so tryReaction goes on to the roll and the shot blasts. (The test's rules make the damage type an explosion of radius 1.)
        string Explosive(string rules) => Replaced(rules, "damageType: 1", "damageType: 2");
        var arena = Make(Explosive);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        var result = Kneel(arena);
        Assert.True(ShotsOf(result, arena.Alien) >= 1);
        Assert.Contains(result.Events, static @event => @event is BattleExploded);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(10, true)]
    public void AMeleeReactionBehindAShotThatKilledItsTargetIsCancelledWithNothingPaid(int power, bool strikes)
    {
        // The first reactor shoots from five tiles away (its state starts at once); the second, next to the soldier, answers with a hit that starts only when the first state
        // is done: reactionShoot finds the soldier out and the state ends without paying.
        var arena = Make(rules => Replaced(power == 10 ? rules : Replaced(rules, "power: 10", $"power: {power}"), "tuSnap: 20",
            "tuSnap: 20\n    meleePower: 30\n    costMelee: {time: 20}\n    accuracyMelee: 100\n    meleeType: 1\n    meleeAlter: {RandomType: 3}"));
        // (Beside the soldier, not between it and the first reactor, so the first reactor's line of fire is clear.)
        var other = SecondAlien(arena, 10, 11, 0);
        Prepare(arena.Alien, 800, 50);
        Prepare(other, 100, 50);
        arena.Alien.Stats = arena.Alien.Stats with { Firing = 1000 };
        arena.See();
        if (strikes)
        {
            arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
            arena.Soldier.Health = 1000;
        }
        var result = Kneel(arena);
        Assert.Equal(!strikes, arena.Soldier.IsOut);
        var hits = result.Events.OfType<UnitMeleeAttacked>().Count(attacked => attacked.UnitId == other.Id);
        Assert.Equal(strikes, hits > 0);
        if (!strikes) Assert.Equal(50, other.TimeUnits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMeleeReactionToALandingStrikesOnlyWhenTheLandedUnitIsTheSelectedOne(bool selected)
    {
        // The hit state's own reactionShoot check (MeleeAttackBState::init): the landed unit must be the selected one, else the state ends before the reactor turns or pays.
        var arena = BattleWalkTests.Make(edit: rules => Replaced(rules, "tuSnap: 20",
            "tuSnap: 20\n    meleePower: 30\n    costMelee: {time: 20}\n    accuracyMelee: 100\n    meleeType: 1\n    meleeAlter: {RandomType: 3}"));
        GunFor(arena.Battle, arena.Alien);
        arena.Stand(arena.Alien, 11, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        arena.Battle.SetTile(arena.Soldier, BattleWalkTests.At(10, 10, 2));
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        arena.See();
        arena.Battle.SelectedUnitId = selected ? arena.Soldier.Id : arena.Alien.Id;
        var units = arena.Alien.TimeUnits;
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(BattleWalkTests.At(10, 10, 0), arena.Soldier.Position);
        var events = arena.Battle.Events.Drain();
        Assert.Equal(selected, events.Any(static @event => @event is UnitMeleeAttacked));
        Assert.Equal(selected, arena.Alien.TimeUnits < units);
    }

    [Fact]
    public void ACheckForAUnitOfTheWrongSideOrOffTheMapDrawsNoReaction()
    {
        var arena = Make();
        // checkReactionFire reacts only to a unit of the side on turn: the alien is not.
        Assert.False(arena.Battle.CheckReactionFire(arena.Alien, new BattleReactionOrigin(BattleActionType.Kneel)));
        Assert.False(arena.Battle.HasPendingStates);
        // The soldier is of the side on turn and answered...
        Assert.True(arena.Battle.CheckReactionFire(arena.Soldier, new BattleReactionOrigin(BattleActionType.Kneel)));
        // ...but not once it has left the map.
        var gone = Make();
        gone.Soldier.Position = null;
        Assert.False(gone.Battle.CheckReactionFire(gone.Soldier, new BattleReactionOrigin(BattleActionType.Kneel)));
        Assert.False(gone.Battle.HasPendingStates);
        Assert.Equal(gone.Alien.Stats.TimeUnits, gone.Alien.TimeUnits);
    }

    [Fact]
    public void AUnitThatLandsInsideAWalkAndDrawsAReactionEndsTheWalkWhereItLanded()
    {
        // The soldier walks off a ledge onto the alien's tile (which steps aside), lands beside it and is shot at: UnitFallBState aborts the walk's path, so it does not go on.
        var (armed, unarmed) = (LedgeWalk(armed: true), LedgeWalk(armed: false));
        Assert.Contains(armed.Battle.Events.Drain(), static @event => @event is UnitFired);
        Assert.Equal(BattleWalkTests.At(12, 10, 0), unarmed.Soldier.Position);
        Assert.Equal(BattleWalkTests.At(11, 10, 0), armed.Soldier.Position);
    }

    private static Arena LedgeWalk(bool armed)
    {
        var arena = BattleWalkTests.Make();
        // (The alien below the ledge steps aside facing away from the soldier: a second one, armed, watches the landing from the east.)
        if (armed)
        {
            var watcher = SecondAlien(arena, 16, 10);
            Prepare(watcher, 300, watcher.Stats.TimeUnits);
        }
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(arena.Soldier, BattleWalkTests.At(10, 10, 1));
        arena.Stand(arena.Alien, 11, 10, 6);
        arena.See();
        // The soldier does not know of the alien below the ledge, so the walk steps off the ledge onto it.
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, false);
        Assert.Null(arena.Battle.Walk(arena.Soldier.Id, BattleWalkTests.At(12, 10, 0), BattleMoveType.Normal, false));
        return arena;
    }

    [Fact]
    public void ALandingAfterAnInventoryChangeRunsTheReactionItDrawsOnTheLiveBattle()
    {
        // What the inventory's close does (BattleRuntime.Applied, the loaded battle and the first turn call the same): the gate passes, and the live fall then answers the landing of
        // the selected unit. Nothing in a reaction state refuses (no state sets ExplosionRefusal), so the gate has nothing to miss.
        var arena = BattleWalkTests.Make();
        GunFor(arena.Battle, arena.Alien);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        arena.Battle.SetTile(arena.Soldier, BattleWalkTests.At(10, 10, 2));
        arena.Battle.SelectedUnitId = arena.Soldier.Id;
        arena.See();
        Assert.Null(arena.Battle.FallsRefusal(arena.Soldier));
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        arena.Battle.Events.Drain();
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(0, arena.Soldier.Position!.Value.Z);
        Assert.Contains(arena.Battle.Events.Drain(), @event => @event is UnitFired fired && fired.UnitId == arena.Alien.Id);
        Assert.Null(arena.Battle.TakeExplosionRefusal());
    }

    private sealed class CountingHooks : IBattleHooks
    {
        public int UnitActions { get; private set; }

        public BattleReactionHookValues ReactionUnitAction(in BattleReactionHookContext context, BattleReactionHookValues values)
        {
            UnitActions++;
            return values;
        }
    }

    private sealed class WeaponHooks : IBattleHooks
    {
        public int? Weapon { get; private set; }

        public BattleActionType? Original { get; private set; }

        public int? Target { get; private set; }

        public BattleReactionHookValues ReactionWeaponAction(in BattleReactionHookContext context, BattleReactionHookValues values)
        {
            (Weapon, Original, Target) = (context.OriginalWeapon?.Id, context.OriginalType, context.OriginalTarget?.Id);
            return values with { Chance = 0 };
        }
    }

    private sealed record HookCall(string Name, BattleReactionHookContext Context, BattleReactionHookValues Values);

    /// <summary>Records what the unit scripts are given and answers with a chance of 0.</summary>
    private sealed class RecordingHooks : IBattleHooks
    {
        public List<HookCall> Calls { get; } = [];

        public BattleReactionHookValues ReactionUnitAction(in BattleReactionHookContext context, BattleReactionHookValues values)
        {
            Calls.Add(new HookCall("unit-action", context, values));
            return values;
        }

        public BattleReactionHookValues ReactionUnitReaction(in BattleReactionHookContext context, BattleReactionHookValues values)
        {
            Calls.Add(new HookCall("unit-reaction", context, values));
            return values with { Chance = 0 };
        }
    }
}
