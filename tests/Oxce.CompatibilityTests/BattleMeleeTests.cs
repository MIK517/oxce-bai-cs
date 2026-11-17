using Oxce.Core.Random;
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
/// Phase 7 branch 5 S6: melee (<c>MeleeAttackBState</c>, the melee part of <c>ExplosionBState</c>, <c>TileEngine::meleeAttack</c>,
/// <c>meleeAttackCalculate</c>, <c>validMeleeRange</c>, <c>validTerrainMeleeRange</c>) and the attack command, on the flat arena of the damage tests.
/// The soldier holds a GUN whose butt is the weapon: 30 melee power as a flat roll, a cost of 20 time units and an accuracy of 100 percent.
/// The alien (100 health, facing west) stands next to it by default. A <see cref="ScriptedDraws"/> source orders the draws: the first one a hit
/// makes is the to-hit roll (0 to 99).
/// </summary>
public sealed class BattleMeleeTests
{
    private const string Butt = "meleePower: 30\ncostMelee: {time: 20}\naccuracyMelee: 100\nmeleeType: 1\nmeleeAlter: {RandomType: 3}";

    internal static Arena Arm(string gun = Butt, string alienExtra = "", string soldierExtra = "", int distance = 1, string global = "", Func<string, string>? more = null)
    {
        var arena = Duel(gun: gun, alienExtra: alienExtra, soldierExtra: soldierExtra, global: global, more: more);
        arena.Stand(arena.Alien, 10 + distance, 10, 6);
        arena.See();
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Melee = 100 };
        arena.Soldier.TimeUnits = 100;
        arena.Battle.Random = new ScriptedDraws(0);
        return arena;
    }

    private static BattleItem Gun(Arena arena, BattleUnit? unit = null) => Shot(arena, unit).Gun;

    private static CampaignReason? Hit(Arena arena, BattleItem gun, bool alt = false, BattleUnit? unit = null, BattleResearchGate? research = null) =>
        arena.Battle.MeleeAttackAction(research ?? BattleResearchGate.Everything, (unit ?? arena.Soldier).Id, gun.Id, alt);

    private sealed class Recorder : IBattleHooks
    {
        public List<string> Calls { get; } = [];

        public int ItemResult { get; set; } = int.MinValue;

        public int UnitResult { get; set; } = int.MinValue;

        public int TryMeleeAttackItem(in BattleAttack attack, BattleUnit victim, int attackStrength, int defenseStrength, int roll, int arc, int defenseStrengthPenalty, int result)
        {
            Calls.Add($"item {attackStrength} {defenseStrength} {roll} {arc} {defenseStrengthPenalty} {result}");
            return ItemResult == int.MinValue ? result : ItemResult;
        }

        public int TryMeleeAttackUnit(in BattleAttack attack, BattleUnit victim, int attackStrength, int defenseStrength, int result)
        {
            Calls.Add($"unit {result}");
            return UnitResult == int.MinValue ? result : UnitResult;
        }

        public int StatBonus(BattleStatBonusKind kind, BattleUnit? unit, BattleItem? item, int value)
        {
            if (kind is BattleStatBonusKind.MeleeBonus or BattleStatBonusKind.MeleeDodge) Calls.Add($"{kind} {value}");
            return kind == BattleStatBonusKind.MeleeBonus ? value + 5 : value;
        }
    }

    // ---- a hit and a miss ----

    [Fact]
    public void AHitStrikesTheUnitInFrontPaysTheCostAndPutsTheWeaponDown()
    {
        var arena = Arm();
        var gun = Gun(arena);
        Assert.Null(Hit(arena, gun));
        Assert.Equal(80, arena.Soldier.TimeUnits);
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
        var events = arena.Battle.Events.Drain();
        Assert.Equal(new UnitMeleeAttacked(arena.Soldier.Id, gun.Id, arena.Alien.Id, new BattlePosition(11 * 16 + 8 - 1, 10 * 16 + 8, 11), 0, true), events[0]);
        Assert.Equal(new UnitHit(arena.Alien.Id, arena.Soldier.Id, 100 - arena.Alien.Health, arena.Alien.Health), events[1]);
        Assert.True(arena.Alien.Health < 100);
        // The unit that was struck remembers who did it.
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
    }

    [Fact]
    public void TheHitPowerIsTheMeleePowerWithTheBonusAndTheItemsDamageType()
    {
        // 30 melee power, a flat roll, and the damage type the butt names (armor piercing here, whose health share is the whole damage).
        var arena = Arm(gun: Butt + "\nmeleeBonus: {flatOne: 2}");
        var gun = Gun(arena);
        var weak = Arm();
        var weakGun = Gun(weak);
        Hit(arena, gun);
        Hit(weak, weakGun);
        var strong = 100 - arena.Alien.Health;
        var normal = 100 - weak.Alien.Health;
        Assert.True(normal > 0);
        Assert.True(strong > normal, $"{strong} {normal}");

        // The butt's own damage type (meleeType) strikes, not the item's gun type (damageType): a stun melee type hurts no health, whatever damageType says.
        var stun = Arm(gun: Butt.Replace("meleeType: 1", "meleeType: 6", StringComparison.Ordinal) + "\ndamageType: 1\npower: 30\ndamageAlter: {RandomType: 3}");
        Hit(stun, Gun(stun));
        Assert.Equal(100, stun.Alien.Health);
        Assert.True(stun.Alien.StunLevel > 0);
        var health = Arm(gun: Butt + "\ndamageType: 6\npower: 30\ndamageAlter: {RandomType: 3}");
        Hit(health, Gun(health));
        Assert.True(health.Alien.Health < 100);
    }

    [Fact]
    public void AMissStillPaysAndHurtsNobodyAndPutsTheWeaponDown()
    {
        var arena = Arm(gun: Butt.Replace("accuracyMelee: 100", "accuracyMelee: 50", StringComparison.Ordinal));
        arena.Battle.Random = new ScriptedDraws(99);
        var gun = Gun(arena);
        Assert.Null(Hit(arena, gun));
        var events = arena.Battle.Events.Drain();
        Assert.Equal(new UnitMeleeAttacked(arena.Soldier.Id, gun.Id, arena.Alien.Id, new BattlePosition(11 * 16 + 7, 10 * 16 + 8, 11), 0, false), Assert.Single(events));
        Assert.Equal((100, 80, BattleUnitStatus.Standing), (arena.Alien.Health, arena.Soldier.TimeUnits, arena.Soldier.Status));
    }

    [Fact]
    public void TheRollAgainstTheAccuracyDecidesTheHit()
    {
        var arena = Arm(gun: Butt.Replace("accuracyMelee: 100", "accuracyMelee: 60", StringComparison.Ordinal));
        var gun = Gun(arena);
        var accuracy = arena.Battle.FiringAccuracy(arena.Soldier, gun, BattleActionType.Hit);
        Assert.Equal(60, accuracy);
        var attack = new BattleAttack(BattleActionType.Hit, arena.Soldier, gun, gun);
        foreach (var (roll, hits) in new[] { (0, true), (accuracy - 1, true), (accuracy, false), (99, false) })
        {
            arena.Battle.Random = new ScriptedDraws(roll);
            Assert.Equal(hits, arena.Battle.MeleeAttackCalculate(attack, arena.Alien) > 0);
        }
        Assert.Equal(0, arena.Battle.MeleeAttackCalculate(attack, null));
        Assert.True(arena.Battle.MeleeAttack(attack, arena.Alien, 3));
    }

    // ---- the dodge ----

    [Fact]
    public void TheVictimsDodgeLowersTheChanceAndFromBehindItDropsByTheArmorsPenalty()
    {
        var arena = Arm(alienExtra: "    meleeDodge:\n      flatOne: 40\n    meleeDodgeBackPenalty: 1\n");
        var gun = Gun(arena);
        var attack = new BattleAttack(BattleActionType.Hit, arena.Soldier, gun, gun);
        var accuracy = arena.Battle.FiringAccuracy(arena.Soldier, gun, BattleActionType.Hit);
        var roll = accuracy - 40 + 5;
        arena.Battle.Random = new ScriptedDraws(roll);
        // The alien faces the soldier: the whole dodge counts, so this roll misses.
        Assert.Equal(accuracy - roll - 40, arena.Battle.MeleeAttackCalculate(attack, arena.Alien));
        // Facing away (east): the angle is 4 quarters, the penalty the whole dodge.
        arena.Alien.SetDirection(2);
        arena.Battle.Random = new ScriptedDraws(roll);
        Assert.Equal(accuracy - roll, arena.Battle.MeleeAttackCalculate(attack, arena.Alien));
        // From the side the angle is 2 quarters: half of the dodge is lost.
        arena.Alien.SetDirection(0);
        arena.Battle.Random = new ScriptedDraws(roll);
        Assert.Equal(accuracy - roll - 40 + 20, arena.Battle.MeleeAttackCalculate(attack, arena.Alien));
    }

    [Fact]
    public void TheArcIsTheAngleBetweenTwoDirections()
    {
        Assert.Equal([0, 1, 2, 3, 4, 3, 2, 1], Enumerable.Range(0, 8).Select(direction => BattleState.ArcDirection(direction, 0)));
        Assert.Equal(4, BattleState.ArcDirection(6, 2));
        Assert.Equal(0, BattleState.ArcDirection(5, 5));
    }

    // ---- the hooks ----

    [Fact]
    public void TheMeleeScriptsAndBonusesRunAtTheReferencesPlaces()
    {
        var arena = Arm(alienExtra: "    meleeDodge:\n      flatOne: 10\n");
        var recorder = new Recorder { ItemResult = 1 };
        arena.Battle.Hooks = recorder;
        arena.Battle.Random = new ScriptedDraws(99);
        var gun = Gun(arena);
        Assert.Null(Hit(arena, gun));
        // The melee bonus is read for the blast's power first, then the dodge; the item's script runs with the default result, the unit's after it with the
        // item's; a result above 0 hits whatever the roll was.
        Assert.Equal(["MeleeBonus 30", "MeleeDodge 10"], recorder.Calls.Take(2));
        Assert.Equal("item 100 10 99 0 0 -9", recorder.Calls[2]);
        Assert.Equal("unit 1", recorder.Calls[3]);
        Assert.True(arena.Alien.Health < 100);
        // A unit script that gives 0 turns the hit into a miss.
        var other = Arm();
        other.Battle.Hooks = new Recorder { UnitResult = 0 };
        Hit(other, Gun(other));
        Assert.Equal(100, other.Alien.Health);
    }

    // ---- the ammunition ----

    private static Arena Rod(int spendPerShot = 1, string gunExtra = "", string clipExtra = "") => Arm(gun: "", more: rules =>
    {
        rules = Replaced(rules, "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]",
            $"- type: GUN\n    battleType: 3\n    compatibleAmmo: [CLIP]\n    confMelee: {{ammoSlot: 0, spendPerShot: {spendPerShot}}}\n    costMelee: {{time: 20}}\n    accuracyMelee: 100\n{gunExtra}");
        return Replaced(rules, "- type: CLIP\n    battleType: 2\n    clipSize: 12",
            "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    meleePower: 30\n    meleeType: 1\n    meleeAlter: {RandomType: 3}\n" + clipExtra);
    });

    [Fact]
    public void AMeleeWeaponUsesARoundOfItsAmmunitionAndTheLastRoundLeavesTheBattle()
    {
        var arena = Rod();
        var (_, gun, clip) = Shot(arena);
        clip.AmmoQuantity = 2;
        Assert.Null(Hit(arena, gun));
        Assert.Equal(1, clip.AmmoQuantity);
        Assert.Contains(clip, arena.Battle.Items);
        // The hit struck with the clip's melee power: the damage item is the ammunition.
        Assert.True(arena.Alien.Health < 100);
        arena.Battle.Random = new ScriptedDraws(0);
        Assert.Null(Hit(arena, gun));
        Assert.DoesNotContain(clip, arena.Battle.Items);
        Assert.Null(gun.AmmoSlots[0]);
        Assert.False(clip.IsAmmo);
        // Nothing is left to hit with: refused before the unit pays.
        var tu = arena.Soldier.TimeUnits;
        Assert.Equal("STR_NO_AMMUNITION_LOADED", Hit(arena, gun)!.TextKey);
        Assert.Equal(tu, arena.Soldier.TimeUnits);
    }

    [Fact]
    public void AHitNeedsTheRoundsItSpends()
    {
        var arena = Rod(spendPerShot: 3);
        var (_, gun, clip) = Shot(arena);
        clip.AmmoQuantity = 2;
        var reason = Hit(arena, gun);
        Assert.Equal("STR_NO_ROUNDS_LEFT", reason!.TextKey);
        Assert.Equal((2, 100, 100), (clip.AmmoQuantity, arena.Soldier.TimeUnits, arena.Alien.Health));
        clip.AmmoQuantity = 3;
        Assert.Null(Hit(arena, gun));
        Assert.DoesNotContain(clip, arena.Battle.Items);
    }

    [Fact]
    public void ARechargeableClipStaysInTheWeaponWhenItsLastRoundIsSpent()
    {
        var arena = Rod(clipExtra: "    isAmmoRechargeable: true\n");
        var (_, gun, clip) = Shot(arena);
        clip.AmmoQuantity = 1;
        Assert.Null(Hit(arena, gun));
        Assert.Equal(0, clip.AmmoQuantity);
        Assert.Contains(clip, arena.Battle.Items);
        Assert.Equal(clip.Id, gun.AmmoSlots[0]);
        Assert.True(clip.IsAmmo);
    }

    [Fact]
    public void AHostileUnitsFurtherHitsOnlyNeedSomeAmmunitionInTheSlotNotTheFullShare()
    {
        // Each hit spends 2 rounds. Three are loaded: the first hit spends two, the later ones find one round, strike anyway (the reference's check only
        // asks for ammunition in the slot) and spend nothing.
        var arena = Rod(spendPerShot: 2, gunExtra: "    ai: {meleeHitCount: 3}\n");
        foreach (var item in arena.Battle.InventoryOf(arena.Soldier).ToList()) arena.Battle.RemoveItem(item);
        var (_, rod, clip) = Shot(arena, arena.Alien);
        clip.AmmoQuantity = 3;
        arena.Battle.Side = BattleFaction.Hostile;
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Melee = 100 };
        arena.Alien.TimeUnits = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 30000 };
        arena.Soldier.Health = 30000;
        var cost = BattleActionCost.For(arena.Rules, BattleActionType.Hit, arena.Alien, rod);
        Assert.Null(arena.Battle.StrikeMelee(arena.Alien, rod, new BattlePosition(10, 10, 0), 0, cost));
        Assert.Equal(3, arena.Battle.Events.Drain().OfType<UnitMeleeAttacked>().Count());
        Assert.Equal(40, arena.Alien.TimeUnits);
        Assert.Equal(1, clip.AmmoQuantity);
    }

    [Fact]
    public void AMeleeWeaponWithItsOwnAmmunitionHitsWithItsPower()
    {
        // BT_MELEE with the self-use slot: the weapon itself is the damage item, with its power (not the melee power) and damage type.
        var arena = Arm(gun: "", more: rules => Replaced(rules, "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]",
            "- type: GUN\n    battleType: 3\n    clipSize: -1\n    power: 30\n    damageType: 1\n    damageAlter: {RandomType: 3}\n    costMelee: {time: 20}\n    accuracyMelee: 100\n    confMelee: {ammoSlot: -1}"));
        var gun = Shot(arena).Gun;
        Assert.Null(Hit(arena, gun));
        Assert.True(arena.Alien.Health < 100);
        Assert.Contains(arena.Battle.Items, item => item == gun);
    }

    // ---- refusals ----

    [Fact]
    public void ARefusalBeforeTheStrikeChangesNothing()
    {
        var arena = Arm(distance: 3);
        var gun = Gun(arena);
        // nobody within reach
        Assert.Equal(BattleAttackReasons.NoOneThere, Hit(arena, gun)!.TextKey);
        // another unit's weapon, or one not in a hand
        Assert.Equal("battle.attack-weapon-unavailable", Hit(arena, new BattleItem(arena.Battle.TakeItemId(), "GUN"))!.Code);
        // not enough time units
        arena.Stand(arena.Alien, 11, 10, 6);
        arena.See();
        arena.Soldier.TimeUnits = 19;
        Assert.Equal("STR_NOT_ENOUGH_TIME_UNITS", Hit(arena, gun)!.TextKey);
        // a unit of the side not on turn
        arena.Soldier.TimeUnits = 100;
        Assert.Equal("battle.selection-unit-unknown", Hit(arena, Gun(arena, arena.Alien), unit: arena.Alien)!.Code);
        Assert.Equal((100, 100), (arena.Soldier.TimeUnits, arena.Alien.Health));
        Assert.Empty(arena.Battle.Events.Drain());
    }

    [Fact]
    public void AWeaponWithoutAMeleeCostDoesNotOfferTheHit()
    {
        var arena = Arm(gun: "meleePower: 30");
        Assert.Equal("battle.attack-action-not-offered", Hit(arena, Gun(arena))!.Code);
        Assert.Equal(100, arena.Alien.Health);
    }

    [Fact]
    public void ThePsiWeaponNeedsTheSkillBeforeItOffersTheHit()
    {
        var arena = Arm(gun: Butt + "\npsiRequired: true");
        arena.Soldier.Stats = arena.Soldier.Stats with { PsiSkill = 0 };
        Assert.Equal("battle.attack-action-not-offered", Hit(arena, Gun(arena))!.Code);
        arena.Soldier.Stats = arena.Soldier.Stats with { PsiSkill = 40 };
        Assert.Null(Hit(arena, Gun(arena)));
    }

    // ---- who can be reached ----

    [Fact]
    public void AUnitTwoTilesAwayCannotBeReachedAndADiagonalOneCanBe()
    {
        var arena = Arm(distance: 2);
        var soldier = arena.Soldier;
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 2, soldier, null, out _));
        arena.Stand(arena.Alien, 11, 11, 6);
        arena.See();
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 2, soldier, null, out _));
        Assert.True(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 3, soldier, null, out var dest));
        Assert.Equal(new BattlePosition(11, 11, 0), dest);
        Assert.True(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 3, soldier, arena.Alien, out _));
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 3, soldier, soldier, out _));
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 8, soldier, null, out _));
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), -1, soldier, null, out _));
    }

    [Fact]
    public void AWallBetweenTheUnitsStopsTheStrike()
    {
        var arena = Arm();
        arena.Put(11, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.Stand(arena.Alien, 12, 10, 6);
        arena.See();
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 2, arena.Soldier, null, out _));
    }

    [Fact]
    public void ABigUnitReachesWithEachOfItsTilesAndAnotherLevelThroughStairs()
    {
        var arena = Flat(bigAlien: true);
        var big = arena.Alien;
        arena.Stand(big, 10, 10, 2);
        arena.Stand(arena.Soldier, 12, 11, 6);
        arena.See();
        // The soldier stands east of the big unit's lower row: its tile (11, 11) reaches it.
        Assert.True(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 2, big, null, out var dest));
        Assert.Equal(new BattlePosition(12, 11, 0), dest);
        Assert.False(arena.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 6, big, null, out _));

        // The high part of a stair (terrain level -16) strikes the tile above in front, and an open tile above a raised floor the one below.
        var stairs = Flat();
        stairs.Put(10, 10, 0, BattleTilePart.Floor, RampHigh);
        stairs.Put(11, 10, 1, BattleTilePart.Floor, Floor);
        stairs.Stand(stairs.Alien, 11, 10, 6);
        stairs.Battle.SetTile(stairs.Alien, new BattlePosition(11, 10, 1));
        stairs.See();
        Assert.True(stairs.Battle.ValidMeleeRange(new BattlePosition(10, 10, 0), 2, stairs.Soldier, stairs.Alien, out var up));
        Assert.Equal(new BattlePosition(11, 10, 1), up);

        var below = Flat();
        below.Put(11, 10, 0, BattleTilePart.Floor, RampHigh);
        below.Clear(11, 10, 1, BattleTilePart.Floor);
        below.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        below.Battle.SetTile(below.Soldier, new BattlePosition(10, 10, 1));
        below.Battle.SetTile(below.Alien, new BattlePosition(11, 10, 0));
        below.Soldier.SetDirection(2);
        below.See();
        Assert.True(below.Battle.ValidMeleeRange(new BattlePosition(10, 10, 1), 2, below.Soldier, below.Alien, out var down));
        Assert.Equal(new BattlePosition(11, 10, 0), down);
    }

    private static BattleUnit Extra(Arena arena, int id, BattleFaction faction, int x, int y)
    {
        var unit = new BattleUnit(id, faction, faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = arena.Alien.ArmorId,
            ArmorSize = 1,
            UnitType = arena.Alien.UnitType,
            Health = 100,
            MovementType = 0,
        };
        arena.Battle.AddUnit(unit);
        arena.Battle.SetTile(unit, new BattlePosition(x, y, 0));
        unit.SetDirection(6);
        unit.Morale = 100;
        return unit;
    }

    [Fact]
    public void SeveralUnitsInReachAreChosenByFactionAndAFriendByHisWounds()
    {
        var arena = Flat(bigAlien: true);
        var big = arena.Alien;
        arena.Stand(big, 10, 10, 2);
        var friend = Extra(arena, 90, BattleFaction.Hostile, 12, 10);
        friend.FatalWounds[BattleUnit.BodyPartTorso] = 2;
        arena.Stand(arena.Soldier, 12, 11, 6);
        arena.See();
        var from = new BattlePosition(10, 10, 0);
        // The big unit reaches (12, 10) from its upper right tile and (12, 11) from its lower right one: an enemy wins over the friend found first.
        Assert.True(arena.Battle.ValidMeleeRange(from, 2, big, null, out var dest));
        Assert.Equal(new BattlePosition(12, 11, 0), dest);
        // Looking for a friend to heal (a medikit): the wounded friend, not the enemy.
        Assert.True(arena.Battle.ValidMeleeRange(from, 2, big, null, out dest, preferEnemy: false));
        Assert.Equal(new BattlePosition(12, 10, 0), dest);
        // Of two friends the one with more wounds.
        arena.Stand(arena.Soldier, 20, 20, 6);
        var worse = Extra(arena, 91, BattleFaction.Hostile, 12, 11);
        worse.FatalWounds[BattleUnit.BodyPartTorso] = 3;
        arena.See();
        Assert.True(arena.Battle.ValidMeleeRange(from, 2, big, null, out dest, preferEnemy: false));
        Assert.Equal(new BattlePosition(12, 11, 0), dest);
    }

    [Fact]
    public void AStrikeAtATileWithNoUnitDoesNothingAndAVoxelAboveTheMapIsCutToItsTop()
    {
        // The reference throws in both cases; the port strikes nothing and spends nothing in the first and cuts the voxel in the second.
        var empty = Arm();
        var emptyGun = Gun(empty);
        var emptyCost = BattleActionCost.For(empty.Rules, BattleActionType.Hit, empty.Soldier, emptyGun);
        Assert.Equal("battle.attack-not-performed", empty.Battle.StrikeMelee(empty.Soldier, emptyGun, new BattlePosition(12, 10, 0), 0, emptyCost)!.Code);
        Assert.Equal(100, empty.Soldier.TimeUnits);
        Assert.Empty(empty.Battle.Events.Drain());

        // The alien stands on the high part of a stair of the top level: its middle voxel (11 + 16 + 24) is above the map's 48 voxels.
        var top = Arm();
        top.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        top.Put(11, 10, 1, BattleTilePart.Floor, RampHigh);
        top.Battle.SetTile(top.Soldier, new BattlePosition(10, 10, 1));
        top.Battle.SetTile(top.Alien, new BattlePosition(11, 10, 1));
        top.See();
        var gun = Gun(top);
        var cost = BattleActionCost.For(top.Rules, BattleActionType.Hit, top.Soldier, gun);
        Assert.Null(top.Battle.StrikeMelee(top.Soldier, gun, new BattlePosition(11, 10, 1), 0, cost));
        Assert.IsType<UnitMeleeAttacked>(top.Battle.Events.Drain()[0]);
        Assert.Equal(80, top.Soldier.TimeUnits);
    }

    // ---- terrain ----

    private const string TerrainConstants = "\nconstants:\n  - extendedTerrainMelee: 1\n";

    private static Arena Wall1(string gun = Butt + "\nmeleeType: 3", string constants = TerrainConstants)
    {
        var arena = Arm(gun: gun.Replace("meleeType: 1\n", "", StringComparison.Ordinal), global: constants, distance: 5);
        arena.Put(11, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.See();
        return arena;
    }

    [Fact]
    public void ANoOneThereHitStrikesTheWallInFrontWhenTheModAllowsItAndItNeverMisses()
    {
        var arena = Wall1();
        var gun = Gun(arena);
        arena.Battle.Random = new ScriptedDraws(99);
        Assert.Null(Hit(arena, gun));
        var swing = Assert.IsType<UnitMeleeAttacked>(arena.Battle.Events.Drain()[0]);
        Assert.Equal((-1, 3, true), (swing.TargetUnitId, swing.TerrainPart, swing.Hit));
        Assert.Equal(80, arena.Soldier.TimeUnits);
        // The object is gone (HE-type damage over the wall's zero armor).
        Assert.Null(arena.Map.MapData(arena.Tile(11, 10), BattleTilePart.ObjectPart));
    }

    [Fact]
    public void TerrainMeleeNeedsTheModsConstantAWeaponThatHurtsTilesAndASolidEnoughPart()
    {
        // extendedTerrainMelee 0: off.
        var off = Wall1(constants: "");
        Assert.Equal(BattleAttackReasons.NoOneThere, Hit(off, Gun(off))!.TextKey);
        // A weapon whose melee type does nothing to tiles (stun): no.
        var weak = Wall1(gun: Butt.Replace("meleeType: 1", "meleeType: 6", StringComparison.Ordinal));
        Assert.Equal(BattleAttackReasons.NoOneThere, Hit(weak, Gun(weak))!.TextKey);
        // Lofts from layer 12 up: nothing is high enough.
        var high = Wall1(constants: "\nconstants:\n  - extendedTerrainMelee: 12\n");
        Assert.Equal(BattleAttackReasons.NoOneThere, Hit(high, Gun(high))!.TextKey);
        // A floor is never a target and a wall that can be walked through (cost known) is not worth hitting.
        var floors = Wall1();
        floors.Clear(11, 10, 0, BattleTilePart.ObjectPart);
        Assert.Equal(BattleAttackReasons.NoOneThere, Hit(floors, Gun(floors))!.TextKey);
        // A diagonal facing never reaches terrain.
        var diagonal = Wall1();
        diagonal.Soldier.SetDirection(3);
        Assert.False(diagonal.Battle.ValidTerrainMeleeRange(diagonal.Soldier, null, false, out _, out _));
    }

    [Fact]
    public void ATerrainStrikeNamesTheTileAndPartAndNeedsNoWeaponToAsk()
    {
        var arena = Wall1();
        Assert.True(arena.Battle.ValidTerrainMeleeRange(arena.Soldier, Gun(arena), false, out var target, out var part));
        Assert.Equal((new BattlePosition(11, 10, 0), (int)BattleTilePart.ObjectPart), (target, part));
        // With no weapon asked about, the damage type is not checked.
        Assert.True(arena.Battle.ValidTerrainMeleeRange(arena.Soldier, null, false, out _, out _));
    }

    private static Arena Open1(int direction)
    {
        var arena = Arm(global: TerrainConstants, distance: 5);
        arena.Soldier.SetDirection(direction);
        return arena;
    }

    private static bool Strike(Arena arena, BattleUnit? actor = null, bool alt = false) => Strike(arena, out _, out _, actor, alt);

    private static bool Strike(Arena arena, out BattlePosition target, out int part, BattleUnit? actor = null, bool alt = false) =>
        arena.Battle.ValidTerrainMeleeRange(actor ?? arena.Soldier, null, alt, out target, out part);

    [Fact]
    public void EachCompassDirectionLooksForItsWallsAndItsNeighboursObjectInTheReferencesPlaces()
    {
        // The tile and the kind of wall each direction strikes: the unit's own tile for north and west, the neighbouring one for east and south.
        (int Direction, int X, int Y, BattleTilePart Part)[] found =
        [
            (0, 10, 10, BattleTilePart.NorthWall),
            (2, 11, 10, BattleTilePart.WestWall),
            (4, 10, 11, BattleTilePart.NorthWall),
            (6, 10, 10, BattleTilePart.WestWall),
        ];
        foreach (var (direction, x, y, part) in found)
        {
            var arena = Open1(direction);
            arena.Put(x, y, 0, part, SolidWall);
            Assert.True(Strike(arena, out var target, out var tilePart), $"direction {direction}");
            Assert.Equal((new BattlePosition(x, y, 0), (int)part), (target, tilePart));
        }
        // The wrong kind of wall, or the wall of a tile the direction does not look at, is no target (a small unit has no second tile).
        (int Direction, int X, int Y, BattleTilePart Part)[] missed =
        [
            (0, 10, 10, BattleTilePart.WestWall),
            (0, 10, 9, BattleTilePart.WestWall),
            (2, 10, 10, BattleTilePart.WestWall),
            (2, 11, 10, BattleTilePart.NorthWall),
            (4, 10, 10, BattleTilePart.NorthWall),
            (4, 10, 11, BattleTilePart.WestWall),
            (6, 10, 10, BattleTilePart.NorthWall),
            (6, 9, 10, BattleTilePart.NorthWall),
        ];
        foreach (var (direction, x, y, part) in missed)
        {
            var arena = Open1(direction);
            arena.Put(x, y, 0, part, SolidWall);
            Assert.False(Strike(arena), $"direction {direction} {part} at {x},{y}");
        }
        // An object on the neighbouring tile in every direction.
        (int Direction, int X, int Y)[] neighbours = [(0, 10, 9), (2, 11, 10), (4, 10, 11), (6, 9, 10)];
        foreach (var (direction, x, y) in neighbours)
        {
            var arena = Open1(direction);
            arena.Put(x, y, 0, BattleTilePart.ObjectPart, Wall);
            Assert.True(Strike(arena, out var target, out var tilePart), $"direction {direction}");
            Assert.Equal((new BattlePosition(x, y, 0), (int)BattleTilePart.ObjectPart), (target, tilePart));
        }
    }

    [Fact]
    public void APartTheUnitCannotTargetIsSkippedForTheNextOne()
    {
        // A big unit facing east, a west wall east of its lower right tile, and solid objects (no big wall) on its right tiles between its eye and the wall.
        var arena = Flat(bigAlien: true, edit: static rules => rules + TerrainConstants);
        arena.Stand(arena.Soldier, 20, 20, 2);
        arena.Stand(arena.Alien, 10, 10, 2);
        arena.Put(12, 11, 0, BattleTilePart.WestWall, SolidWall);
        arena.See();
        Assert.True(Strike(arena, out _, out _, arena.Alien));
        arena.Put(11, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.Put(11, 11, 0, BattleTilePart.ObjectPart, Wall);
        Assert.False(Strike(arena, actor: arena.Alien));
    }

    [Fact]
    public void AnObjectOnTheUnitsOwnTileIsStruckOnlyWhenItsBigWallFacesTheDirection()
    {
        // (the big wall record, the directions that strike it)
        (int Record, int[] Directions)[] objects =
        [
            (BigNorth, [0]),
            (BigEast, [2]),
            (BigSouth, [4]),
            (BigWest, [6]),
            (BigWestNorth, [0, 6]),
        ];
        foreach (var (record, directions) in objects)
        {
            foreach (var direction in new[] { 0, 2, 4, 6 })
            {
                var arena = Open1(direction);
                arena.Put(10, 10, 0, BattleTilePart.ObjectPart, record);
                var strikes = Strike(arena, out var target, out var part);
                Assert.Equal(directions.Contains(direction), strikes);
                if (strikes) Assert.Equal((new BattlePosition(10, 10, 0), (int)BattleTilePart.ObjectPart), (target, part));
            }
        }
    }

    [Fact]
    public void AWallThatCanBeWalkedThroughIsNoTargetUnlessItIsADoor()
    {
        // (the record, whether it is struck) as a west wall east of the unit: a wall with no cost, a wall nobody can walk through, a door and a UFO door.
        foreach (var (record, struck) in new[] { (Wall, false), (SolidWall, true), (DoorWall, true), (UfoDoor, true) })
        {
            var arena = Open1(2);
            arena.Put(11, 10, 0, BattleTilePart.WestWall, record);
            Assert.Equal(struck, Strike(arena));
        }
    }

    [Fact]
    public void OnTheHighPartOfStairsTheTerrainStruckIsOneLevelUpUnlessAltIsHeld()
    {
        var arena = Open1(2);
        arena.Put(10, 10, 0, BattleTilePart.Floor, RampHigh);
        arena.Put(11, 10, 1, BattleTilePart.ObjectPart, Wall);
        Assert.True(Strike(arena, out var target, out var part));
        Assert.Equal((new BattlePosition(11, 10, 1), (int)BattleTilePart.ObjectPart), (target, part));
        // Alt: the level the unit stands on, where nothing is to strike.
        Assert.False(Strike(arena, alt: true));
        arena.Put(11, 10, 0, BattleTilePart.ObjectPart, Wall);
        Assert.True(Strike(arena, out target, out _, alt: true));
        Assert.Equal(new BattlePosition(11, 10, 0), target);
        // On level ground Alt changes nothing.
        var flat = Open1(2);
        flat.Put(11, 10, 0, BattleTilePart.ObjectPart, Wall);
        Assert.True(Strike(flat, alt: true));
    }

    [Fact]
    public void ABigUnitStrikesFromItsSecondTilesToo()
    {
        // A west wall east of the lower right tile, a north wall north of the upper right tile, a north wall south of the lower right tile
        // (the neighbour's) and a west wall west of the lower left tile.
        (int Direction, int X, int Y, BattleTilePart Part)[] cases =
        [
            (2, 12, 11, BattleTilePart.WestWall),
            (0, 11, 10, BattleTilePart.NorthWall),
            (4, 11, 12, BattleTilePart.NorthWall),
            (6, 10, 11, BattleTilePart.WestWall),
            // The second neighbouring tile's other wall kind, which only a big unit has.
            (0, 11, 9, BattleTilePart.WestWall),
            (2, 12, 11, BattleTilePart.NorthWall),
            (4, 11, 12, BattleTilePart.WestWall),
            (6, 9, 11, BattleTilePart.NorthWall),
        ];
        foreach (var (direction, x, y, part) in cases)
        {
            var arena = Flat(bigAlien: true, edit: static rules => rules + TerrainConstants);
            arena.Stand(arena.Soldier, 20, 20, 2);
            arena.Stand(arena.Alien, 10, 10, direction);
            arena.Put(x, y, 0, part, SolidWall);
            arena.See();
            Assert.True(Strike(arena, out var target, out var tilePart, arena.Alien), $"direction {direction} {part} at {x},{y}");
            Assert.Equal((new BattlePosition(x, y, 0), (int)part), (target, tilePart));
        }
    }

    [Fact]
    public void ATerrainStrikeNeverHurtsTheBodyLyingOnTheTile()
    {
        var arena = Wall1();
        var body = Extra(arena, 90, BattleFaction.Hostile, 11, 10);
        arena.Battle.SetTile(body, new BattlePosition(0, 0, 0));
        arena.Map.SetUnitAt(arena.Tile(0, 0), null);
        body.Position = null;
        body.SavedPosition = new BattlePosition(11, 10, 0);
        body.Status = BattleUnitStatus.Unconscious;
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = body.Id }, new BattlePosition(11, 10, 0));
        Assert.Null(Hit(arena, Gun(arena)));
        Assert.Equal(100, body.Health);
        Assert.Null(arena.Map.MapData(arena.Tile(11, 10), BattleTilePart.ObjectPart));
    }

    // ---- what the attacker is and does ----

    [Fact]
    public void ANeighboursMeleeReactionsRememberTheAttackerWhenTheModAsksAndCloseQuartersDoesNot()
    {
        const string Reactions = "\nconstants:\n  - extendedMeleeReactions: 2\n";
        var arena = Arm(global: Reactions);
        var gun = Gun(arena);
        Hit(arena, gun);
        Assert.Equal([arena.Soldier.Id], arena.Alien.MeleeAttackedBy);
        // A second blow does not list the attacker twice.
        arena.Battle.Random = new ScriptedDraws(0);
        Hit(arena, gun);
        Assert.Equal([arena.Soldier.Id], arena.Alien.MeleeAttackedBy);
        // The key belongs to the constants section: at the root of the file the reference ignores it.
        var root = Arm(global: "\nextendedMeleeReactions: 2\n");
        Hit(root, Gun(root));
        Assert.Empty(root.Alien.MeleeAttackedBy);
        var quarters = Arm(global: Reactions);
        var attack = new BattleAttack(BattleActionType.CloseQuarters, quarters.Soldier, Gun(quarters), null);
        quarters.Battle.MeleeAttack(attack, quarters.Alien, 0);
        Assert.Empty(quarters.Alien.MeleeAttackedBy);
        var plain = Arm();
        Hit(plain, Gun(plain));
        Assert.Empty(plain.Alien.MeleeAttackedBy);
    }

    [Fact]
    public void AUnitThatBurnsFloorsSetsTheTargetsTileAlightAndAHostileUnitHitsAgainByTheWeaponsCount()
    {
        var arena = Arm(gun: Butt + "\nai: {meleeHitCount: 3}");
        var alien = arena.Alien;
        // The hostile unit's turn: it hits the soldier (to its west) up to three times for the weapon's count, paying for each.
        var gun = HostileStrike(arena, alien, out var cost);
        arena.Soldier.Health = 30000;
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 30000 };
        Assert.Null(arena.Battle.StrikeMelee(alien, gun, new BattlePosition(10, 10, 0), 0, cost));
        var swings = arena.Battle.Events.Drain().OfType<UnitMeleeAttacked>().ToList();
        Assert.Equal(3, swings.Count);
        Assert.Equal(40, alien.TimeUnits);
        // A target that is out stops the hits.
        var down = Arm(gun: Butt + "\nai: {meleeHitCount: 3}");
        var killer = HostileStrike(down, down.Alien, out var killCost);
        down.Soldier.Health = 1;
        Assert.Null(down.Battle.StrikeMelee(down.Alien, killer, new BattlePosition(10, 10, 0), 0, killCost));
        Assert.Single(down.Battle.Events.Drain().OfType<UnitMeleeAttacked>());
        Assert.Equal(80, down.Alien.TimeUnits);
        // A weapon with no more rounds stops them too, and the first hit of one that has none starts nothing.
        var rounds = Rod();
        foreach (var item in rounds.Battle.InventoryOf(rounds.Soldier).ToList()) rounds.Battle.RemoveItem(item);
        var (_, rod, clip) = Shot(rounds, rounds.Alien);
        clip.AmmoQuantity = 1;
        rounds.Battle.Side = BattleFaction.Hostile;
        rounds.Alien.Stats = rounds.Alien.Stats with { TimeUnits = 100, Melee = 100 };
        rounds.Alien.TimeUnits = 100;
        rounds.Soldier.Stats = rounds.Soldier.Stats with { Health = 30000 };
        rounds.Soldier.Health = 30000;
        var rodCost = BattleActionCost.For(rounds.Rules, BattleActionType.Hit, rounds.Alien, rod);
        Assert.Null(rounds.Battle.StrikeMelee(rounds.Alien, rod, new BattlePosition(10, 10, 0), 0, rodCost));
        Assert.Single(rounds.Battle.Events.Drain().OfType<UnitMeleeAttacked>());
        Assert.Equal(80, rounds.Alien.TimeUnits);
        Assert.Equal("STR_NO_AMMUNITION_LOADED", rounds.Battle.StrikeMelee(rounds.Alien, rod, new BattlePosition(10, 10, 0), 0, rodCost)!.TextKey);
    }

    private static BattleItem HostileStrike(Arena arena, BattleUnit alien, out BattleActionCost cost)
    {
        foreach (var item in arena.Battle.InventoryOf(arena.Soldier).ToList()) arena.Battle.RemoveItem(item);
        var gun = Gun(arena, alien);
        arena.Battle.Side = BattleFaction.Hostile;
        alien.Stats = alien.Stats with { TimeUnits = 100, Melee = 100 };
        alien.TimeUnits = 100;
        arena.Battle.Random = new ScriptedDraws(0);
        cost = BattleActionCost.For(arena.Rules, BattleActionType.Hit, alien, gun);
        return gun;
    }

    [Fact]
    public void AUnitThatBurnsFloorsSetsTheTargetsTileAlight()
    {
        var arena = Arm(alienExtra: "    specab: 2\n");
        arena.Put(10, 10, 0, BattleTilePart.Floor, BurnFloor);
        var gun = HostileStrike(arena, arena.Alien, out var cost);
        Assert.Null(arena.Battle.StrikeMelee(arena.Alien, gun, new BattlePosition(10, 10, 0), 0, cost));
        Assert.Contains(arena.Battle.Events.Drain().OfType<TileIgnited>(), static fire => fire.Position == new BattlePosition(10, 10, 0));
        Assert.True(arena.Map.Fire(arena.Tile(10, 10)) > 0);
    }

    // ---- the reaction and the command ----

    [Fact]
    public void AnAttackDrawsAReactionOnlyWithTheExtendedMeleeReactions()
    {
        // tryReaction: the chance of a reaction to a hit is 100 with extendedMeleeReactions above 0, else 0 (the reactor is asked, and does not roll in).
        var (plain, extended) = (Arm(), Arm(global: "\nconstants:\n  - extendedMeleeReactions: 1\n"));
        foreach (var arena in new[] { plain, extended })
        {
            var gun = Gun(arena);
            Assert.NotNull(Gun(arena, arena.Alien));
            arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Reactions = 100 };
            arena.Alien.TimeUnits = 100;
            arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 1 };
            Assert.Null(Hit(arena, gun));
        }
        Assert.Equal(100, plain.Alien.TimeUnits);
        Assert.True(extended.Alien.TimeUnits < 100);
        Assert.Contains(extended.Battle.Events.Drain(), @event => @event is UnitFired fired && fired.UnitId == extended.Alien.Id ||
            @event is UnitMeleeAttacked attacked && attacked.UnitId == extended.Alien.Id);
    }

    [Fact]
    public void WhenNoOneCouldAnswerTheUnitsThatSeeTheAttackerSpotIt()
    {
        // The alien looks away from the attacker, but a melee attacker is seen from any side (extendedMeleeReactions 2).
        var arena = Arm(global: "\nconstants:\n  - extendedMeleeReactions: 2\n");
        arena.Alien.SetDirection(2);
        // The alien's reaction score reaches the attacker's, and it has nothing to react with.
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Reactions = 100 };
        arena.Alien.TimeUnits = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 1 };
        arena.Alien.ClearVisibleUnits();
        arena.Battle.SetSpotted(arena.Soldier, BattleFaction.Hostile, false);
        Assert.Null(Hit(arena, Gun(arena)));
        Assert.Contains(arena.Soldier.Id, arena.Alien.VisibleUnits);
        Assert.True(arena.Soldier.IsSpottedBy(BattleFaction.Hostile));
    }

    [Fact]
    public void NoReactionIsCheckedWhileAUnitFalls()
    {
        var arena = Arm();
        var gun = Gun(arena);
        var reactor = Gun(arena, arena.Alien);
        Assert.NotNull(reactor);
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 100, Reactions = 100 };
        arena.Alien.TimeUnits = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 1 };
        // A unit on its way down: the reference's check waits for the fall, so nothing can answer and nothing refuses.
        Assert.True(arena.Battle.AddFallingUnit(arena.Alien));
        Assert.Null(Hit(arena, gun));
        Assert.True(arena.Alien.Health < 100);
    }

    private sealed class ResearchLink : IBattleCampaignLink
    {
        public int MissionCount => 0;

        public int Difficulty => 0;

        public BattleResearchGate Research { get; } = new(static _ => false, false);

        public BattleSoldierDescription? DescribeSoldier(int soldierId) => null;

        public void KillSoldier(int soldierId, BattleUnitKillSnapshot cause)
        {
        }
    }

    [Fact]
    public void TheAttackCommandReturnsTheStrikeThenTheHitAndRefusesTheKindsOfLaterSlices()
    {
        var arena = Arm();
        var gun = Gun(arena);
        var runtime = RuntimeOf(arena);
        var result = runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.Hit));
        Assert.IsType<UnitMeleeAttacked>(result.Events[0]);
        Assert.IsType<UnitHit>(result.Events[1]);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(80, arena.Soldier.TimeUnits);
        // A kind the battle does not know (the shots are in BattleShootingTests, the throw in BattleThrowingTests, the launch in BattleLaunchTests).
        var shot = runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, (BattleAimKind)99));
        Assert.Equal("battle.attack-kind-unknown", Refusal(shot));
        // A refused attack returns its reason alone and the command does not fault the battle.
        arena.Soldier.TimeUnits = 5;
        Assert.Equal("battle.attack-refused", Refusal(runtime.Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.Hit))));
        Assert.Null(runtime.Fault);
    }

    [Fact]
    public void TheCommandReadsTheResearchOfTheCampaignThroughItsLink()
    {
        var arena = Arm(gun: Butt + "\nrequires: [STR_ALIEN_ALLOYS]", more: static rules => rules + "\nresearch:\n  - name: STR_ALIEN_ALLOYS\n    cost: 1\n");
        var gun = Gun(arena);
        var refused = RuntimeOf(arena, new ResearchLink()).Execute(new Attack(arena.Soldier.Id, gun.Id, BattleAimKind.Hit));
        Assert.Equal("battle.attack-refused", Refusal(refused));
        Assert.Equal(100, arena.Alien.Health);
        // Without a link the research is done (a battle built by hand).
        var other = Arm(gun: Butt + "\nrequires: [STR_ALIEN_ALLOYS]", more: static rules => rules + "\nresearch:\n  - name: STR_ALIEN_ALLOYS\n    cost: 1\n");
        var done = RuntimeOf(other).Execute(new Attack(other.Soldier.Id, Gun(other).Id, BattleAimKind.Hit));
        Assert.IsType<UnitMeleeAttacked>(done.Events[0]);
        Assert.True(other.Alien.Health < 100);
    }
}
