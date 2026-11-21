using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Xunit;
using static Oxce.CompatibilityTests.BattleDamageTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>Phase 7 branch 5 S4 audit follow-up: fall rounds, dropping a dead unit's fixed items, a melee item's ammunition, conversions that find no room, and a flier's revival.</summary>
public sealed class BattleCasualtyFollowUpTests
{
    private static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    private static BattleUnit Extra(Arena arena, int id, BattleFaction faction, int x, int y, int z = 0, string armor = "", int size = 1)
    {
        var unit = new BattleUnit(id, faction, faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = armor.Length == 0 ? arena.Alien.ArmorId : armor,
            ArmorSize = size,
            UnitType = arena.Alien.UnitType,
            Health = 100,
            MovementType = 0,
        };
        arena.Battle.AddUnit(unit);
        arena.Battle.SetTile(unit, At(x, y, z));
        unit.SetDirection(6);
        return unit;
    }

    private static string Tall(string rules) =>
        rules.Replace("    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4", StringComparison.Ordinal);

    [Fact]
    public void KnockOutsInSeveralFallRoundsRunTheLaterRoundsDeathsFirst()
    {
        var arena = Flat(edit: Tall);
        var (victim1, victim2) = (Extra(arena, 970, BattleFaction.Hostile, 12, 12), Extra(arena, 971, BattleFaction.Hostile, 16, 12));
        foreach (var (cx, cy) in new[] { (12, 12), (16, 12) })
            foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
                arena.Clear(cx + dx, cy + dy, 0, BattleTilePart.Floor);
        // One lands in the first round, the other two levels up lands in the second.
        var (faller1, faller2) = (Extra(arena, 972, BattleFaction.Hostile, 12, 12, 1), Extra(arena, 973, BattleFaction.Hostile, 16, 12, 2));
        foreach (var faller in new[] { faller1, faller2 })
        {
            arena.Battle.UpdateTileFloorState(faller);
            arena.Battle.AddFallingUnit(faller);
        }
        arena.Battle.ResolveFalls();
        Assert.Equal([BattleUnitStatus.Unconscious, BattleUnitStatus.Unconscious], [victim1.Status, victim2.Status]);
        var body1 = Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == victim1.Id);
        var body2 = Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == victim2.Id);
        // statePushNext puts the second round's deaths right behind the running fall, ahead of the first round's.
        Assert.True(body2.Id < body1.Id);
        Assert.Empty(arena.Battle.FallingUnits);
    }

    [Fact]
    public void DroppingTheInventoryWithDeleteFixedItemsUnloadsAndRemovesTheFixedOnesAndTheSpecialWeapons()
    {
        var arena = Duel(gun: "fixedWeapon: true");
        var soldier = arena.Soldier;
        var (_, gun, clip) = Shot(arena);
        var grenade = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE"), null);
        grenade.OwnerUnitId = soldier.Id;
        grenade.Slot = "STR_LEFT_HAND";
        grenade.Tile = null;
        var special = arena.Battle.Items.Where(item => soldier.SpecialWeaponIds.Contains(item.Id)).ToArray();
        arena.Battle.ItemDropInventory(At(10, 10), soldier, deleteFixedItems: true);
        Assert.DoesNotContain(gun, arena.Battle.Items);
        Assert.Equal((At(10, 10), "STR_GROUND", null, false), (clip.Tile, clip.Slot, gun.AmmoSlots[0], clip.IsAmmo));
        Assert.Equal((At(10, 10), null), (grenade.Tile, grenade.OwnerUnitId));
        Assert.Empty(soldier.SpecialWeaponIds);
        foreach (var item in special) Assert.DoesNotContain(item, arena.Battle.Items);

        // Without it the fixed weapon stays with its owner and the rest is dropped.
        var keep = Duel(gun: "fixedWeapon: true");
        var (_, fixedGun, _) = Shot(keep);
        keep.Battle.ItemDropInventory(At(10, 10), keep.Soldier, deleteFixedItems: false);
        Assert.Equal((keep.Soldier.Id, null), (fixedGun.OwnerUnitId, fixedGun.Tile));
    }

    [Fact]
    public void AMeleeItemsOwnAttackNamesTheItemAsTheAmmunition()
    {
        var arena = Duel(more: rules => rules.Replace("- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    battleType: 3\n    clipSize: 1", StringComparison.Ordinal));
        var (_, gun, _) = Shot(arena);
        var club = new BattleItem(arena.Battle.TakeItemId(), "GRENADE");
        arena.Battle.AddItem(club);
        arena.Alien.Health = 0;
        arena.Battle.CheckForCasualties(null, new BattleAttack(BattleActionType.Hit, arena.Soldier, gun, club));
        Assert.Equal("GRENADE", Assert.Single(arena.Soldier.Statistics.Kills).WeaponAmmo);
    }

    [Fact]
    public void AConversionForACarriedUnitSpawnsNothingAndOneThatDoesNotFitIsSkipped()
    {
        var arena = Flat(bigAlien: true);
        // The spawn unit of this arena covers two by two tiles.
        var units = arena.Battle.Units.Count;
        var carried = Extra(arena, 980, BattleFaction.Hostile, 3, 3, armor: "SMALL_VEHICLE_ARMOR");
        carried.SpawnUnit = "ALIEN";
        var body = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = carried.Id }, null);
        body.OwnerUnitId = arena.Soldier.Id;
        body.Slot = "STR_BACK_PACK";
        arena.Battle.SetTile(carried, At(4, 4));
        arena.Map.SetUnitAt(arena.Tile(4, 4), null);
        carried.Position = null;
        carried.Status = BattleUnitStatus.Unconscious;
        Assert.Null(arena.Battle.ConvertUnit(carried));
        Assert.Equal((units + 1, BattleUnitStatus.Dead), (arena.Battle.Units.Count, carried.Status));
        Assert.DoesNotContain(body, arena.Battle.Items);

        // A footprint with a unit on it, or off the map, is not spawned on.
        var crowded = Extra(arena, 981, BattleFaction.Hostile, 6, 6, armor: "SMALL_VEHICLE_ARMOR");
        crowded.SpawnUnit = "ALIEN";
        Extra(arena, 982, BattleFaction.Hostile, 7, 6, armor: "SMALL_VEHICLE_ARMOR");
        var count = arena.Battle.Units.Count;
        Assert.Null(arena.Battle.ConvertUnit(crowded));
        var edge = Extra(arena, 983, BattleFaction.Hostile, 29, 29, armor: "SMALL_VEHICLE_ARMOR");
        edge.SpawnUnit = "ALIEN";
        Assert.Null(arena.Battle.ConvertUnit(edge));
        Assert.Equal((count + 1, BattleUnitStatus.Dead), (arena.Battle.Units.Count, edge.Status));

        // With room the big unit takes the place.
        var roomy = Extra(arena, 984, BattleFaction.Hostile, 20, 20, armor: "SMALL_VEHICLE_ARMOR");
        roomy.SpawnUnit = "ALIEN";
        var spawned = arena.Battle.ConvertUnit(roomy);
        Assert.Equal((At(20, 20), 2), (spawned!.Position, spawned.ArmorSize));
    }

    [Fact]
    public void AFlyingUnitThatWakesWithNoFreeTileAroundTakesTheTileAbove()
    {
        var arena = Duel();
        var sleeper = Extra(arena, 990, BattleFaction.Hostile, 3, 3);
        var body = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = sleeper.Id }, At(20, 20));
        arena.Battle.SetTile(sleeper, At(4, 4));
        arena.Map.SetUnitAt(arena.Tile(4, 4), null);
        sleeper.Position = null;
        sleeper.SavedPosition = At(20, 20);
        (sleeper.Status, sleeper.Health, sleeper.StunLevel, sleeper.MovementType) = (BattleUnitStatus.Unconscious, 10, 3, 1);
        var id = 991;
        foreach (var (dx, dy) in new[] { (0, 0), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
            Extra(arena, id++, BattleFaction.Hostile, 20 + dx, 20 + dy);
        arena.Battle.ReviveUnconsciousUnits(noTU: true);
        Assert.Equal((BattleUnitStatus.Standing, At(20, 20, 1)), (sleeper.Status, sleeper.Position));
        Assert.DoesNotContain(body, arena.Battle.Items);
    }
}
