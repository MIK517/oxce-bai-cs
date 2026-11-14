using Oxce.Formats.Terrain;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleDamageTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S8 (the first part): the throw (<c>ProjectileFlyBState</c>'s throw branches, <c>validThrowRange</c>, <c>BattleItem::fuseThrowEvent</c>), priming and unpriming
/// (<c>handleNonTargetAction</c>) and their menu lines, on the flat arena of the damage tests. The soldier (facing east, a throwing statistic of 100) holds a GRENADE of power
/// 80 with a throw cost of 25; the alien stands five tiles east. With every draw 0 the item lands where it is aimed.
/// </summary>
public sealed class BattleThrowingTests
{
    private const string Blast = "damageType: 3\ndamageAlter: {RandomType: 3}\npower: 80\ncostThrow: {time: 25}\ncostPrime: {time: 10}\ncostUnprime: {time: 5}\nunprimeActionName: STR_UNPRIME_GRENADE";

    private const string GrenadeText = "- type: GRENADE\n    battleType: 4\n    weight: 1";

    /// <summary>The soldier's grenade: <see cref="Blast"/> (or another base text, for a key it sets) and the extra keys; <paramref name="more"/> edits the whole ruleset.</summary>
    private static (Arena Arena, BattleItem Grenade) Load(string grenade = "", int distance = 5, string? blast = null, Func<string, string>? more = null,
        Action<Dictionary<string, byte[]>>? terrain = null)
    {
        var arena = Duel(grenade: (blast ?? Blast) + (grenade.Length == 0 ? "" : "\n" + grenade), more: more, terrain: terrain);
        arena.Stand(arena.Alien, 10 + distance, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Throwing = 100, Strength = 60 };
        arena.Soldier.TimeUnits = 100;
        foreach (var item in arena.Battle.InventoryOf(arena.Soldier).ToList()) arena.Battle.RemoveItem(item);
        var thrown = new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { OwnerUnitId = arena.Soldier.Id, Slot = "STR_RIGHT_HAND" };
        arena.Battle.AddItem(thrown);
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0);
        return (arena, thrown);
    }

    private static CampaignReason? Throw(Arena arena, BattleItem item, int x = 15, int y = 10, int z = 0) =>
        arena.Battle.ThrowAction(arena.Soldier.Id, item.Id, new BattlePosition(x, y, z), false);

    private static readonly BattleViewer Player = BattleViewer.LocalPlayer;

    private static BattleRuntime RuntimeOf(Arena arena) => new(arena.Battle, arena.Rules, new ScriptedDraws(0), null, static (_, _) => null);

    private static int Fuse(Arena arena, BattleItem item, int turns)
    {
        item.SetFuseTimer(turns, arena.Rules.Battle.Equipment.Items[item.ItemType]);
        return item.FuseTimer;
    }

    // ---- the throw ----

    [Fact]
    public void AThrownItemLeavesTheHandPaysTheCostAndLandsOnTheTargetTile()
    {
        var (arena, grenade) = Load();
        Assert.Null(Throw(arena, grenade));
        Assert.Equal(75, arena.Soldier.TimeUnits);
        Assert.Null(grenade.OwnerUnitId);
        Assert.Equal(arena.Soldier.Id, grenade.PreviousOwnerUnitId);
        Assert.Equal((15, 10, 0), (grenade.Tile!.Value.X, grenade.Tile.Value.Y, grenade.Tile.Value.Z));
        Assert.Equal(BattleState.GroundSlot, grenade.Slot);
        Assert.Contains(grenade, arena.Battle.Items);
        var fired = Assert.IsType<UnitFired>(Assert.Single(arena.Battle.Events.Drain()));
        Assert.Equal((arena.Soldier.Id, grenade.Id, BattleAimKind.Throw), (fired.UnitId, fired.ItemId, fired.Kind));
        Assert.NotEmpty(fired.Trajectory);
        Assert.Equal(100, arena.Alien.Health);
        // Vanilla gives throwing experience for any throw, and a throw is no shot.
        Assert.Equal(1, arena.Soldier.Experience.Throwing);
        Assert.Equal(0, arena.Soldier.Statistics.ShotsFiredCounter);
        Assert.Equal(0, arena.Alien.Statistics.ShotAtCounter);
        // An unprimed grenade does not explode, and the soldier did not aim.
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
    }

    [Fact]
    public void TheExtendedExperienceSystemGivesNoThrowingExperienceForTheThrowItself()
    {
        var arena = Duel(grenade: Blast, global: "constants:\n  extendedExperienceAwardSystem: true");
        arena.Stand(arena.Alien, 15, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Throwing = 100, Strength = 60 };
        arena.Soldier.TimeUnits = 100;
        foreach (var item in arena.Battle.InventoryOf(arena.Soldier).ToList()) arena.Battle.RemoveItem(item);
        var grenade = new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { OwnerUnitId = arena.Soldier.Id, Slot = "STR_RIGHT_HAND" };
        arena.Battle.AddItem(grenade);
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0);
        Assert.Null(Throw(arena, grenade));
        Assert.Equal(0, arena.Soldier.Experience.Throwing);
    }

    [Fact]
    public void AGrenadeWithAnInstantFuseExplodesWhereItLands()
    {
        var (arena, grenade) = Load("fuseType: -2");
        Fuse(arena, grenade, 0);
        Assert.Null(Throw(arena, grenade));
        var events = arena.Battle.Events.Drain();
        Assert.IsType<UnitFired>(events[0]);
        var blast = Assert.Single(events.OfType<BattleExploded>());
        Assert.Equal(80, blast.Power);
        Assert.False(blast.Terrain);
        Assert.True(arena.Alien.Health < 100);
        // The hot grenade is spent.
        Assert.DoesNotContain(grenade, arena.Battle.Items);
    }

    [Fact]
    public void ASettableFuseAtZeroIsNoInstantGrenadeUnlessTheTriggerSaysSo()
    {
        var (arena, grenade) = Load();
        Fuse(arena, grenade, 0);
        Assert.Null(Throw(arena, grenade));
        Assert.DoesNotContain(arena.Battle.Events.Drain(), static @event => @event is BattleExploded);
        Assert.Contains(grenade, arena.Battle.Items);
        Assert.Equal(0, grenade.FuseTimer);

        var (exploding, hot) = Load("fuseTriggerEvents: {throwTrigger: true, throwExplode: true}");
        // The throw trigger enables a fuse that runs, and throwExplode makes it blow up on landing.
        Fuse(exploding, hot, 0);
        Assert.False(hot.FuseEnabled);
        Assert.Null(Throw(exploding, hot));
        Assert.Single(exploding.Battle.Events.Drain().OfType<BattleExploded>());
        Assert.DoesNotContain(hot, exploding.Battle.Items);
    }

    [Fact]
    public void APrimedGrenadeThatLandsWhereAUnitStandsMakesADangerZoneAndOneThatLandsOnEmptyGroundDoesNot()
    {
        static IEnumerable<int> Marked(Arena arena) => Enumerable.Range(0, arena.Map.TileCount).Where(arena.Map.Dangerous);

        var (arena, grenade) = Load();
        Fuse(arena, grenade, 3);
        grenade.FuseEnabled = true;
        Assert.Null(Throw(arena, grenade));
        // The alien's tile and the ones its blast reaches.
        Assert.Contains(arena.Tile(15, 10), Marked(arena));
        Assert.Contains(arena.Tile(14, 10), Marked(arena));
        Assert.DoesNotContain(arena.Tile(10, 10), Marked(arena));

        var (empty, thrown) = Load();
        Fuse(empty, thrown, 3);
        thrown.FuseEnabled = true;
        Assert.Null(Throw(empty, thrown, 15, 14));
        Assert.Empty(Marked(empty));

        // A grenade with no live fuse makes none either, wherever it lands.
        var (cold, plain) = Load();
        Assert.Null(Throw(cold, plain));
        Assert.Empty(Marked(cold));
    }

    [Fact]
    public void AThrowTriggerWithoutAFuseTypeStartsTheFuseAndAnItemThatIsNoGrenadeIsRemoved()
    {
        // A non-grenade (battle type 0) with throwExplode and no fuse type: the throw starts its fuse at 0 and it vanishes, with no blast.
        var (arena, item) = Load("fuseType: -3\nfuseTriggerEvents: {throwTrigger: true, throwExplode: true}",
            more: static rules => Replaced(rules, GrenadeText, "- type: GRENADE\n    battleType: 0\n    weight: 1"));
        Assert.Null(Throw(arena, item));
        var events = arena.Battle.Events.Drain();
        Assert.DoesNotContain(events, static @event => @event is BattleExploded);
        Assert.True(item.FuseEnabled);
        Assert.Equal(0, item.FuseTimer);
        Assert.DoesNotContain(item, arena.Battle.Items);
    }

    [Fact]
    public void ASpecialChanceOfZeroKeepsAHotItemFromExploding()
    {
        var (arena, grenade) = Load("fuseType: -2\nspecialChance: 0");
        arena.Battle.Random = new ScriptedDraws(50);
        Fuse(arena, grenade, 0);
        Assert.Null(Throw(arena, grenade));
        Assert.DoesNotContain(arena.Battle.Events.Drain(), static @event => @event is BattleExploded);
        Assert.Contains(grenade, arena.Battle.Items);
    }

    // ---- the refusals ----

    [Fact]
    public void ATargetPastTheThrowRangeIsRefusedAndNothingIsPaid()
    {
        var (arena, grenade) = Load("throwRange: 3");
        var reason = Throw(arena, grenade);
        Assert.Equal(("battle.attack-refused", "STR_OUT_OF_RANGE"), (reason!.Code, reason.TextKey));
        Assert.Equal(100, arena.Soldier.TimeUnits);
        Assert.Equal(arena.Soldier.Id, grenade.OwnerUnitId);
        Assert.Empty(arena.Battle.Events.Drain());
    }

    [Fact]
    public void ABlockedTargetIsNoPlaceToThrowAndCostsNothing()
    {
        var (arena, grenade) = Load();
        arena.Put(15, 10, 0, BattleTilePart.ObjectPart, Wall);
        var reason = Throw(arena, grenade);
        Assert.Equal(("battle.attack-refused", "STR_UNABLE_TO_THROW_HERE"), (reason!.Code, reason.TextKey));
        Assert.Equal(100, arena.Soldier.TimeUnits);
        Assert.Equal(arena.Soldier.Id, grenade.OwnerUnitId);
        Assert.Equal(0, arena.Soldier.Experience.Throwing);
    }

    /// <summary>The test set with the stair floor (a terrain level of -24) given a full loft in every layer: a solid block of 24 voxels, as a real full-height floor is.</summary>
    private static void SolidStairs(Dictionary<string, byte[]> files)
    {
        Terrain(files);
        var bytes = files["TERRAIN/TESTSET.MCD"];
        for (var offset = 8; offset < 20; offset++) bytes[StairFloor * McdTerrainCodec.RecordSize + offset] = 1;
    }

    [Fact]
    public void AnItemThrownAtATileWithAFloorOf24LandsOnTheLevelAbove()
    {
        // ProjectileFlyBState.cpp:158-163: the target rises one level, so the arc ends on top of the block (a block with no loft would let the arc fall through it).
        // The lift is seen by the checks of the target tile: a solid wall standing on the block would be a blocked target, but the tile above is clear.
        var (arena, grenade) = Load(terrain: SolidStairs);
        arena.Put(15, 10, 0, BattleTilePart.Floor, StairFloor);
        arena.Put(15, 10, 0, BattleTilePart.ObjectPart, SolidWall);
        arena.See();
        Assert.Null(Throw(arena, grenade));
        Assert.Equal((15, 10, 1), (grenade.Tile!.Value.X, grenade.Tile.Value.Y, grenade.Tile.Value.Z));
        Assert.Equal(75, arena.Soldier.TimeUnits);
    }

    [Fact]
    public void AFloorOf24OnTheTopLevelIsNotLiftedAndANormalOneIsNot()
    {
        // The guard `z + 1 < map height`: nothing is lifted past the map, so the throw lands on its own level (the floor of 24 on the top level) or is refused.
        var (arena, grenade) = Load(terrain: SolidStairs);
        arena.Put(15, 10, 1, BattleTilePart.Floor, StairFloor);
        arena.See();
        var reason = Throw(arena, grenade, z: 1);
        Assert.Equal("STR_UNABLE_TO_THROW_HERE", reason!.TextKey);
        Assert.Equal(100, arena.Soldier.TimeUnits);
        // A tile with an ordinary floor keeps its level.
        var (flat, other) = Load();
        Assert.Null(Throw(flat, other));
        Assert.Equal(0, other.Tile!.Value.Z);
    }

    [Fact]
    public void NotEnoughTimeUnitsRefusesAndAnItemWithoutAThrowCostOrAFixedOneIsNotOffered()
    {
        var (arena, grenade) = Load();
        arena.Soldier.TimeUnits = 20;
        var tooPoor = Throw(arena, grenade)!;
        Assert.Equal(("battle.attack-refused", "STR_NOT_ENOUGH_TIME_UNITS"), (tooPoor.Code, tooPoor.TextKey));
        Assert.Equal(20, arena.Soldier.TimeUnits);

        var (fixedArena, fixedItem) = Load("fixedWeapon: true");
        Assert.Equal("battle.attack-action-not-offered", Throw(fixedArena, fixedItem)!.Code);
        var (noCost, plain) = Load(blast: Blast.Replace("costThrow: {time: 25}", "costThrow: {time: 0}", StringComparison.Ordinal));
        Assert.Equal("battle.attack-action-not-offered", Throw(noCost, plain)!.Code);
    }

    [Fact]
    public void ANotHeldItemAndATargetOffTheMapAreRefusedBeforeAnything()
    {
        var (arena, grenade) = Load();
        Assert.Equal("battle.attack-target-outside", Throw(arena, grenade, 99, 10)!.Code);
        grenade.Slot = "STR_BACK_PACK";
        Assert.Equal("battle.attack-weapon-unavailable", Throw(arena, grenade)!.Code);
    }

    [Fact]
    public void AThrowTurnsTheUnitFirstAndStopsWhenItCannotPayForTheThrowAfterwards()
    {
        var (arena, grenade) = Load();
        arena.Stand(arena.Alien, 10, 5, 6);
        arena.See();
        arena.Soldier.TimeUnits = arena.Soldier.TurnCost(arena.Rules) * 2 + 20;
        var result = RuntimeOf(arena).Execute(new Attack(arena.Soldier.Id, grenade.Id, BattleAimKind.Throw, Target: new BattlePosition(10, 5, 0)));
        // The turn took its time units, and the throw (25) could not be paid: the action stops with the message.
        Assert.Equal([typeof(UnitTurned), typeof(UnitAttackStopped)], Events(result).Select(static @event => @event.GetType()));
        Assert.Equal(new UnitAttackStopped(arena.Soldier.Id, grenade.Id, BattleAimKind.Throw, "STR_NOT_ENOUGH_TIME_UNITS"), Events(result)[1]);
        Assert.Equal(arena.Soldier.Id, grenade.OwnerUnitId);
    }

    // ---- the command ----

    [Fact]
    public void TheAttackCommandThrowsAndASecondCommandIsRefusedWithoutFaultingTheBattle()
    {
        var (arena, grenade) = Load();
        var runtime = RuntimeOf(arena);
        var result = runtime.Execute(new Attack(arena.Soldier.Id, grenade.Id, BattleAimKind.Throw, Target: new BattlePosition(15, 10, 0)));
        Assert.IsType<UnitFired>(Assert.Single(Events(result)));
        Assert.True(runtime.Revision > 0);
        var revision = runtime.Revision;
        Assert.Equal("battle.attack-weapon-unavailable", Refusal(runtime.Execute(new Attack(arena.Soldier.Id, grenade.Id, BattleAimKind.Throw, Target: new BattlePosition(15, 10, 0)))));
        Assert.Equal(revision, runtime.Revision);
        Assert.Equal("battle.attack-kind-is-prime-command", Refusal(runtime.Execute(new Attack(arena.Soldier.Id, grenade.Id, BattleAimKind.Prime))));
        Assert.Null(runtime.Fault);
    }

    // ---- priming ----

    [Fact]
    public void PrimingASettableFuseTakesTheChosenTurnsPaysAndRaisesTheEvent()
    {
        var (arena, grenade) = Load();
        var runtime = RuntimeOf(arena);
        var result = runtime.Execute(new PrimeItem(arena.Soldier.Id, grenade.Id, 3));
        Assert.Equal(new ItemPrimed(arena.Soldier.Id, grenade.Id, 3), Assert.Single(Events(result)));
        Assert.Equal((3, 90), (grenade.FuseTimer, arena.Soldier.TimeUnits));
        Assert.True(grenade.FuseEnabled);
        Assert.True(runtime.Revision > 0);
    }

    [Fact]
    public void AFixedFuseIgnoresTheTurnsAndTheTurnsOfASettableOneAreChecked()
    {
        var (fixedFuse, grenade) = Load("fuseType: 2");
        Assert.IsType<ItemPrimed>(Assert.Single(Events(RuntimeOf(fixedFuse).Execute(new PrimeItem(fixedFuse.Soldier.Id, grenade.Id, 9)))));
        Assert.Equal(2, grenade.FuseTimer);

        var (instant, instantGrenade) = Load("fuseType: -2");
        RuntimeOf(instant).Execute(new PrimeItem(instant.Soldier.Id, instantGrenade.Id, 9));
        Assert.Equal(0, instantGrenade.FuseTimer);

        var (settable, set) = Load();
        var runtime = RuntimeOf(settable);
        Assert.Equal("battle.prime-turns-invalid", Refusal(runtime.Execute(new PrimeItem(settable.Soldier.Id, set.Id, 24))));
        Assert.Equal("battle.prime-turns-invalid", Refusal(runtime.Execute(new PrimeItem(settable.Soldier.Id, set.Id, -1))));
        Assert.Equal((-1, 100), (set.FuseTimer, settable.Soldier.TimeUnits));
        Assert.IsType<ItemPrimed>(Assert.Single(Events(runtime.Execute(new PrimeItem(settable.Soldier.Id, set.Id, 23)))));
        Assert.Equal(23, set.FuseTimer);
    }

    [Fact]
    public void UnprimingNeedsAPrimedItemAnUnprimeNameAndTheTimeUnits()
    {
        var (arena, grenade) = Load();
        var runtime = RuntimeOf(arena);
        Assert.Equal("battle.prime-action-not-offered", Refusal(runtime.Execute(new UnprimeItem(arena.Soldier.Id, grenade.Id))));
        runtime.Execute(new PrimeItem(arena.Soldier.Id, grenade.Id, 2));
        // A primed item offers no priming again.
        Assert.Equal("battle.prime-action-not-offered", Refusal(runtime.Execute(new PrimeItem(arena.Soldier.Id, grenade.Id, 2))));
        var result = runtime.Execute(new UnprimeItem(arena.Soldier.Id, grenade.Id));
        Assert.Equal(new ItemUnprimed(arena.Soldier.Id, grenade.Id), Assert.Single(Events(result)));
        Assert.Equal((-1, 85, false), (grenade.FuseTimer, arena.Soldier.TimeUnits, grenade.FuseEnabled));

        var (nameless, plain) = Load(blast: Blast.Replace("\nunprimeActionName: STR_UNPRIME_GRENADE", "", StringComparison.Ordinal));
        var other = RuntimeOf(nameless);
        other.Execute(new PrimeItem(nameless.Soldier.Id, plain.Id, 2));
        Assert.Equal("battle.prime-action-not-offered", Refusal(other.Execute(new UnprimeItem(nameless.Soldier.Id, plain.Id))));
        Assert.Equal(2, plain.FuseTimer);

        var (poor, short_) = Load();
        poor.Soldier.TimeUnits = 9;
        var refusal = RuntimeOf(poor).Execute(new PrimeItem(poor.Soldier.Id, short_.Id, 1));
        Assert.Equal("battle.attack-refused", Refusal(refusal));
        Assert.Equal((-1, 9), (short_.FuseTimer, poor.Soldier.TimeUnits));
    }

    [Fact]
    public void ThePrimingLinesComeBeforeTheShotsAndNeedTheirCostsAndNames()
    {
        var (arena, grenade) = Load();
        var menu = arena.Battle.AimMenu(BattleResearchGate.Everything, Player, arena.Soldier.Id, grenade.Id)!;
        Assert.Equal([BattleAimKind.Throw, BattleAimKind.Prime], menu.Items.Select(static item => item.Kind));
        Assert.Equal(("STR_PRIME_GRENADE", 0, 10), (menu.Items[1].Name, menu.Items[1].Accuracy, menu.Items[1].TimeUnits));
        Fuse(arena, grenade, 3);
        menu = arena.Battle.AimMenu(BattleResearchGate.Everything, Player, arena.Soldier.Id, grenade.Id)!;
        Assert.Equal([BattleAimKind.Throw, BattleAimKind.Unprime], menu.Items.Select(static item => item.Kind));
        Assert.Equal(("STR_UNPRIME_GRENADE", 5), (menu.Items[1].Name, menu.Items[1].TimeUnits));

        // No prime cost, no priming line; an item with no fuse offers neither.
        var (free, noCost) = Load(blast: Blast.Replace("costPrime: {time: 10}", "costPrime: {time: 0}", StringComparison.Ordinal));
        Assert.Equal([BattleAimKind.Throw], free.Battle.AimMenu(BattleResearchGate.Everything, Player, free.Soldier.Id, noCost.Id)!.Items.Select(static item => item.Kind));
        var (flat, noFuse) = Load("fuseType: -3");
        Assert.DoesNotContain(flat.Battle.AimMenu(BattleResearchGate.Everything, Player, flat.Soldier.Id, noFuse.Id)!.Items, static item => item.Kind is BattleAimKind.Prime or BattleAimKind.Unprime);
    }

    [Fact]
    public void TheDefaultFuseOfEachFuseType()
    {
        var (arena, _) = Load();
        var rule = arena.Rules.Battle.Equipment.Items["GRENADE"];
        Assert.Equal(0, BattleState.FuseTimerDefault(rule with { FuseType = -1 }));
        Assert.Equal(0, BattleState.FuseTimerDefault(rule with { FuseType = -2 }));
        Assert.Equal(-1, BattleState.FuseTimerDefault(rule with { FuseType = -3 }));
        Assert.Equal(5, BattleState.FuseTimerDefault(rule with { FuseType = 5 }));
        Assert.Equal(63, BattleState.FuseTimerDefault(rule with { FuseType = 63 }));
        Assert.Equal(-1, BattleState.FuseTimerDefault(rule with { FuseType = 64 }));
    }

    // ---- the audit's findings (S8a) ----

    [Fact]
    public void AFixedFuseOfThirtyTurnsIsPrimedForThirtyAndAnUnknownFuseTypeHasNoDefaultToPrimeWith()
    {
        var (arena, grenade) = Load("fuseType: 30");
        var result = RuntimeOf(arena).Execute(new PrimeItem(arena.Soldier.Id, grenade.Id, 5));
        Assert.Equal(new ItemPrimed(arena.Soldier.Id, grenade.Id, 30), Assert.Single(Events(result)));
        Assert.Equal((30, 90), (grenade.FuseTimer, arena.Soldier.TimeUnits));

        // A fuse type past 63 is read as -1 by the reference: nothing is paid and nothing is primed.
        var (unknown, odd) = Load("fuseType: 70");
        Assert.Equal("battle.prime-fuse-type-unknown", Refusal(RuntimeOf(unknown).Execute(new PrimeItem(unknown.Soldier.Id, odd.Id, 5))));
        Assert.Equal((-1, 100), (odd.FuseTimer, unknown.Soldier.TimeUnits));
    }

    [Fact]
    public void TheUnprimeCostIsTheRulesAndTheMenuAloneAsksForTheName()
    {
        var (arena, _) = Load(blast: Blast.Replace("\nunprimeActionName: STR_UNPRIME_GRENADE", "", StringComparison.Ordinal));
        // getCostUnprime has no name check (the inventory's unpriming and the drop of primed items read it as it is).
        Assert.Equal(5, arena.Rules.Battle.Equipment.Items["GRENADE"].ActionCosts.Of(RuntimeItemActionCosts.Unprime).Cost.Time);
        Assert.Equal(5, Load().Arena.Rules.Battle.Equipment.Items["GRENADE"].ActionCosts.Of(RuntimeItemActionCosts.Unprime).Cost.Time);
    }

    [Fact]
    public void AThrownFlareLightsItsLandingTileAndTheThrowersTileGoesDark()
    {
        var (arena, flare) = Load(blast: "power: 8\ncostThrow: {time: 25}", more: static rules => Replaced(Replaced(rules, GrenadeText, "- type: GRENADE\n    battleType: 10\n    weight: 1"),
            "shade: 0\n    width: 30", "shade: 15\n    width: 30"));
        var soldierTile = new BattlePosition(10, 10, 0);
        var landing = new BattlePosition(15, 10, 0);
        // In the hand the flare lights the unit's tile.
        Assert.True(arena.Battle.TileShade(soldierTile) < 15);
        Assert.Equal(7, arena.Battle.TileShade(soldierTile));
        Assert.Equal(12, arena.Battle.TileShade(landing));
        Assert.Null(Throw(arena, flare));
        // Only the landed flare lights now, five tiles from the thrower's tile.
        Assert.Equal(12, arena.Battle.TileShade(soldierTile));
        Assert.Equal(7, arena.Battle.TileShade(landing));
        Assert.Equal(landing, flare.Tile);
    }

    [Fact]
    public void AHostileUnitsThrowPrimesAGrenadeOrProximityGrenadeWithTheDefaultFuseAndNothingElse()
    {
        foreach (var (battleType, expected) in new[] { (4, 0), (5, 0), (0, -1) })
        {
            var (arena, item) = Load(more: battleType == 4 ? null : rules => Replaced(rules, GrenadeText, $"- type: GRENADE\n    battleType: {battleType}\n    weight: 1"));
            arena.Battle.Side = BattleFaction.Hostile;
            arena.Soldier.Faction = BattleFaction.Hostile;
            // The selection commands are X-COM's turn only (the AI's throw is branch 6's), so the hostile unit's throw runs through the projectile action itself.
            var run = typeof(BattleState).GetMethod("RunProjectileAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var action = new BattleAimAction(arena.Soldier, BattleActionType.Throw, item, new BattlePosition(15, 10, 0));
            Assert.Null(run.Invoke(arena.Battle, [arena.Soldier, item, BattleAimKind.Throw, action]));
            Assert.Equal(expected, item.FuseTimer);
        }

        // X-COM's own throw leaves the fuse for the player to set.
        var (player, own) = Load();
        Assert.Null(Throw(player, own));
        Assert.Equal(-1, own.FuseTimer);
    }

    [Fact]
    public void PrimingNeedsTheResearchTheWeaponsPsiSkillAndManaFeatureAndTheUnprimeItsTimeUnits()
    {
        var researched = new BattleResearchGate(static id => id == "RES_GUN", false);
        var nothing = new BattleResearchGate(static _ => false, false);
        var (arena, grenade) = Load("requires: [RES_GUN]", more: static rules => rules + "\nresearch:\n  - name: RES_GUN\n    cost: 10\n");
        var unresearched = arena.Battle.PrimeAction(nothing, arena.Soldier.Id, grenade.Id, true, 3, out _)!;
        Assert.Equal(("battle.attack-refused", "STR_UNABLE_TO_USE_ALIEN_ARTIFACT_UNTIL_RESEARCHED"), (unresearched.Code, unresearched.TextKey));
        Assert.Equal((-1, 100), (grenade.FuseTimer, arena.Soldier.TimeUnits));
        Assert.Null(arena.Battle.PrimeAction(researched, arena.Soldier.Id, grenade.Id, true, 3, out _));
        // The unpriming asks the same of the weapon.
        Assert.Equal("battle.attack-refused", arena.Battle.PrimeAction(nothing, arena.Soldier.Id, grenade.Id, false, 0, out _)!.Code);
        Assert.Equal(3, grenade.FuseTimer);

        var (psi, psiGrenade) = Load("psiRequired: true");
        psi.Soldier.Stats = psi.Soldier.Stats with { PsiSkill = 0 };
        Assert.Equal("battle.prime-action-not-offered", Refusal(RuntimeOf(psi).Execute(new PrimeItem(psi.Soldier.Id, psiGrenade.Id, 3))));
        Assert.DoesNotContain(psi.Battle.AimMenu(BattleResearchGate.Everything, Player, psi.Soldier.Id, psiGrenade.Id)!.Items, static item => item.Kind == BattleAimKind.Prime);
        psi.Soldier.Stats = psi.Soldier.Stats with { PsiSkill = 20 };
        Assert.IsType<ItemPrimed>(Assert.Single(Events(RuntimeOf(psi).Execute(new PrimeItem(psi.Soldier.Id, psiGrenade.Id, 3)))));

        // The mana feature is off by default.
        var (mana, manaGrenade) = Load("manaRequired: true");
        Assert.Equal("battle.prime-action-not-offered", Refusal(RuntimeOf(mana).Execute(new PrimeItem(mana.Soldier.Id, manaGrenade.Id, 3))));

        var (poor, held) = Load();
        var runtime = RuntimeOf(poor);
        runtime.Execute(new PrimeItem(poor.Soldier.Id, held.Id, 3));
        poor.Soldier.TimeUnits = 4;
        Assert.Equal("battle.attack-refused", Refusal(runtime.Execute(new UnprimeItem(poor.Soldier.Id, held.Id))));
        Assert.Equal((3, 4), (held.FuseTimer, poor.Soldier.TimeUnits));
    }
}
