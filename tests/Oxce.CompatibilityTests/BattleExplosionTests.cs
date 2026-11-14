using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleDamageTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S5: explosions (<c>ExplosionBState</c>, <c>TileEngine::explode</c>, <c>detonate</c>, <c>checkForTerrainExplosions</c> and its chain, the
/// self-destruct of a unit, the units and items an explosion spawns, and the generator's <c>explodePowerSources</c> and <c>explodeOtherJunk</c>) on the flat arena
/// of the sight tests. <see cref="Blast"/> is the base high explosive type with its damage rolled flat, so a unit's loss follows the power that reaches it.
/// </summary>
public sealed class BattleExplosionTests
{
    private const string BlastGrenade = "damageType: 3\ndamageAlter: {RandomType: 3}\npower: ";

    /// <summary>The base high explosive type: damage is the power reaching the target (a flat roll), half of it to tiles, all of it to items.</summary>
    private static RuntimeItemDamageRule Blast { get; } = RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageHighExplosive) with
    {
        RandomType = RuntimeItemDamageRule.RandomFlat,
    };

    private static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    private static BattlePosition Center(int x, int y) => new(x * 16 + 8, y * 16 + 8, 12);

    private static BattleUnit Extra(Arena arena, int id, int x, int y, BattleFaction faction = BattleFaction.Hostile)
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
        arena.Battle.SetTile(unit, At(x, y));
        unit.SetDirection(6);
        unit.Morale = 100;
        return unit;
    }

    private static readonly int[] Distances = [0, 1, 3, 7];
    private static readonly int[] ItemDistances = [0, 3, 4, 5];
    private static readonly int[] ChainColumns = [24, 25, 26];
    private static readonly int[] TerrainKinds = [0, 5, 6, 3];

    private static BattleItem Grenade(Arena arena, BattlePosition tile) =>
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE"), tile);

    private static Arena Armed(string grenade = "") => Duel(grenade: BlastGrenade + "80" + (grenade.Length == 0 ? "" : "\n" + grenade));

    // ---- TileEngine::explode: the spread ----

    [Fact]
    public void TheDamageFallsWithTheDistanceAndStopsAtTheRadius()
    {
        var arena = Duel();
        var units = Distances.Select((distance, index) => Extra(arena, 970 + index, 20 + distance, 20)).ToArray();
        arena.Battle.Explode(default, Center(20, 20), 100, Blast, 6, true);
        var lost = units.Select(static unit => 100 - unit.Health).ToArray();
        Assert.True(lost[0] > lost[1] && lost[1] > lost[2] && lost[2] > 0, string.Join(",", lost));
        // Seven tiles away is past the radius of six.
        Assert.Equal(0, lost[3]);
        // The center is a direct hit and the unit one tile away a ground zero one (a squared distance under 4): both from below. Three tiles away the hit is from the side.
        Assert.Equal((BattleUnit.SideUnder, BattleUnit.SideUnder), (units[0].FatalShotSide, units[1].FatalShotSide));
        Assert.NotEqual(BattleUnit.SideUnder, units[2].FatalShotSide);
    }

    [Fact]
    public void ARayLosesTheTypesRadiusReductionForEachTile()
    {
        // The reduction of the base high explosive is 10 a tile: a power of 100 at the center is 60 four tiles away, and a custom reduction of 25 leaves 0 there.
        var near = Duel();
        var far = Duel();
        var (a, b) = (Extra(near, 970, 24, 20), Extra(far, 970, 24, 20));
        near.Battle.Explode(default, Center(20, 20), 100, Blast, 6, true);
        far.Battle.Explode(default, Center(20, 20), 100, Blast with { RadiusReduction = 25 }, 6, true);
        Assert.True(100 - a.Health > 0);
        Assert.Equal(100, b.Health);
    }

    [Fact]
    public void AFireBlastHalvesItsPowerAndTheRadiusIsCutByDiagonals()
    {
        // The incendiary type is a fire blast: a power of 60 is 30 for the rays.
        var arena = Duel();
        arena.Put(20, 20, 0, BattleTilePart.Floor, BurnFloor);
        var unit = Extra(arena, 970, 20, 20);
        arena.Battle.Explode(default, Center(20, 20), 60, RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageIncendiary) with
        {
            RandomType = RuntimeItemDamageRule.RandomFlat,
        }, 3, true);
        Assert.True(arena.Map.Fire(arena.Tile(20, 20)) > 0);
        Assert.True(unit.Health < 100);
        // Diagonal neighbours cost half as much again, so a ray of 15 reaches an orthogonal neighbour but not the next diagonal one.
        var flame = Duel();
        flame.Put(21, 20, 0, BattleTilePart.Floor, BurnFloor);
        flame.Put(22, 21, 0, BattleTilePart.Floor, BurnFloor);
        flame.Battle.Explode(default, Center(20, 20), 30, RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageIncendiary) with
        {
            RandomType = RuntimeItemDamageRule.RandomFlat,
        }, 3, true);
        Assert.True(flame.Map.Fire(flame.Tile(21, 20)) > 0);
        Assert.Equal(0, flame.Map.Fire(flame.Tile(22, 21)));
    }

    [Fact]
    public void ABlastDoesNotClimbALevelAndATypeWithoutTileDamageDetonatesNothing()
    {
        // The vertical decrement is 1000 for the flat explosion: a unit a level above is out of reach.
        var arena = Duel();
        var above = Extra(arena, 970, 20, 20);
        arena.Battle.SetTile(above, At(20, 20, 1));
        arena.Battle.Explode(default, Center(20, 20), 100, Blast, 6, true);
        Assert.Equal(100, above.Health);
        // The incendiary type converts nothing to tiles: the barrel stays.
        var fire = Duel();
        fire.Put(20, 20, 0, BattleTilePart.ObjectPart, Barrel);
        fire.Battle.Explode(default, Center(20, 20), 100, RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageIncendiary), 3, true);
        Assert.NotNull(fire.Map.MapData(fire.Tile(20, 20), BattleTilePart.ObjectPart));
    }

    [Fact]
    public void AWallStopsTheBlastAndAnOpenDoorLetsItThrough()
    {
        int Lost(Action<Arena>? setup)
        {
            var arena = Duel();
            for (var y = 12; y <= 28; y++) arena.Put(22, y, 0, BattleTilePart.WestWall, 0);
            for (var y = 12; y <= 28; y++) arena.Clear(22, y, 0, BattleTilePart.WestWall);
            setup?.Invoke(arena);
            var behind = Extra(arena, 970, 24, 20);
            arena.Battle.Explode(default, Center(20, 20), 100, Blast, 6, true);
            return 100 - behind.Health;
        }

        var open = Lost(null);
        var walled = Lost(arena =>
        {
            for (var y = 12; y <= 28; y++) arena.Put(22, y, 0, BattleTilePart.WestWall, BlastWall);
        });
        var doored = Lost(arena =>
        {
            for (var y = 12; y <= 28; y++) arena.Put(22, y, 0, BattleTilePart.WestWall, BlastDoor);
        });
        var doorOpen = Lost(arena =>
        {
            for (var y = 12; y <= 28; y++)
            {
                arena.Put(22, y, 0, BattleTilePart.WestWall, BlastDoor);
                arena.Map.SetDoorFrame(arena.Tile(22, y), BattleTilePart.WestWall, true);
            }
        });
        Assert.True(walled < open, $"{walled} < {open}");
        Assert.Equal(walled, doored);
        Assert.Equal(open, doorOpen);
    }

    [Fact]
    public void ABlastOutsideTheMapOrWithNoPowerChangesNothing()
    {
        var arena = Duel();
        var revision = arena.Battle.Revision;
        arena.Battle.Explode(default, new BattlePosition(-40, -40, 12), 100, Blast, 6, true);
        Assert.Equal(revision, arena.Battle.Revision);
        arena.Battle.Explode(default, Center(20, 20), 0, Blast, 6, true);
        Assert.Equal(100, arena.Alien.Health);
    }

    // ---- items and bodies on the ground ----

    [Fact]
    public void AnItemIsDestroyedWhereTheDamageToItemsPassesItsArmor()
    {
        var arena = Duel();
        var items = ItemDistances.Select(distance => arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CLIP"), At(20 + distance, 20))).ToArray();
        arena.Battle.Explode(default, Center(20, 20), 60, Blast, 6, true);
        // The item armor is 20 and the damage to items is the power reaching the tile: 60, 30, 20 (not above) and 10.
        Assert.Equal([false, false, true, true], items.Select(item => arena.Battle.Items.Contains(item)));
    }

    [Fact]
    public void ABodyOnTheGroundAndTheThingsAUnitCarriesTakeTheBlast()
    {
        var arena = Duel();
        var body = Extra(arena, 970, 20, 20);
        body.Health = 10;
        body.StunLevel = 3;
        var tile = body.Position!.Value;
        arena.Battle.SetTile(body, At(0, 0));
        arena.Map.SetUnitAt(arena.Tile(0, 0), null);
        body.Position = null;
        body.SavedPosition = tile;
        body.Status = BattleUnitStatus.Unconscious;
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = body.Id }, tile);
        arena.Battle.Explode(default, Center(20, 20), 100, Blast, 4, true);
        // The unconscious body is hit as a unit (the item itself survives: a hit unit is not an item destroyed).
        Assert.True(body.Health <= 0);
    }

    // ---- ExplosionBState ----

    [Fact]
    public void AGrenadeExplodesAtItsPowerAndRadiusKillsItsVictimsAndLeavesTheGame()
    {
        var arena = Armed();
        var victim = Extra(arena, 970, 21, 20);
        victim.Health = 50;
        var grenade = Grenade(arena, At(20, 20));
        var attack = new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade);
        arena.Battle.PushExplosion(attack, Center(20, 20));
        // init: the blast is dealt at once; the casualty check and the rest wait for the list to run.
        Assert.Equal(BattleUnitStatus.Standing, victim.Status);
        Assert.True(arena.Battle.HasPendingStates);
        var casualties = arena.Battle.RunStates();
        Assert.False(arena.Battle.HasPendingStates);
        // A power of 80 and a radius of 80 * 0.05 = 4.
        Assert.Equal([new BattleExploded(Center(20, 20), 80, 4, false)], arena.Battle.Events.Of<BattleExploded>());
        Assert.Equal((BattleUnitStatus.Dead, victim.Id), (victim.Status, Assert.Single(casualties).UnitId));
        Assert.DoesNotContain(grenade, arena.Battle.Items);
        // The grenade's thrower is the murderer.
        Assert.Equal(arena.Soldier.Id, victim.MurdererId);
        Assert.Single(arena.Soldier.Statistics.Kills);
    }

    [Fact]
    public void AnExplosionWithNoPowerLeavesTheListAtOnceAndKeepsTheItem()
    {
        var arena = Duel(grenade: "damageType: 3");
        var grenade = Grenade(arena, At(20, 20));
        arena.Battle.PushExplosion(new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade), Center(20, 20));
        // ExplosionBState::init pops a blast with nothing to give: no casualty check, no removal of the grenade.
        Assert.False(arena.Battle.HasPendingStates);
        Assert.Empty(arena.Battle.Events.Of<BattleExploded>());
        Assert.Contains(grenade, arena.Battle.Items);
    }

    [Fact]
    public void ABulletHitDealsItsDamageAtTheVoxelAndTheCasualtyCheckRunsAfterIt()
    {
        var arena = Duel(clip: "power: 500\ndamageType: 1\ndamageAlter: {RandomType: 3}");
        var (attack, _, _) = Shot(arena);
        arena.Soldier.Status = BattleUnitStatus.Aiming;
        arena.Battle.PushExplosion(attack, new BattlePosition(15 * 16 + 6, 10 * 16 + 8, 6), lowerWeapon: true);
        Assert.Equal((0, BattleUnitStatus.Standing), (arena.Alien.Health, arena.Alien.Status));
        var casualties = arena.Battle.RunStates();
        Assert.Equal(BattleUnitStatus.Dead, Assert.Single(casualties).Status);
        // The shooter puts the gun down, and nothing exploded.
        Assert.Equal(BattleUnitStatus.Standing, arena.Soldier.Status);
        Assert.Empty(arena.Battle.Events.Of<BattleExploded>());
    }

    [Fact]
    public void ThePowerComesFromTheWeaponWhenItIgnoresTheAmmunitionAndLosesTheRangeReduction()
    {
        var arena = Duel(gun: "ignoreAmmoPower: true\npower: 70\npowerRangeReduction: 8\npowerRangeThreshold: 1", clip: "power: 10\ndamageType: 3\ndamageAlter: {RandomType: 3}");
        var (attack, _, _) = Shot(arena);
        // A range of 48 voxels is 3 tiles, 2 past the threshold: 70 - 8 * 2 = 54. The radius is the ammunition's (10 * 0.05 is under 1, so 1).
        arena.Battle.PushExplosion(attack, Center(20, 20), range: 48);
        arena.Battle.RunStates();
        Assert.Equal(new BattleExploded(Center(20, 20), 54, 1, false), Assert.Single(arena.Battle.Events.Of<BattleExploded>()));
    }

    [Fact]
    public void AMissingBulletIsAHitOfNothing()
    {
        // A firearm item whose power is used up misses: the blast of a shell with no power would not start either (init's power test).
        var arena = Duel(gun: "ignoreAmmoPower: true\npower: 0", clip: "power: 40\ndamageType: 3");
        var (attack, _, _) = Shot(arena);
        arena.Battle.PushExplosion(attack, Center(15, 10));
        Assert.Equal(100, arena.Alien.Health);
        arena.Battle.RunStates();
        Assert.Empty(arena.Battle.Events.Of<BattleExploded>());
    }

    // ---- the chain of terrain explosions ----

    [Fact]
    public void ADestroyedBarrelExplodesAfterTheBlastAndSetsOffTheNextOne()
    {
        var arena = Armed();
        foreach (var x in ChainColumns) arena.Put(x, 20, 0, BattleTilePart.ObjectPart, Powder);
        // The fourth barrel is out of every blast's reach.
        arena.Put(28, 20, 0, BattleTilePart.ObjectPart, Powder);
        var grenade = Grenade(arena, At(20, 20));
        arena.Battle.PushExplosion(new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade), Center(20, 20));
        arena.Battle.RunStates();
        // The grenade reaches the barrel four tiles away (radius 4); each barrel's strength of 20 is a power of 20 and a radius of 2, which reaches the next barrel.
        Assert.Equal([false, true, true, true],
            arena.Battle.Events.Of<BattleExploded>().Select(static explosion => explosion.Terrain).Take(4));
        Assert.Equal([Center(20, 20), new BattlePosition(24 * 16 + 8, 20 * 16 + 8, 0), new BattlePosition(25 * 16 + 8, 20 * 16 + 8, 0), new BattlePosition(26 * 16 + 8, 20 * 16 + 8, 0)],
            arena.Battle.Events.Of<BattleExploded>().Select(static explosion => explosion.Center));
        Assert.All(ChainColumns, x => Assert.Null(arena.Map.MapData(arena.Tile(x, 20), BattleTilePart.ObjectPart)));
        Assert.NotNull(arena.Map.MapData(arena.Tile(28, 20), BattleTilePart.ObjectPart));
        Assert.Null(arena.Battle.CheckForTerrainExplosions());
    }

    [Fact]
    public void ATerrainExplosionGoesOffAtTheTilesStrengthWithTheDamageTypeOfItsKind()
    {
        // The explosive type 0 is high explosive, 5 incendiary, 6 stun, anything else smoke; the radius is a tenth of the strength.
        foreach (var kind in TerrainKinds)
        {
            var arena = Duel();
            arena.Put(20, 20, 0, BattleTilePart.Floor, BurnFloor);
            var unit = Extra(arena, 970, 20, 20);
            var tile = arena.Tile(20, 20);
            arena.Map.SetExplosive(tile, 30, kind);
            arena.Battle.RunTerrainExplosions();
            Assert.Equal(new BattleExploded(new(20 * 16, 20 * 16, 0), 30, 3, true), Assert.Single(arena.Battle.Events.Of<BattleExploded>()));
            Assert.Equal(0, arena.Map.Explosive(tile));
            switch (kind)
            {
                case 0:
                    Assert.True(unit.Health < 100);
                    break;
                case 5:
                    Assert.True(arena.Map.Fire(tile) > 0 || unit.Fire > 0);
                    break;
                case 6:
                    Assert.True(unit.StunLevel > 0);
                    Assert.Equal(100, unit.Health);
                    break;
                default:
                    Assert.True(arena.Map.Smoke(tile) > 0);
                    break;
            }
        }
    }

    [Fact]
    public void ATerrainExplosionsCasualtiesHaveNoMurdererFromTheirLastHit()
    {
        var arena = Duel();
        var victim = Extra(arena, 970, 20, 20);
        victim.Health = 5;
        victim.MurdererId = arena.Soldier.Id;
        arena.Map.SetExplosive(arena.Tile(20, 20), 60, 0);
        var casualties = arena.Battle.RunTerrainExplosions();
        Assert.Equal(BattleUnitStatus.Dead, Assert.Single(casualties).Status);
        // checkForCasualties(…, terrainExplosion): the last unit to hit the victim is not looked up as the murderer, so no kill is credited.
        Assert.Empty(arena.Soldier.Statistics.Kills);
    }

    // ---- TileEngine::detonate ----

    [Fact]
    public void ABlastBreaksTheWallsAroundATileOnlyWhereItsArmorIsReached()
    {
        // A wall of armor 30 breaks under an explosive of 30 or more on the tile west of it, and one of armor 255 never does.
        var arena = Duel();
        arena.Put(22, 20, 0, BattleTilePart.WestWall, ThinWall);
        arena.Put(22, 22, 0, BattleTilePart.WestWall, BlastWall);
        arena.Battle.Explode(default, Center(21, 20), 100, Blast, 2, true);
        arena.Battle.Explode(default, Center(21, 22), 100, Blast, 2, true);
        Assert.Null(arena.Map.MapData(arena.Tile(22, 20), BattleTilePart.WestWall));
        Assert.NotNull(arena.Map.MapData(arena.Tile(22, 22), BattleTilePart.WestWall));
        // Too weak a blast leaves the thin wall as it is.
        var weak = Duel();
        weak.Put(22, 20, 0, BattleTilePart.WestWall, ThinWall);
        weak.Battle.Explode(default, Center(21, 20), 40, Blast, 2, true);
        Assert.NotNull(weak.Map.MapData(weak.Tile(22, 20), BattleTilePart.WestWall));
    }

    [Fact]
    public void ADetonationCountsAnObjectiveAndASmokingTileAndNoDetonationHappensForAnEmptyExplosive()
    {
        var arena = Duel();
        Assert.False(arena.Battle.Detonate(arena.Tile(20, 20), 0));
        arena.Put(20, 20, 0, BattleTilePart.ObjectPart, Objective);
        arena.Battle.ObjectiveType = 7;
        arena.Battle.Random = new ScriptedDraws(0);
        Assert.True(arena.Battle.Detonate(arena.Tile(20, 20), 50));
        Assert.Null(arena.Map.MapData(arena.Tile(20, 20), BattleTilePart.ObjectPart));
        // The destroyed part makes smoke (at least 1) on a tile that is not burning.
        Assert.True(arena.Map.Smoke(arena.Tile(20, 20)) >= 1);
    }

    [Fact]
    public void AnObjectBurnsDownThroughItsDeadFormAndTheRemainingPowerSetsTheTileAlight()
    {
        var arena = Duel();
        // The fuse is destroyed (armor 0), its dead form (a floor part that explodes, armor 0) next; both leave the tile.
        arena.Put(20, 20, 0, BattleTilePart.Floor, Floor);
        arena.Put(20, 20, 0, BattleTilePart.ObjectPart, Fuse);
        arena.Battle.Detonate(arena.Tile(20, 20), 100);
        Assert.Null(arena.Map.MapData(arena.Tile(20, 20), BattleTilePart.ObjectPart));
        Assert.Equal(20, arena.Map.Explosive(arena.Tile(20, 20)));
        // A flammable part with power to spare burns: the crate (flammable 10, fuel 4) survives 50 and burns.
        var burn = Duel();
        burn.Put(20, 20, 0, BattleTilePart.Floor, Floor);
        burn.Put(20, 20, 0, BattleTilePart.ObjectPart, Crate);
        burn.Battle.Detonate(burn.Tile(20, 20), 50);
        Assert.NotNull(burn.Map.MapData(burn.Tile(20, 20), BattleTilePart.ObjectPart));
        Assert.True(burn.Map.Fire(burn.Tile(20, 20)) > 0);
    }

    [Fact]
    public void ABaseModuleThatIsBlastedIsCountedAndOneInABlockTheSavedMapLacksIsDestroyedAllTheSame()
    {
        var arena = Duel();
        typeof(BattleState).GetField("<MissionType>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(arena.Battle, "STR_BASE_DEFENSE");
        var grid = Oxce.Gameplay.Battles.Generation.BattleBaseModule.EmptyGrid(30, 30);
        grid[2][2] = new(3, 3);
        typeof(BattleState).GetField("<BaseModules>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(arena.Battle, grid);
        arena.Put(25, 25, 0, BattleTilePart.ObjectPart, BaseModule);
        arena.Battle.Explode(default, Center(25, 25), 40, Blast, 1, true);
        Assert.Null(arena.Map.MapData(arena.Tile(25, 25), BattleTilePart.ObjectPart));
        Assert.Equal(2, arena.Battle.BaseModules[2][2].Remaining);
        // A saved module map smaller than the battle: the count is skipped (ADR 0041).
        typeof(BattleState).GetField("<BaseModules>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(arena.Battle, Oxce.Gameplay.Battles.Generation.BattleBaseModule.EmptyGrid(10, 10));
        arena.Put(26, 26, 0, BattleTilePart.ObjectPart, BaseModule);
        arena.Battle.Explode(default, Center(26, 26), 40, Blast, 1, true);
        Assert.Null(arena.Map.MapData(arena.Tile(26, 26), BattleTilePart.ObjectPart));
    }

    // ---- self-destruct ----

    private static Arena Bomber(string grenadeExtra = "", Func<string, string>? more = null) =>
        Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n", grenade: BlastGrenade + "60" + (grenadeExtra.Length == 0 ? "" : "\n" + grenadeExtra),
            more: more);

    [Fact]
    public void AUnitThatBlowsItselfUpAsItDiesHurtsThoseAroundItAndDiesOfIt()
    {
        var arena = Bomber();
        var near = Extra(arena, 970, 16, 10);
        var far = Extra(arena, 971, 20, 10);
        Hurt(arena, 500, Plain, attack: new BattleAttack(BattleActionType.AimedShot, arena.Soldier));
        Assert.True(arena.Battle.HasPendingStates);
        var casualties = arena.Battle.RunStates();
        // The special weapon is the grenade: a power of 60 and a radius of 3 around the corner of the unit's tile.
        Assert.Equal([new BattleExploded(new(15 * 16, 10 * 16, 0), 60, 3, false)], arena.Battle.Events.Of<BattleExploded>());
        Assert.Contains(casualties, casualty => casualty.UnitId == arena.Alien.Id && casualty.Status == BattleUnitStatus.Dead);
        Assert.True(near.Health < 100);
        Assert.Equal(100, far.Health);
        // The flag is clear again after the explosion started, so another hit would explode it once more.
        Assert.False(arena.Alien.AlreadyExploded);
    }

    [Fact]
    public void AMeleeSelfDestructItemLeavesARefusalForTheCommandAndAPsiAmpOneIsAPlainHit()
    {
        var melee = Bomber(more: rules => Replaced(rules, "- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    clipSize: -1\n    battleType: 3"));
        Hurt(melee, 500, Plain);
        Assert.Equal("battle.self-destruct-item-unsupported", melee.Battle.TakeExplosionRefusal()!.Code);
        Assert.Null(melee.Battle.TakeExplosionRefusal());
        var psi = Bomber(more: rules => Replaced(rules, "- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    battleType: 9"));
        var near = Extra(psi, 970, 16, 10);
        Assert.Null(Hurt(psi, 500, Plain).Refusal);
        psi.Battle.RunStates();
        // The amplifier's explosion is a hit at the unit's corner voxel, not an area blast.
        Assert.Empty(psi.Battle.Events.Of<BattleExploded>());
        Assert.Equal(100, near.Health);
        Assert.Null(psi.Battle.TakeExplosionRefusal());
    }

    [Fact]
    public void ASelfDestructThatFailsItsChanceDoesNothing()
    {
        var arena = Bomber(grenadeExtra: "specialChance: 0");
        Hurt(arena, 500, Plain);
        arena.Battle.RunStates();
        Assert.DoesNotContain(arena.Battle.Events.Of<BattleExploded>(), static explosion => explosion.Power > 0);
    }

    [Fact]
    public void ASelfDestructItemThatIsAMeleeWeaponIsRefusedByName()
    {
        var arena = Bomber(more: rules => Replaced(rules, "- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    clipSize: -1\n    battleType: 3"));
        Assert.Equal("battle.self-destruct-item-unsupported", Hurt(arena, 500, Plain).Refusal!.Code);
    }

    [Fact]
    public void AnExplosionThatKillsAUnitWithASelfDestructPushesItBehindTheRunningStates()
    {
        // A blast kills the bomber (a type that does not ignore self-destruct): its own explosion waits its turn behind the first, and runs.
        var arena = Bomber();
        var near = Extra(arena, 970, 16, 10);
        var blast = Blast with { IgnoreSelfDestruct = false };
        arena.Battle.Explode(default, Center(15, 10), 100, blast, 3, true);
        Assert.True(arena.Battle.HasPendingStates);
        arena.Battle.RunStates();
        Assert.False(arena.Battle.HasPendingStates);
        Assert.Single(arena.Battle.Events.Of<BattleExploded>());
        Assert.True(near.Health < 100);
    }


    // ---- the branches the first audit found untested ----

    [Fact]
    public void AnExplosionQueuedDuringAWalkRunsBeforeTheFallsItCauses()
    {
        var arena = Duel();
        arena.Put(20, 20, 1, BattleTilePart.Floor, Floor);
        var standing = Extra(arena, 970, 20, 20);
        arena.Battle.SetTile(standing, At(20, 20, 1));
        arena.Battle.UpdateTileFloorState(standing);
        Assert.False(standing.HaveNoFloorBelow);
        // A blast is on the list when the walk ends; its think sets off the strength left on the tile below the unit, which breaks the floor under it.
        arena.Map.SetExplosive(arena.Tile(20, 20), 40, 0);
        arena.Battle.PushExplosion(default, Center(5, 25));
        Assert.Null(arena.Battle.Walk(arena.Soldier.Id, At(11, 10), BattleMoveType.Normal, false));
        // The walk resolved the list first and the fall afterwards: the unit is down and no fall is left.
        Assert.Equal(At(20, 20), standing.Position);
        Assert.Empty(arena.Battle.FallingUnits);
    }

    [Fact]
    public void ADiagonalBigWallLetsTheBlastOutOnTheSideTheCenterIsOn()
    {
        int Lost(int offset)
        {
            var arena = Duel();
            arena.Put(20, 20, 0, BattleTilePart.ObjectPart, DiagWall);
            var northWest = Extra(arena, 970, 18, 18);
            arena.Battle.Explode(default, new BattlePosition(20 * 16 + offset, 20 * 16 + offset, 12), 100, Blast, 6, true);
            return 100 - northWest.Health;
        }

        // Centered on the north-west side of the wall the rays going north-west skip it; from the south-east side the wall is in their way.
        Assert.True(Lost(2) > Lost(14), $"{Lost(2)} > {Lost(14)}");
    }

    [Fact]
    public void TheBlockTypeFollowsTheResistTypeOfTheDamage()
    {
        // A wall of high explosive block 40 stops a blast of the high explosive type, but not smoke (its own block) and not a blast of no type (the vision block).
        int Lost(RuntimeItemDamageRule type)
        {
            var arena = Duel();
            for (var y = 12; y <= 28; y++) arena.Put(22, y, 0, BattleTilePart.WestWall, BlastWall);
            var behind = Extra(arena, 970, 23, 20);
            arena.Battle.Explode(default, Center(20, 20), 60, type, 6, true);
            return 100 - behind.Health;
        }

        var none = Blast with { ResistType = RuntimeItemDamageRule.DamageNone };
        Assert.True(Lost(none) > Lost(Blast), $"{Lost(none)} > {Lost(Blast)}");
        var smoke = Duel();
        for (var y = 12; y <= 28; y++) smoke.Put(22, y, 0, BattleTilePart.WestWall, BlastWall);
        smoke.Battle.Explode(default, Center(20, 20), 60, RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageSmoke), 6, true);
        // Smoke is stopped by any wall part whatever its blast block (the smoke branch of the blockage), and goes through open ground.
        Assert.Equal(0, smoke.Map.Smoke(smoke.Tile(23, 20)));
        var open = Duel();
        open.Battle.Explode(default, Center(20, 20), 60, RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageSmoke), 6, true);
        Assert.True(open.Map.Smoke(open.Tile(23, 20)) > 0);
    }

    [Fact]
    public void AUnitKilledWithOverkillLosesWhatItCarriesToTheBlast()
    {
        var arena = Duel();
        var (_, gun, clip) = Shot(arena);
        arena.Battle.Explode(default, Center(10, 10), 400, Blast with { IgnoreOverKill = false }, 2, true);
        // The overkill (health far below zero) is the damage to its items: the gun and the clip in it are over their armor of 20.
        Assert.DoesNotContain(gun, arena.Battle.Items);
        Assert.DoesNotContain(clip, arena.Battle.Items);
    }

    [Fact]
    public void ASpawnedTankIsArmedAndAnAttackerlessSpawnFollowsTheItemsOwner()
    {
        var arena = Duel(grenade: "spawnUnit: ALIEN\nspawnUnitChance: 100",
            more: rules => Replaced(rules, "  - type: GRENADE\n", "  - type: ALIEN\n    battleType: 1\n    fixedWeapon: true\n    clipSize: -1\n    weight: 1\n  - type: GRENADE\n"));
        var grenade = Grenade(arena, At(22, 22));
        grenade.PreviousOwnerUnitId = arena.Soldier.Id;
        // No attacker: the owner is the item's previous owner (an X-COM soldier, so the faction -1 follows it), and the facing is the second draw.
        arena.Battle.Random = new ScriptedDraws(0, 5);
        Assert.True(arena.Battle.SpawnNewUnit(new BattleAttack(BattleActionType.None, null, grenade, grenade), At(22, 22)));
        var spawned = arena.Battle.Units[^1];
        Assert.Equal((BattleFaction.Player, 5, arena.Soldier.Id), (spawned.Faction, spawned.Direction, spawned.PreviousOwnerId));
        Assert.True(arena.Battle.Items.Any(item => item.ItemType == "ALIEN" && item.OwnerUnitId == spawned.Id),
            string.Join(";", arena.Battle.Items.Select(item => $"{item.ItemType}/{item.OwnerUnitId}/{item.Slot}")) + " unit " + spawned.Id + " " + spawned.UnitType);
    }

    [Fact]
    public void ASpawnedGlowingItemLightsItsTile()
    {
        var arena = Flat(shade: 15, edit: rules => Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1",
            "- type: FLARE\n    battleType: 10\n    weight: 1\n    power: 9\n  - type: GRENADE\n    battleType: 4\n    weight: 1\n    spawnItem: FLARE\n    spawnItemChance: 100"));
        arena.Battle.CalculateLighting(BattleLightLayer.Ambient, null, 0, true);
        var grenade = Grenade(arena, At(22, 22));
        var before = arena.Battle.TileShade(At(22, 22));
        Assert.True(arena.Battle.SpawnNewItem(new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade), At(22, 22)));
        Assert.True(arena.Battle.TileShade(At(22, 22)) < before);
    }

    [Fact]
    public void TheGeneratorsLeftoverTerrainBlastsUseTheirOwnTypes()
    {
        // A strength of 30 of the stun kind: the power sources' routine blasts every leftover as high explosive, the junk routine by the kind.
        var sources = Duel();
        var hurt = Extra(sources, 970, 20, 20);
        sources.Map.SetExplosive(sources.Tile(20, 20), 30, 6);
        sources.Battle.ExplodePowerSources();
        Assert.True(hurt.Health < 100);
        var junk = Duel();
        var other = Extra(junk, 970, 20, 20);
        junk.Map.SetExplosive(junk.Tile(20, 20), 30, 6);
        junk.Battle.ExplodeOtherJunk();
        Assert.Equal((100, true), (other.Health, other.StunLevel > 0));
    }

    [Fact]
    public void ABombersExplosionWaitsBehindTheRunningBlastAndItsDeath()
    {
        var arena = Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n",
            grenade: "damageType: 3\ndamageAlter: {RandomType: 3, IgnoreSelfDestruct: false}\npower: 60");
        arena.Alien.Health = 30;
        var grenade = Grenade(arena, At(15, 10));
        // The blast kills the bomber in its init: the bomber's own explosion is pushed behind the running state and is not started yet.
        arena.Battle.PushExplosion(new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade), Center(15, 10));
        Assert.Single(arena.Battle.Events.Of<BattleExploded>());
        Assert.True(arena.Alien.AlreadyExploded);
        Assert.Equal(BattleUnitStatus.Standing, arena.Alien.Status);
        arena.Battle.RunStates();
        // Then the blast's think queued the bomber's death in front of it, and the bomber's explosion went off last.
        Assert.Equal(2, arena.Battle.Events.Of<BattleExploded>().Count);
        Assert.Equal((BattleUnitStatus.Dead, false), (arena.Alien.Status, arena.Alien.AlreadyExploded));
    }

    // ---- spawns ----

    [Fact]
    public void AnExplosionSpawnsItsUnitAndItemWhereTheProjectileWasBeforeTheImpact()
    {
        var arena = Duel(grenade: "spawnUnit: ALIEN\nspawnUnitFaction: 1\nspawnUnitChance: 100\nspawnItem: CLIP\nspawnItemChance: 100");
        var grenade = Grenade(arena, At(22, 22));
        var units = arena.Battle.Units.Count;
        arena.Battle.PushExplosion(new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade), Center(22, 22), Center(22, 23));
        arena.Battle.RunStates();
        var spawned = Assert.Single(arena.Battle.Units.Skip(units));
        Assert.Equal(("ALIEN", BattleFaction.Hostile), (spawned.UnitType, spawned.Faction));
        // The thrower's direction (facing a throw's way) is the new unit's, and the tile is the one before the impact.
        Assert.Equal(At(22, 23), spawned.Position);
        Assert.Equal(arena.Soldier.Direction, spawned.Direction);
        Assert.Contains(arena.Battle.Items, item => item.ItemType == "CLIP" && item.Tile == At(22, 23) && item.PreviousOwnerUnitId == arena.Soldier.Id);
        Assert.DoesNotContain(grenade, arena.Battle.Items);
    }

    [Fact]
    public void ASpawnWithoutTheChanceOrWithTheWeaponsOverrideFollowsTheDraw()
    {
        var arena = Duel(grenade: "spawnUnit: ALIEN\nspawnUnitChance: 0");
        var grenade = Grenade(arena, At(22, 22));
        var units = arena.Battle.Units.Count;
        arena.Battle.PushExplosion(new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade), Center(22, 22));
        arena.Battle.RunStates();
        Assert.Equal(units, arena.Battle.Units.Count);
        // The weapon's action carries an override of the ammunition's chance: aimed shots of this gun always spawn.
        var overridden = Duel(gun: "confAimed: {ammoSpawnUnitChanceOverride: 100}", clip: "spawnUnit: ALIEN\nspawnUnitChance: 0\npower: 30\ndamageType: 3\ndamageAlter: {RandomType: 3}");
        var (attack, _, _) = Shot(overridden);
        var before = overridden.Battle.Units.Count;
        overridden.Battle.PushExplosion(attack, Center(22, 22));
        overridden.Battle.RunStates();
        Assert.Equal(before + 1, overridden.Battle.Units.Count);
    }

    // ---- the generator's explosions ----

    [Fact]
    public void ACrashedUfosPowerSourcesExplodeAtTheirChanceWithTheirPowerAndTheUnitsTheyTakeOutDieWhenTheBattleOpens()
    {
        var arena = Duel();
        // Draws: the 75 percent test passes, the power is 180 + 0, then the blast rolls its draws (the scripted source gives 0, the minimum, after its list).
        arena.Put(16, 10, 0, BattleTilePart.ObjectPart, 2);
        arena.Put(2, 2, 0, BattleTilePart.ObjectPart, 2);
        arena.Battle.Random = new ScriptedDraws(0, 0, 99);
        arena.Battle.ExplodePowerSources();
        // The sources go off in tile order, each at a power of 180 to 250 at the middle of the tile's height with the radius of 10.
        Assert.Equal([new BattleExploded(new(2 * 16, 2 * 16, 12), 180, 10, false)], arena.Battle.Events.Of<BattleExploded>().Take(1));
        Assert.Equal(2, arena.Battle.Events.Of<BattleExploded>().Count);
        // The source objects (armor 4) are blown away, and the alien next to the second one is hurt (its death is the battlescape's opening check).
        Assert.Null(arena.Map.MapData(arena.Tile(2, 2), BattleTilePart.ObjectPart));
        Assert.Null(arena.Map.MapData(arena.Tile(16, 10), BattleTilePart.ObjectPart));
        Assert.True(arena.Alien.Health < 100);
    }

    [Fact]
    public void ADamagedBodyOfAPowerSourceExplosionIsTheBattlescapesOpeningCasualtyCheck()
    {
        var arena = Duel();
        arena.Alien.Health = 0;
        // OpenBattlescape: checkForCasualties as a hidden explosion; before the game a dying unit becomes its corpse at once.
        arena.Battle.IsBeforeGame = true;
        arena.Battle.OpenBattlescape();
        Assert.Equal(BattleUnitStatus.Dead, arena.Alien.Status);
    }

    [Fact]
    public void MapGrenadesPrimedForTheStartExplodeWithTheirOwnPowerAndLeaveTheGame()
    {
        var arena = Armed();
        var junk = Grenade(arena, At(20, 20));
        junk.FuseTimer = -2;
        var kept = Grenade(arena, At(5, 5));
        kept.FuseTimer = -1;
        var victim = Extra(arena, 970, 21, 20);
        arena.Battle.ExplodeOtherJunk();
        Assert.DoesNotContain(junk, arena.Battle.Items);
        Assert.Contains(kept, arena.Battle.Items);
        Assert.Equal([new BattleExploded(new(20 * 16 + 8, 20 * 16 + 8, 0), 80, 4, false)], arena.Battle.Events.Of<BattleExploded>());
        Assert.True(victim.Health < 100);
        Assert.False(arena.Battle.HasPendingStates);
    }

    [Fact]
    public void AnItemThatSpawnsOrBelongsToXcomIsNotMapJunk()
    {
        var arena = Armed("spawnUnit: ALIEN");
        var junk = Grenade(arena, At(20, 20));
        junk.FuseTimer = -2;
        arena.Battle.ExplodeOtherJunk();
        Assert.Contains(junk, arena.Battle.Items);
        Assert.Empty(arena.Battle.Events.Of<BattleExploded>());
        var xcom = Armed();
        var own = Grenade(xcom, At(20, 20));
        own.FuseTimer = -2;
        own.XcomProperty = true;
        xcom.Battle.ExplodeOtherJunk();
        Assert.Contains(own, xcom.Battle.Items);
    }
}
