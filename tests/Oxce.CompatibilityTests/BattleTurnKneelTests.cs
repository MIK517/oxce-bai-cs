using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S5: turning and kneeling (<c>src/Battlescape/UnitTurnBState.cpp</c> <c>init</c> and <c>think</c>,
/// <c>BattlescapeGame::secondaryAction</c> and <c>kneel</c>, <c>TileEngine::getSpottingUnits</c> and <c>determineReactionType</c>). The arena is
/// the sight tests' one: the soldier at (10, 10) facing east, the alien five tiles east facing west, both seeing each other in daylight.
/// </summary>
public sealed class BattleTurnKneelTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";
    private const string AlienStats = "stats: {tu: 50, firing: 61}";

    private static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    /// <summary>A flat arena with a turn cost of 2, snap shots of 20 and aimed shots of 50 percent, and an alien that reacts fast.</summary>
    private static Arena Make(Func<string, string>? edit = null, int distance = 5)
    {
        string Edit(string rules)
        {
            rules = Replaced(rules, "  - type: SUIT\n", "  - type: SUIT\n    turnCost: 2\n");
            rules = Replaced(rules, AlienStats, "stats: {tu: 50, firing: 61, reactions: 100}");
            rules = Replaced(rules, GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50");
            return edit is null ? rules : edit(rules);
        }

        return Flat(distance: distance, edit: Edit);
    }

    private static BattlePosition At(int x, int y) => new(x, y, 0);

    private static T Event<T>(CampaignCommandResult result) where T : ICampaignEvent => Assert.IsType<T>(Assert.Single(result.Events));

    private static string Refusal(CampaignCommandResult result) => Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Code;

    private static CampaignCommandResult Run(Arena arena, ICampaignCommand command) => arena.Campaign.Execute(command);

    private static BattleItem Hold(Arena arena, BattleUnit unit, string type, string slot, bool special = false)
    {
        var item = new BattleItem(arena.Battle.TakeItemId(), type) { OwnerUnitId = unit.Id, Slot = special ? "" : slot, IsSpecialWeapon = special };
        arena.Battle.AddItem(item);
        if (special) unit.SpecialWeaponIds.Add(item.Id);
        return item;
    }

    /// <summary>Gives the alien a gun in its right hand, loaded with a clip when asked.</summary>
    private static BattleItem Arm(Arena arena, bool loaded = true)
    {
        var gun = Hold(arena, arena.Alien, "GUN", "STR_RIGHT_HAND");
        gun.IsWeaponWithAmmo = true;
        if (loaded)
        {
            var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
            arena.Battle.AddItem(clip);
            gun.AmmoSlots[0] = clip.Id;
        }
        return gun;
    }

    // ---- turning ----

    [Theory]
    [InlineData(0, 10, 5, 2)]
    [InlineData(1, 15, 5, 1)]
    [InlineData(3, 15, 15, 1)]
    [InlineData(4, 10, 15, 2)]
    [InlineData(5, 5, 15, 3)]
    [InlineData(6, 5, 10, 4)]
    [InlineData(7, 5, 5, 3)]
    public void ATurnTakesTheShortestWayAndPaysTheTurnCostForEachEighth(int direction, int x, int y, int eighths)
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var before = soldier.TimeUnits;
        // The alien is already known to the soldier, so turning away and back spots nothing new: the turn goes all the way.
        var turned = Event<UnitTurned>(Run(arena, new TurnUnit(soldier.Id, At(x, y))));
        Assert.Equal(new UnitTurned(soldier.Id, direction, eighths, 2 * eighths, BattleTurnStop.Arrived), turned);
        Assert.Equal((direction, BattleUnitStatus.Standing, before - 2 * eighths), (soldier.Direction, soldier.Status, soldier.TimeUnits));
        // The body's turn also takes the (absent) turret; the unit keeps its stance.
        Assert.Equal(direction, soldier.ToDirection);
        Assert.False(soldier.Kneeled);
    }

    [Fact]
    public void AUnitThatAlreadyFacesTheTileOrStandsOnItDoesNothing()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var (before, revision) = (soldier.TimeUnits, arena.Battle.Revision);
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 0, BattleTurnStop.NoTurn), Event<UnitTurned>(Run(arena, new TurnUnit(soldier.Id, At(15, 10)))));
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 0, BattleTurnStop.NoTurn), Event<UnitTurned>(Run(arena, new TurnUnit(soldier.Id, At(10, 10)))));
        // Any tile in the same eighth of the compass is the same direction.
        Assert.Equal(BattleTurnStop.NoTurn, Event<UnitTurned>(Run(arena, new TurnUnit(soldier.Id, At(20, 11)))).Stop);
        Assert.Equal((before, revision), (soldier.TimeUnits, arena.Battle.Revision));
    }

    [Fact]
    public void ATurnTowardsATileOutsideTheMapIsRefusedBeforeAnyChange()
    {
        var arena = Make();
        var before = arena.Soldier.TimeUnits;
        Assert.Equal("battle.turn-target-outside-map", Refusal(Run(arena, new TurnUnit(arena.Soldier.Id, new BattlePosition(-1, 10, 0)))));
        Assert.Equal("battle.turn-target-outside-map", Refusal(Run(arena, new TurnUnit(arena.Soldier.Id, new BattlePosition(10, 10, 2)))));
        Assert.Equal((before, 2), (arena.Soldier.TimeUnits, arena.Soldier.Direction));
    }

    [Fact]
    public void ATurnStopsWhenAStepShowsTheUnitANewUnit()
    {
        // The alien stands north of the soldier, who faces east and cannot see it; the first eighth (to the north east) brings it into view.
        var arena = Make();
        arena.Stand(arena.Alien, 10, 5, 6);
        arena.See();
        Assert.Empty(arena.Soldier.UnitsSpottedThisTurn);
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        var before = arena.Soldier.TimeUnits;
        var turned = Event<UnitTurned>(Run(arena, new TurnUnit(arena.Soldier.Id, At(10, 5))));
        Assert.Equal(new UnitTurned(arena.Soldier.Id, 1, 1, 2, BattleTurnStop.SpottedUnits), turned);
        Assert.Equal((1, BattleUnitStatus.Standing, before - 2), (arena.Soldier.Direction, arena.Soldier.Status, arena.Soldier.TimeUnits));
        Assert.Equal([arena.Alien.Id], arena.Soldier.UnitsSpottedThisTurn);
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.True(arena.Alien.IsSpottedBy(BattleFaction.Player));
        // Turning on, with the alien already spotted this turn, goes all the way to the target.
        Assert.Equal(new UnitTurned(arena.Soldier.Id, 0, 1, 2, BattleTurnStop.Arrived), Event<UnitTurned>(Run(arena, new TurnUnit(arena.Soldier.Id, At(10, 5)))));
    }

    [Fact]
    public void ATurnNextToAnArmedOpposingUnitCompletesBecauseATurnChecksNoReactions()
    {
        var arena = Make();
        Arm(arena);
        var turned = Event<UnitTurned>(Run(arena, new TurnUnit(arena.Soldier.Id, At(5, 10))));
        Assert.Equal(new UnitTurned(arena.Soldier.Id, 6, 4, 8, BattleTurnStop.Arrived), turned);
        // The soldier's sight follows each eighth: facing west it sees nothing of the alien in the east, who still sees the soldier.
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.True(Arena.Sees(arena.Alien, arena.Soldier));
    }

    [Fact]
    public void ATurnPaysForTheEighthsItCanAndStopsWhenTheNextOneIsTooDear()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        // 3 time units: one eighth of 2, then 1 left is not enough.
        soldier.TimeUnits = 3;
        var turned = Event<UnitTurned>(Run(arena, new TurnUnit(soldier.Id, At(5, 10))));
        Assert.Equal(new UnitTurned(soldier.Id, 3, 1, 2, BattleTurnStop.NotEnoughTimeUnits), turned);
        Assert.Equal((3, 1, BattleUnitStatus.Standing), (soldier.Direction, soldier.TimeUnits, soldier.Status));
        // Not even the first eighth: refused, nothing changes.
        var revision = arena.Battle.Revision;
        Assert.Equal("battle.turn-not-enough-time-units", Refusal(Run(arena, new TurnUnit(soldier.Id, At(5, 10)))));
        Assert.Equal((3, 1, revision), (soldier.Direction, soldier.TimeUnits, arena.Battle.Revision));
    }

    [Fact]
    public void ATurnRespectsTheReservedTimeUnitsAndStopsWhereTheyWouldBeUsed()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var gun = arena.Battle.InventoryOf(soldier).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");
        arena.Battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        var reserve = BattleActionCost.For(arena.Rules, BattleActionType.AimedShot, soldier, gun).Time;
        Assert.True(reserve > 0);
        // The reserve plus two eighths minus one: the first eighth fits, the second does not.
        soldier.TimeUnits = reserve + 3;
        var turned = Event<UnitTurned>(Run(arena, new TurnUnit(soldier.Id, At(5, 10))));
        Assert.Equal(new UnitTurned(soldier.Id, 3, 1, 2, BattleTurnStop.ReservedTimeUnits), turned);
        Assert.Equal((3, reserve + 1), (soldier.Direction, soldier.TimeUnits));
        // The first eighth already past the reserve: refused, nothing changes.
        var revision = arena.Battle.Revision;
        Assert.Equal("battle.turn-time-units-reserved", Refusal(Run(arena, new TurnUnit(soldier.Id, At(5, 10)))));
        Assert.Equal((3, reserve + 1, revision), (soldier.Direction, soldier.TimeUnits, arena.Battle.Revision));
    }

    [Fact]
    public void ATurretUnitTurnsItsTurretWithItsBody()
    {
        var arena = Make();
        arena.Soldier.TurretType = 2;
        arena.Soldier.DirectionTurret = 2;
        Event<UnitTurned>(Run(arena, new TurnUnit(arena.Soldier.Id, At(10, 15))));
        Assert.Equal((4, 4), (arena.Soldier.Direction, arena.Soldier.DirectionTurret));
    }

    [Fact]
    public void ATurnIsRefusedOutsideTheSelectionRulesAndForUnitsOutOrOfAnotherSide()
    {
        var arena = Make();
        var target = At(5, 10);
        Assert.Equal("battle.selection-unit-unknown", Refusal(Run(arena, new TurnUnit(arena.Alien.Id, target))));
        Assert.Equal("battle.selection-unit-unknown", Refusal(Run(arena, new TurnUnit(9999, target))));
        arena.Soldier.Status = BattleUnitStatus.Unconscious;
        Assert.Equal("battle.selection-unit-unknown", Refusal(Run(arena, new TurnUnit(arena.Soldier.Id, target))));
        arena.Soldier.Status = BattleUnitStatus.Standing;
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.Equal("battle.selection-unavailable", Refusal(Run(arena, new TurnUnit(arena.Soldier.Id, target))));
        Assert.Equal("battle.selection-unavailable", Refusal(Run(arena, new KneelUnit(arena.Soldier.Id))));
    }

    // ---- kneeling ----

    [Fact]
    public void KneelingCostsFourAndStandingUpEightAndEachSeesAgain()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var before = soldier.TimeUnits;
        Assert.Equal(new UnitKneeled(soldier.Id, true, 4), Event<UnitKneeled>(Run(arena, new KneelUnit(soldier.Id))));
        Assert.Equal((true, before - 4), (soldier.Kneeled, soldier.TimeUnits));
        Assert.Equal(new UnitKneeled(soldier.Id, false, 8), Event<UnitKneeled>(Run(arena, new KneelUnit(soldier.Id))));
        Assert.Equal((false, before - 12), (soldier.Kneeled, soldier.TimeUnits));
        Assert.True(Arena.Sees(soldier, arena.Alien));
    }

    [Fact]
    public void KneelingRefreshesTheKneelersTilesAndUnitsThroughUpdateSoldierInfosFullFieldOfView()
    {
        // The event update through the tile skips tiles (the reference leaves them to updateSoldierInfo's full update of the unit).
        var arena = Make();
        var visible = arena.Soldier.VisibleTiles.Count;
        Assert.True(visible > 0);
        arena.Soldier.ClearVisibleUnits();
        arena.Soldier.ClearVisibleTiles(arena.Map);
        Event<UnitKneeled>(Run(arena, new KneelUnit(arena.Soldier.Id)));
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.Equal(visible, arena.Soldier.VisibleTiles.Count);
        // The kneeling stance is what the sight sees from: the unit's height is the armor's kneeling height.
        Assert.Equal(14, arena.Soldier.HeightOf(arena.Rules));
    }

    [Fact]
    public void AKneelIsRefusedForAFloatingUnitAnArmorThatForbidsItAndMissingTimeUnits()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        soldier.Floating = true;
        Assert.Equal("battle.kneel-floating", Refusal(Run(arena, new KneelUnit(soldier.Id))));
        soldier.Floating = false;
        soldier.TimeUnits = 3;
        var revision = arena.Battle.Revision;
        Assert.Equal("battle.kneel-not-enough-time-units", Refusal(Run(arena, new KneelUnit(soldier.Id))));
        Assert.Equal((false, 3, revision), (soldier.Kneeled, soldier.TimeUnits, arena.Battle.Revision));
        soldier.TimeUnits = 4;
        Assert.Equal(4, Event<UnitKneeled>(Run(arena, new KneelUnit(soldier.Id))).TimeUnitsSpent);

        var forbidding = Make(rules => Replaced(rules, "  - type: SUIT\n", "  - type: SUIT\n    allowsKneeling: false\n"));
        Assert.Equal("battle.kneel-not-allowed", Refusal(Run(forbidding, new KneelUnit(forbidding.Soldier.Id))));
        Assert.False(forbidding.Soldier.Kneeled);
    }

    [Fact]
    public void AKneelRespectsTheReservedTimeUnitsUnlessKneelingDownIsReserved()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var gun = arena.Battle.InventoryOf(soldier).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");
        arena.Battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        var reserve = BattleActionCost.For(arena.Rules, BattleActionType.AimedShot, soldier, gun).Time;
        // Kneeling down must leave the reserve: 4 more than the unit has to spare is refused.
        soldier.TimeUnits = reserve + 3;
        Assert.Equal("battle.kneel-time-units-reserved", Refusal(Run(arena, new KneelUnit(soldier.Id))));
        soldier.TimeUnits = reserve + 4;
        Assert.Equal(4, Event<UnitKneeled>(Run(arena, new KneelUnit(soldier.Id))).TimeUnitsSpent);
        // Standing up also must: the reserve is the weapon's alone for a kneeling unit.
        soldier.TimeUnits = reserve + 7;
        Assert.Equal("battle.kneel-time-units-reserved", Refusal(Run(arena, new KneelUnit(soldier.Id))));
        soldier.TimeUnits = reserve + 8;
        Assert.Equal(8, Event<UnitKneeled>(Run(arena, new KneelUnit(soldier.Id))).TimeUnitsSpent);
        // With kneeling reserved a standing unit kneels without the check (its own reserve is the kneel), though it must have the time units.
        arena.Battle.KneelReserved = true;
        soldier.TimeUnits = 4;
        Assert.Equal(4, Event<UnitKneeled>(Run(arena, new KneelUnit(soldier.Id))).TimeUnitsSpent);
        Assert.Equal(0, soldier.TimeUnits);
    }

    [Fact]
    public void AKneelIsRefusedForAnArmedReactorInRangeAndCompletedWithoutOne()
    {
        var arena = Make();
        var (soldier, alien) = (arena.Soldier, arena.Alien);
        Assert.True(alien.ReactionScore >= soldier.ReactionScore);
        var gun = Arm(arena);
        var (before, revision) = (soldier.TimeUnits, arena.Battle.Revision);
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(Run(arena, new KneelUnit(soldier.Id))));
        Assert.Equal((false, before, revision), (soldier.Kneeled, soldier.TimeUnits, arena.Battle.Revision));

        // Without ammunition (no snap shot) it cannot react; with its time units gone it has no reaction score.
        var empty = Make();
        Arm(empty, loaded: false);
        Assert.Equal(4, Event<UnitKneeled>(Run(empty, new KneelUnit(empty.Soldier.Id))).TimeUnitsSpent);
        var spent = Make();
        Arm(spent);
        spent.Alien.TimeUnits = 0;
        Assert.Equal(4, Event<UnitKneeled>(Run(spent, new KneelUnit(spent.Soldier.Id))).TimeUnitsSpent);
        // A reactor that dozes (out), is over its out threshold, or is slower than the kneeler does not count either.
        foreach (var disable in new Action<BattleUnit>[] { unit => unit.Status = BattleUnitStatus.Unconscious, unit => unit.Respawn = true, unit => unit.TimeUnits = 1 })
        {
            var other = Make();
            Arm(other);
            disable(other.Alien);
            Assert.Equal(4, Event<UnitKneeled>(Run(other, new KneelUnit(other.Soldier.Id))).TimeUnitsSpent);
        }
        // A unit over its out threshold (no health left) is no spotter either; the casualty check stops commands before that, so ask the state.
        var dying = Make();
        Arm(dying);
        Assert.True(dying.Battle.HasPossibleReactor(dying.Rules, dying.Soldier));
        dying.Alien.Health = 0;
        Assert.False(dying.Battle.HasPossibleReactor(dying.Rules, dying.Soldier));
        Assert.NotNull(gun);
    }

    [Fact]
    public void AReactorBeyondTheViewDistanceOrWithoutSnapTimeUnitsDoesNotRefuseAKneel()
    {
        // 21 tiles away (beyond the 20 of the maximum view distance), armed: never a spotter.
        var far = Make(distance: 1);
        far.Battle.SetTile(far.Alien, new BattlePosition(29, 10, 0));
        far.Battle.SetTile(far.Soldier, new BattlePosition(8, 10, 0));
        Arm(far);
        Assert.Equal(4, Event<UnitKneeled>(Run(far, new KneelUnit(far.Soldier.Id))).TimeUnitsSpent);
        // 20 tiles away is in range.
        var edge = Make(distance: 1);
        edge.Battle.SetTile(edge.Alien, new BattlePosition(29, 10, 0));
        edge.Battle.SetTile(edge.Soldier, new BattlePosition(9, 10, 0));
        Arm(edge);
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(Run(edge, new KneelUnit(edge.Soldier.Id))));
        // A weapon whose snap shot costs nothing never gives a reaction.
        var free = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 0\n    tuAimed: 0"));
        Arm(free);
        Assert.Equal(4, Event<UnitKneeled>(Run(free, new KneelUnit(free.Soldier.Id))).TimeUnitsSpent);
        // A civilian never reacts to a soldier.
        var civilian = Make();
        Arm(civilian);
        civilian.Alien.Faction = BattleFaction.Neutral;
        Assert.Equal(4, Event<UnitKneeled>(Run(civilian, new KneelUnit(civilian.Soldier.Id))).TimeUnitsSpent);
    }

    [Fact]
    public void AMeleeWeaponWithItsHitPaidIsAPossibleReactorToo()
    {
        var arena = Make(rules => Replaced(rules, "  - type: RAM\n    battleType: 3\n    clipSize: -1", "  - type: RAM\n    battleType: 3\n    clipSize: -1\n    tuMelee: 30"));
        var ram = Hold(arena, arena.Alien, "RAM", "STR_LEFT_HAND");
        // A melee weapon needs its hit loaded: unloaded it is no reactor, loaded it is.
        ram.IsWeaponWithAmmo = true;
        Assert.Equal(4, Event<UnitKneeled>(Run(arena, new KneelUnit(arena.Soldier.Id))).TimeUnitsSpent);
        Event<UnitKneeled>(Run(arena, new KneelUnit(arena.Soldier.Id))); // stand again
        ram.AmmoSlots[0] = ram.Id;
        ram.AmmoQuantity = 1;
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(Run(arena, new KneelUnit(arena.Soldier.Id))));
    }

    [Fact]
    public void AWeaponlessUnitInRangeSpotsTheKneelerAndAUnitThatWasHitSeesItWhateverWayItFaces()
    {
        // An unarmed alien that sees the soldier lists it, and its faction has spotted it.
        var arena = Make();
        arena.Forget();
        Assert.Empty(arena.Alien.VisibleUnits);
        Event<UnitKneeled>(Run(arena, new KneelUnit(arena.Soldier.Id)));
        Assert.Equal([arena.Soldier.Id], arena.Alien.VisibleUnits);
        Assert.True(arena.Soldier.IsSpottedBy(BattleFaction.Hostile));
        Assert.Contains(arena.Soldier.Id, arena.Alien.UnitsSpottedThisTurn);

        // An alien that faces away does not see it, unless it was hit (a unit without an AI module reads its hit state).
        foreach (var hit in new[] { false, true })
        {
            var away = Make();
            away.Alien.SetDirection(2);
            away.Alien.Ai = null;
            away.Forget();
            away.Alien.HitState = hit;
            away.Battle.SpotMover(away.Rules, away.Battle.Sight(), away.Soldier);
            Assert.Equal(hit, away.Alien.VisibleUnits.Contains(away.Soldier.Id));
            Assert.Equal(hit, away.Soldier.IsSpottedBy(BattleFaction.Hostile));
        }
        // A unit with an AI module remembers who hit it.
        var remembering = Make();
        remembering.Alien.SetDirection(2);
        remembering.Alien.Ai ??= new BattleAiState(BattleFaction.Hostile);
        remembering.Forget();
        remembering.Battle.SpotMover(remembering.Rules, remembering.Battle.Sight(), remembering.Soldier);
        Assert.Empty(remembering.Alien.VisibleUnits);
        remembering.Alien.Ai!.WasHitBy.Add(remembering.Soldier.Id);
        remembering.Battle.SpotMover(remembering.Rules, remembering.Battle.Sight(), remembering.Soldier);
        Assert.Equal([remembering.Soldier.Id], remembering.Alien.VisibleUnits);
    }

    [Fact]
    public void ASpotterThatCannotSeeTheMoversTileOrIsOutOfTheFiltersDoesNotSpotIt()
    {
        // A wall between them stops the spotter's sight.
        var walled = Make();
        walled.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        walled.See();
        walled.Battle.SpotMover(walled.Rules, walled.Battle.Sight(), walled.Soldier);
        Assert.Empty(walled.Alien.VisibleUnits);
        Assert.False(walled.Soldier.IsSpottedBy(BattleFaction.Hostile));
        // A reaction score below the mover's filters it out.
        var slow = Make();
        slow.Forget();
        slow.Alien.TimeUnits = 1;
        slow.Battle.SpotMover(slow.Rules, slow.Battle.Sight(), slow.Soldier);
        Assert.Empty(slow.Alien.VisibleUnits);
        // The extended melee reaction rule 2 lets a unit that was melee attacked see its attacker from any side.
        var melee = Make(rules => rules + "\nextendedMeleeReactions: 2\n");
        melee.Alien.SetDirection(2);
        melee.Alien.Ai = null;
        melee.Forget();
        melee.Alien.MeleeAttackedBy.Add(melee.Soldier.Id);
        melee.Battle.SpotMover(melee.Rules, melee.Battle.Sight(), melee.Soldier);
        Assert.Equal([melee.Soldier.Id], melee.Alien.VisibleUnits);
        var plain = Make();
        plain.Alien.SetDirection(2);
        plain.Alien.Ai = null;
        plain.Forget();
        plain.Alien.MeleeAttackedBy.Add(plain.Soldier.Id);
        plain.Battle.SpotMover(plain.Rules, plain.Battle.Sight(), plain.Soldier);
        Assert.Empty(plain.Alien.VisibleUnits);
    }

    [Fact]
    public void TheExtendedMeleeReactionsRuleIsReadAndDefaultsToZero()
    {
        Assert.Equal(0, Make().Rules.Campaign.ExtendedMeleeReactions);
        Assert.Equal(2, Make(rules => rules + "\nextendedMeleeReactions: 2\n").Rules.Campaign.ExtendedMeleeReactions);
    }

    [Fact]
    public void TheSpottingThresholdIsTheMoversScoreCutToAnInteger()
    {
        var arena = Make();
        var (soldier, alien) = (arena.Soldier, arena.Alien);
        Arm(arena);
        soldier.Stats = soldier.Stats with { Reactions = 47 };
        // Find time units that give the soldier a fractional score and the alien (reactions 100 over 50 time units) a score from the integer
        // below it up to it: a spotter in the reference, which compares with the cut threshold.
        var found = false;
        for (var soldierUnits = 1; soldierUnits <= soldier.Stats.TimeUnits && !found; soldierUnits++)
        {
            soldier.TimeUnits = soldierUnits;
            var score = soldier.ReactionScore;
            for (var alienUnits = 20; alienUnits <= 50 && !found; alienUnits++)
            {
                alien.TimeUnits = alienUnits;
                found = alien.ReactionScore < score && alien.ReactionScore >= Math.Truncate(score);
            }
        }
        Assert.True(found);
        Assert.True(alien.ReactionScore < soldier.ReactionScore);
        Assert.True(arena.Battle.HasPossibleReactor(arena.Rules, soldier));
        // And its spotting happens: an unarmed one lists the soldier.
        var unarmed = Make();
        unarmed.Soldier.Stats = unarmed.Soldier.Stats with { Reactions = 47 };
        unarmed.Soldier.TimeUnits = soldier.TimeUnits;
        unarmed.Alien.TimeUnits = alien.TimeUnits;
        unarmed.Forget();
        unarmed.Battle.SpotMover(unarmed.Rules, unarmed.Battle.Sight(), unarmed.Soldier);
        Assert.Equal([unarmed.Soldier.Id], unarmed.Alien.VisibleUnits);
    }

    [Fact]
    public void AFirearmsButtCanReactInMeleeWhenItsSnapShotCannot()
    {
        // No snap shot at all, a hit that is paid for.
        var butt = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 0\n    tuAimed: 0\n    tuMelee: 20"));
        Arm(butt);
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(Run(butt, new KneelUnit(butt.Soldier.Id))));
        // A snap shot dearer than the time units left, a cheap hit.
        var dear = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 300\n    tuAimed: 50\n    tuMelee: 20"));
        Arm(dear);
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(Run(dear, new KneelUnit(dear.Soldier.Id))));
    }

    [Fact]
    public void APsiWeaponNeedsAPsiSkillToBeAReactor()
    {
        var arena = Make(rules => Replaced(rules, GunText + "\n    tuSnap: 20", GunText + "\n    psiRequired: true\n    tuSnap: 20"));
        Arm(arena);
        Assert.False(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
        arena.Alien.Stats = arena.Alien.Stats with { PsiSkill = 10 };
        Assert.True(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
    }

    [Fact]
    public void ASpecialWeaponCountsAsAReactionWeapon()
    {
        var arena = Make(rules => Replaced(rules, "  - type: RAM\n    battleType: 3\n    clipSize: -1", "  - type: RAM\n    battleType: 3\n    clipSize: -1\n    tuMelee: 30"));
        Assert.False(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
        var ram = Hold(arena, arena.Alien, "RAM", "", special: true);
        ram.IsWeaponWithAmmo = true;
        ram.AmmoSlots[0] = ram.Id;
        ram.AmmoQuantity = 1;
        Assert.True(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
    }

    [Fact]
    public void NobodyReactsOnTheNeutralSidesTurnAndTheSideOnTurnNeverReactsToItself()
    {
        var arena = Make();
        Arm(arena);
        Assert.True(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
        arena.Battle.Side = BattleFaction.Neutral;
        Assert.False(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
        // The alien's own side on turn: it is a friend of the side's mover, not a spotter.
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.False(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
        // A civilian reacts only to a hostile unit that the AI does not ignore.
        arena.Battle.Side = BattleFaction.Player;
        arena.Alien.Faction = BattleFaction.Neutral;
        Assert.False(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
        arena.Soldier.Faction = BattleFaction.Hostile;
        Assert.True(arena.Battle.HasPossibleReactor(arena.Rules, arena.Soldier));
    }
}
