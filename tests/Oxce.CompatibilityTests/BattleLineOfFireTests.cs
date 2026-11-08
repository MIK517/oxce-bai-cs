using System.Text;
using Oxce.Gameplay.Battles;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S2: line of fire and accuracy (<c>src/Battlescape/TileEngine.cpp</c>: <c>getOriginVoxel</c>, <c>canTargetUnit</c>,
/// <c>canTargetTile</c>, <c>adjustTargetVoxelFromTileType</c>, <c>calculateParabolaVoxel</c>, <c>validateThrow</c>, <c>isTileInLOS</c>;
/// <c>ProjectileFlyBState</c>'s <c>validThrowRange</c>, <c>getMaxThrowDistance</c> and target voxel; <c>Projectile</c>'s <c>calculateTrajectory</c>,
/// <c>calculateThrow</c>, <c>applyAccuracy</c> and <c>getHitChance</c>; <c>BattleUnit::getFiringAccuracy</c> and <c>getAccuracyModifier</c>;
/// <c>RuleItem::isOutOfRange</c>, <c>calculateLimits</c>; <c>SavedBattleGame::canUseWeapon</c>) on the flat arena of the sight tests, where the
/// soldier stands at (10, 10) facing east, the alien five tiles east of it, and both are 22 voxels tall (14 kneeling).
/// </summary>
public sealed class BattleLineOfFireTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";
    private const string ClipText = "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    weight: 1";
    private const string GrenadeText = "- type: GRENADE\n    battleType: 4\n    weight: 1";
    private const string ResearchText = "\nresearch:\n  - name: RES_GUN\n    cost: 10\n";

    private static Func<string, string> Edit(string gun = "", string grenade = "", string global = "", string clip = "") => rules =>
    {
        if (gun.Length > 0) rules = Replaced(rules, GunText, GunText + "\n    " + gun.Replace("\n", "\n    ", StringComparison.Ordinal));
        if (clip.Length > 0) rules = Replaced(rules, ClipText, ClipText + "\n    " + clip.Replace("\n", "\n    ", StringComparison.Ordinal));
        if (grenade.Length > 0) rules = Replaced(rules, GrenadeText, GrenadeText + "\n    " + grenade.Replace("\n", "\n    ", StringComparison.Ordinal));
        return global.Length > 0 ? rules + "\n" + global + "\n" : rules;
    };

    /// <summary>The soldier's own gear is replaced by a GUN in the right hand (a loaded clip) and, if asked, a GRENADE in the left.</summary>
    private static (Arena Arena, BattleItem Gun, BattleItem? Grenade) Armed(int distance = 5, string gun = "", string grenade = "", string global = "", string clip = "",
        bool leftHand = false, string alienExtra = "", string soldierExtra = "", Func<string, string>? more = null)
    {
        var edit = Edit(gun, grenade, global, clip);
        var arena = Flat(distance: distance, edit: more is null ? edit : rules => more(edit(rules)), alienExtra: alienExtra, soldierExtra: soldierExtra);
        foreach (var item in arena.Battle.InventoryOf(arena.Soldier).ToList()) arena.Battle.RemoveItem(item);
        var weapon = GunFor(arena.Battle, arena.Soldier);
        BattleItem? thrown = null;
        if (leftHand)
        {
            thrown = new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { OwnerUnitId = arena.Soldier.Id, Slot = "STR_LEFT_HAND" };
            arena.Battle.AddItem(thrown);
        }
        return (arena, weapon, thrown);
    }

    private static readonly BattleViewer Player = BattleViewer.LocalPlayer;

    private static BattleAimView AimAt(Arena arena, BattleItem weapon, BattleAimKind kind, int x, int y = 10, int z = 0, bool force = false)
    {
        var tile = arena.Tile(x, y, z);
        arena.Map.SetDiscovered(tile, Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, true, BattleFaction.Player);
        return arena.Battle.AimView(arena.Rules, BattleResearchGate.Everything, Player, arena.Soldier.Id, weapon.Id, kind, new BattlePosition(x, y, z), force)!;
    }

    // ---- origins ----

    [Fact]
    public void TheOriginVoxelIsTheUnitsTopLessTheWeaponShiftAndOffsetTowardsTheTarget()
    {
        var (arena, gun, _) = Armed();
        var sight = arena.Battle.Sight();
        var shot = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        // Standing at (10, 10): 22 tall less 4 is 18; east is direction 2, shifted (15, 8) in the tile.
        Assert.Equal(new BattlePosition(10 * 16 + 15, 10 * 16 + 8, 18), sight.OriginVoxel(shot));
        // A throw is taken 3 below the top.
        Assert.Equal(new BattlePosition(10 * 16 + 15, 10 * 16 + 8, 19), sight.OriginVoxel(new BattleAimAction(arena.Soldier, BattleActionType.Throw, gun, shot.Target)));
        // A third of the way to the next direction on either side: (2 * 15 + 15 + 1) / 3 = 15 and (2 * 8 + 15 + 1) / 3 = 10; (2 * 15 + 14 + 1) / 3 = 15, (2 * 8 + 1 + 1) / 3 = 6.
        shot.RelativeOrigin = BattleActionOrigin.Right;
        Assert.Equal(new BattlePosition(10 * 16 + 15, 10 * 16 + 10, 18), sight.OriginVoxel(shot));
        shot.RelativeOrigin = BattleActionOrigin.Left;
        Assert.Equal(new BattlePosition(10 * 16 + 15, 10 * 16 + 6, 18), sight.OriginVoxel(shot));
        // A kneeling unit is 14 tall; the voxel is 14 - 4 = 10.
        arena.Soldier.Kneeled = true;
        shot.RelativeOrigin = BattleActionOrigin.Centre;
        Assert.Equal(10, sight.OriginVoxel(shot).Z);
        // A launch from a waypoint's tile starts in the middle of it, a third up.
        var launch = new BattleAimAction(arena.Soldier, BattleActionType.Launch, gun, shot.Target);
        Assert.Equal(new BattlePosition(12 * 16 + 8, 12 * 16 + 8, 16), sight.OriginVoxel(launch, new BattlePosition(12, 12, 0)));
        // From the unit's own tile it is a unit's origin.
        arena.Soldier.Kneeled = false;
        Assert.Equal(18, sight.OriginVoxel(launch, arena.Soldier.RequiredPosition).Z);
    }

    // ---- the line of fire ----

    [Fact]
    public void ATargetBehindAWallHasNoLineOfFireAndOneInTheOpenHas()
    {
        var (arena, gun, _) = Armed();
        Assert.True(AimAt(arena, gun, BattleAimKind.AimedShot, 15).HasLineOfFire);
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        arena.See();
        var blocked = AimAt(arena, gun, BattleAimKind.AimedShot, 15);
        Assert.False(blocked.HasLineOfFire);
        // A tile before the wall is still in the open.
        Assert.True(AimAt(arena, gun, BattleAimKind.AimedShot, 11).HasLineOfFire);
        // The force fire flag (Ctrl) aims at the tile's middle without a check of the line.
        Assert.True(AimAt(arena, gun, BattleAimKind.AimedShot, 15, force: true).HasLineOfFire);
    }

    [Fact]
    public void AUnitHalfInCoverIsHitOnAnExposedVoxel()
    {
        var (arena, gun, _) = Armed();
        arena.Soldier.Kneeled = true; // the eye at 14 - 4 = 10 is below the top of the wall (12)
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, LowWall);
        arena.See();
        var sight = arena.Battle.Sight();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        var origin = sight.OriginVoxel(action);
        var targetTile = arena.Tile(15, 10);
        Assert.True(sight.CanTargetUnit(origin, targetTile, out var scan, arena.Soldier));
        // The middle of the target (z 11) is behind the wall; the scan found a voxel above it, which the line reaches as a unit.
        Assert.True(scan.Z > 12, $"the scan voxel is at {scan.Z}");
        var trajectory = new List<BattlePosition>();
        Assert.Equal(BattleVoxelType.Unit, sight.CalculateLineVoxel(origin, scan, false, trajectory, arena.Soldier));
        // The target voxel the shot chooses is that one, and its line of fire passes the first check.
        var choice = arena.Battle.ChooseTargetVoxel(arena.Rules, action, arena.Soldier.RequiredPosition);
        Assert.Equal(scan, choice.Voxel);
    }

    [Fact]
    public void AScanThatFindsNothingRemembersTheObstacleOfEachLine()
    {
        var (arena, gun, _) = Armed();
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        arena.See();
        var sight = arena.Battle.Sight();
        var origin = sight.OriginVoxel(new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0)));
        var obstacles = new List<BattleObstacle>();
        Assert.False(sight.CanTargetUnit(origin, arena.Tile(15, 10), out _, arena.Soldier, null, obstacles));
        Assert.NotEmpty(obstacles);
        Assert.All(obstacles, obstacle => Assert.Equal((arena.Tile(12, 10), BattleVoxelType.Object), (obstacle.Tile, obstacle.Type)));
        // The artificial intelligence's scan does not remember them.
        arena.Alien.SetDirection(6);
        var forgot = new List<BattleObstacle>();
        Assert.False(sight.CanTargetUnit(origin, arena.Tile(15, 10), out _, arena.Alien, null, forgot));
        Assert.Empty(forgot);
    }

    [Fact]
    public void TheTargetVoxelIsTheUnitsThenTheObjectsThenTheFloorsAndNeverTheShootersOwn()
    {
        var (arena, gun, _) = Armed();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        var origin = arena.Soldier.RequiredPosition;
        var corner = new BattlePosition(15 * 16, 10 * 16, 0);
        var sight = arena.Battle.Sight();
        // A seen unit: a voxel inside it that the line reaches.
        var unit = arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin);
        Assert.NotNull(unit.Voxel);
        Assert.Equal(BattleVoxelType.Unit, sight.CalculateLineVoxel(unit.OriginVoxel, unit.Voxel!.Value, false, [], arena.Soldier));
        // Without the unit, a floor alone is aimed at its low centre; an object on it, at a voxel of the object.
        arena.Battle.SetTile(arena.Alien, new BattlePosition(27, 0, 0));
        arena.See();
        var floor = arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin);
        Assert.Equal(new BattlePosition(corner.X + 8, corner.Y + 8, 2), floor.Voxel);
        arena.Put(15, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        var solid = arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin);
        Assert.NotEqual(floor.Voxel, solid.Voxel);
        var trajectory = new List<BattlePosition>();
        Assert.Equal(BattleVoxelType.Object, sight.CalculateLineVoxel(solid.OriginVoxel, solid.Voxel!.Value, false, trajectory, arena.Soldier));
        Assert.Equal((15, 10, 0), (trajectory[0].X / 16, trajectory[0].Y / 16, trajectory[0].Z / 24));
        // A shot at the shooter's own tile goes to the floor under it.
        var own = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, arena.Soldier.RequiredPosition);
        var self = arena.Battle.ChooseTargetVoxel(arena.Rules, own, arena.Soldier.RequiredPosition);
        Assert.True(self.SelfShot);
        Assert.Equal(new BattlePosition(10 * 16 + 8, 10 * 16 + 8, 0), self.Voxel);
    }

    [Fact]
    public void AWallPartIsAimedAtAlongItsOwnEdgeAndTheObjectComesFirst()
    {
        var (arena, gun, _) = Armed();
        arena.Battle.SetTile(arena.Alien, new BattlePosition(27, 0, 0));
        arena.See();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        var origin = arena.Soldier.RequiredPosition;
        var corner = new BattlePosition(15 * 16, 10 * 16, 0);
        // A west wall is scanned along the tile's west edge (x 0), a north wall along its north edge (y 0).
        arena.Put(15, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.WestWall, Wall);
        Assert.Equal(corner.X, arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin).Voxel!.Value.X);
        arena.Clear(15, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.WestWall);
        arena.Put(15, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.NorthWall, Wall);
        Assert.Equal(corner.Y, arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin).Voxel!.Value.Y);
        // With an object as well the object is aimed at: its spiral starts in the middle of the tile, so its voxel is not on the wall's edge.
        arena.Put(15, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        var voxel = arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin).Voxel!.Value;
        Assert.NotEqual(corner.Y, voxel.Y);
        Assert.Equal(15, voxel.X / 16);
        // A tile whose wall the origin cannot reach falls back to the middle of that edge (8, 0, 9).
        arena.Clear(15, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart);
        arena.Put(14, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        Assert.Equal(new BattlePosition(corner.X + 8, corner.Y, 9), arena.Battle.ChooseTargetVoxel(arena.Rules, action, origin).Voxel);
    }

    [Fact]
    public void AForcedShotAndALaunchAimAtTheTilesMiddleAndAnArcingShotIsLeftToItsArc()
    {
        var (arena, gun, _) = Armed(gun: "arcingShot: true");
        var target = new BattlePosition(15, 10, 0);
        var forced = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, target) { ForceFire = true };
        var choice = arena.Battle.ChooseTargetVoxel(arena.Rules, forced, arena.Soldier.RequiredPosition);
        Assert.Equal(new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 12), choice.Voxel);
        var launch = new BattleAimAction(arena.Soldier, BattleActionType.Launch, gun, target);
        Assert.Equal(new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 16), arena.Battle.ChooseTargetVoxel(arena.Rules, launch, arena.Soldier.RequiredPosition).Voxel);
        Assert.Equal(new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 2), arena.Battle.ChooseTargetVoxel(arena.Rules, launch, arena.Soldier.RequiredPosition, true).Voxel);
        var arcing = arena.Battle.ChooseTargetVoxel(arena.Rules, new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, target), arena.Soldier.RequiredPosition);
        Assert.True(arcing.Arcing);
        Assert.Null(arcing.Voxel);
    }

    // ---- throwing ----

    [Fact]
    public void AThrowGoesOverAWallByAHigherCurveAndNowhereUnderACeiling()
    {
        var (arena, _, grenade) = Armed(grenade: "tuThrow: 25", leftHand: true);
        var sight = arena.Battle.Sight();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.Throw, grenade!, new BattlePosition(15, 10, 0));
        var origin = sight.OriginVoxel(action);
        var aim = new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 1);
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out var open, out _, false));
        Assert.True(AimAt(arena, grenade!, BattleAimKind.Throw, 15).HasLineOfFire);
        // Over a low wall the first curve still works.
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, LowWall);
        arena.See();
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out var low, out _, false));
        Assert.Equal(open, low);
        // Over a wall of the whole level a higher curve is tried, half a step at a time.
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        arena.See();
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out var high, out _, false));
        Assert.True(high > low);
        Assert.Equal(0.0, (high - low) % 0.5, 9);
        Assert.True(AimAt(arena, grenade!, BattleAimKind.Throw, 15).HasLineOfFire);
        // A floor over the path (the second level) stops every arc that climbs over the wall.
        for (var x = 10; x <= 15; x++) arena.Put(x, 10, 1, Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, Wall);
        arena.See();
        var obstacles = new List<BattleObstacle>();
        Assert.False(sight.ValidateThrow(action, origin, aim, 0, out _, out _, false, obstacles));
        Assert.NotEmpty(obstacles); // the player's unit remembers where each arc ended
        Assert.False(AimAt(arena, grenade!, BattleAimKind.Throw, 15).HasLineOfFire);
        // A forced throw still has the curve of the first try.
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out var forced, out _, true));
        Assert.Equal(open, forced);
    }

    [Fact]
    public void AThrowIsLimitedByTheItemsThrowRangeAndTheThrowersStrength()
    {
        var (arena, _, grenade) = Armed(grenade: "tuThrow: 25\nthrowRange: 4", leftHand: true);
        var tooFar = AimAt(arena, grenade!, BattleAimKind.Throw, 15);
        Assert.False(tooFar.InRange);
        Assert.Equal(0, tooFar.Accuracy);
        Assert.False(tooFar.HasLineOfFire);
        Assert.True(AimAt(arena, grenade!, BattleAimKind.Throw, 14).InRange);
        // getMaxThrowDistance: the weight, the strength and the height difference. A feather goes far; a heavy item by a weak unit, not as far.
        Assert.True(BattleSight.MaxThrowDistance(1, 40, -18) > BattleSight.MaxThrowDistance(10, 40, -18));
        Assert.True(BattleSight.MaxThrowDistance(3, 40, 20) > BattleSight.MaxThrowDistance(3, 40, -20));
        // A unit of no strength would divide by zero; it counts a strength of 1.
        Assert.Equal(BattleSight.MaxThrowDistance(3, 1, 0), BattleSight.MaxThrowDistance(3, 0, 0));
    }

    [Fact]
    public void ThrowingAtAnObjectThatBlocksWalkingIsRefusedAndThrowingAtAWalkableOneIsNot()
    {
        // The terrain gets a record that walking cannot cross (255 time units), and one of the same cost that is a big wall of kind 4 (a half wall, which an item may still land beside).
        static void Blocking(Dictionary<string, byte[]> files)
        {
            Terrain(files);
            files["TERRAIN/TESTSET.MCD"] = [.. files["TERRAIN/TESTSET.MCD"], .. BattleTestTerrain.Record((39, 255), (40, 255), (41, 255)),
                .. BattleTestTerrain.Record((39, 255), (40, 255), (41, 255), (33, 4))];
        }
        var blocked = Flat(edit: Edit(grenade: "tuThrow: 25"), terrain: Blocking);
        foreach (var item in blocked.Battle.InventoryOf(blocked.Soldier).ToList()) blocked.Battle.RemoveItem(item);
        var thrown = new BattleItem(blocked.Battle.TakeItemId(), "GRENADE") { OwnerUnitId = blocked.Soldier.Id, Slot = "STR_LEFT_HAND" };
        blocked.Battle.AddItem(thrown);
        var records = blocked.Map.DataSets.Single(static set => set.Name == "TESTSET").Objects.Count;
        var action = new BattleAimAction(blocked.Soldier, BattleActionType.Throw, thrown, new BattlePosition(14, 10, 0));
        var sight = blocked.Battle.Sight();
        var origin = sight.OriginVoxel(action);
        var aim = new BattlePosition(14 * 16 + 8, 10 * 16 + 8, 1);
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out _, out _, false));
        // MCD records 2 + index in the set: the first of the two new records.
        blocked.Put(14, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, records - 2);
        Assert.False(sight.ValidateThrow(action, origin, aim, 0, out _, out _, false));
        // Forcing does not lift the object block.
        Assert.False(sight.ValidateThrow(action, origin, aim, 0, out _, out _, true));
        // A big wall of a kind other than 1 to 3 does not block the landing.
        blocked.Put(14, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, records - 1);
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out _, out _, false));
    }

    [Fact]
    public void AnArcingShotFliesLikeAThrowAndAnOrdinaryShotDoesNot()
    {
        var (arena, gun, _) = Armed(gun: "confAimed: {arcing: true}");
        Assert.True(BattleState.ArcingShot(arena.Rules, gun, BattleActionType.AimedShot));
        Assert.False(BattleState.ArcingShot(arena.Rules, gun, BattleActionType.SnapShot));
        // Over a full wall an ordinary shot has no line, an arc finds one by a high curvature.
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        arena.See();
        Assert.True(AimAt(arena, gun, BattleAimKind.AimedShot, 15).HasLineOfFire);
        Assert.False(AimAt(arena, gun, BattleAimKind.SnapShot, 15).HasLineOfFire);
        // Under a ceiling the arc has none either.
        for (var x = 10; x <= 15; x++) arena.Put(x, 10, 1, Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, Wall);
        arena.See();
        Assert.False(AimAt(arena, gun, BattleAimKind.AimedShot, 15).HasLineOfFire);
        // The projectile's arc ends in a voxel when it has one; a forced arcing shot fakes a hit.
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0)) { ForceFire = true };
        var projectile = new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, default);
        Assert.Equal(BattleVoxelType.Object, projectile.CalculateThrow(1.0));
    }

    [Fact]
    public void AParabolaCrossesTheMapAndEndsOnTheGroundOrOutOfIt()
    {
        var (arena, _, _) = Armed();
        var sight = arena.Battle.Sight();
        var trajectory = new List<BattlePosition>();
        var origin = new BattlePosition(10 * 16 + 8, 10 * 16 + 8, 19);
        var target = new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 1);
        // The same point gives nothing; a throw starts with the origin and ends at the ground under the target (or past it).
        Assert.Equal(BattleVoxelType.Empty, sight.CalculateParabolaVoxel(origin, origin, true, trajectory, null, 1.0, default));
        Assert.Empty(trajectory);
        var result = sight.CalculateParabolaVoxel(origin, target, true, trajectory, arena.Soldier, 1.0, default);
        Assert.Equal(origin, trajectory[0]);
        // The alien stands where the arc comes down: it ends in the unit.
        Assert.Equal(BattleVoxelType.Unit, result);
        Assert.Equal(15, trajectory[^1].X / 16);
        Assert.True(trajectory.Select(static voxel => voxel.Z).Max() > origin.Z, "the arc rises above the start");
        // The impact voxel of an arc that stops on a wall is the voxel inside the wall.
        arena.Put(13, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        var walled = new List<BattlePosition>();
        Assert.Equal(BattleVoxelType.Object, sight.CalculateParabolaVoxel(origin, target, false, walled, arena.Soldier, 0.5, default));
        Assert.Single(walled);
        Assert.Equal(13, walled[0].X / 16);
    }

    // ---- accuracy ----

    private static int Firing(BattleUnit unit) => unit.Stats.Firing;

    [Fact]
    public void TheFiringAccuracyIsTheWeaponsAccuracyTimesTheUnitsFiringLessTheKneelBonusAndTheOneHandedPenalty()
    {
        var (arena, gun, grenade) = Armed(gun: "accuracyAimed: 80\naccuracySnap: 60\naccuracyAuto: 50\nkneelBonus: 120\ntwoHanded: true\noneHandedPenalty: 50\ntuAimed: 30",
            grenade: "tuThrow: 25", leftHand: true);
        var battle = arena.Battle;
        var soldier = arena.Soldier;
        var firing = Firing(soldier);
        Assert.True(firing > 0);
        // Two hands are not free, so the two handed gun is halved. Remove the grenade to hold the gun with an empty hand.
        Assert.Equal(firing * 80 / 100 * 50 / 100, battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AimedShot));
        Assert.Equal(firing * 60 / 100 * 50 / 100, battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.SnapShot));
        Assert.Equal(firing * 50 / 100 * 50 / 100, battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AutoShot));
        battle.RemoveItem(grenade!);
        var open = firing * 80 / 100;
        Assert.Equal(open, battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AimedShot));
        // A kneeling unit gets the bonus.
        soldier.Kneeled = true;
        Assert.Equal(open * 120 / 100, battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AimedShot));
        // Wounds in the head and in both arms (a two handed weapon) cost 10% each; health scales the rest.
        soldier.FatalWounds[0] = 1;
        soldier.FatalWounds[2] = 1;
        soldier.FatalWounds[3] = 2;
        Assert.Equal(75 - 40 + 25, BattleState.AccuracyModifier(arena.Rules, soldier, gun));
        Assert.Equal(open * 120 / 100 * 60 / 100, battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AimedShot));
        soldier.FatalWounds[0] = 20;
        Assert.Equal(10, BattleState.AccuracyModifier(arena.Rules, soldier, gun)); // never below 10
        soldier.FatalWounds[0] = 0;
        soldier.FatalWounds[2] = 0;
        soldier.FatalWounds[3] = 0;
        soldier.Health = soldier.Stats.Health / 2;
        Assert.Equal(25 * soldier.Health / soldier.Stats.Health + 75, BattleState.AccuracyModifier(arena.Rules, soldier, gun));
    }

    [Fact]
    public void AOneHandedWeaponOnlyCountsTheWoundsOfTheArmThatHoldsItAndAThrowOrAHitIsNeverHelpedByKneeling()
    {
        var (arena, gun, grenade) = Armed(gun: "accuracyAimed: 100\nkneelBonus: 130", grenade: "tuThrow: 25\naccuracyMelee: 70", leftHand: true);
        var soldier = arena.Soldier;
        soldier.FatalWounds[2] = 3; // the right arm
        Assert.Equal(70, BattleState.AccuracyModifier(arena.Rules, soldier, gun));
        Assert.Equal(100, BattleState.AccuracyModifier(arena.Rules, soldier, grenade!)); // held in the left hand
        soldier.FatalWounds[2] = 0;
        soldier.Kneeled = true;
        var standing = Firing(soldier) * 100 / 100;
        Assert.Equal(standing * 130 / 100, arena.Battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AimedShot));
        // A throw uses the throwing statistic and the item's throw accuracy (100), no kneel bonus; a hit the melee skill's accuracy.
        Assert.Equal(soldier.Stats.Throwing, arena.Battle.FiringAccuracy(arena.Rules, soldier, grenade!, BattleActionType.Throw));
        Assert.Equal(soldier.Stats.Melee * 70 / 100, arena.Battle.FiringAccuracy(arena.Rules, soldier, grenade!, BattleActionType.Hit));
    }

    [Fact]
    public void ARangeLimitAndTheDropoffChangeTheAccuracyTheCursorShows()
    {
        const string rangeGun = "accuracyAimed: 100\naimRange: 6\nminRange: 3\ndropoff: 10\nmaxRange: 8\ntuAimed: 30";
        var firing = Firing(Armed(gun: rangeGun).Arena.Soldier);
        // Inside the range limits: the plain accuracy.
        var (inside, gun, _) = Armed(5, rangeGun);
        var plain = AimAt(inside, gun, BattleAimKind.AimedShot, 15);
        Assert.Equal((firing, true), (plain.Accuracy, plain.InRange));
        // Past the aimed range: 10 per tile (8 tiles away, 2 over).
        var (far, farGun, _) = Armed(8, rangeGun);
        Assert.Equal((firing - 20, true), (AimAt(far, farGun, BattleAimKind.AimedShot, 18).Accuracy, AimAt(far, farGun, BattleAimKind.AimedShot, 18).InRange));
        // Inside the minimum range: 10 per tile (2 away, 1 under).
        var (near, nearGun, _) = Armed(2, rangeGun);
        Assert.Equal(firing - 10, AimAt(near, nearGun, BattleAimKind.AimedShot, 12).Accuracy);
        // Past the maximum range: out of range, no accuracy.
        var (tooFar, tooFarGun, _) = Armed(9, rangeGun);
        var gone = AimAt(tooFar, tooFarGun, BattleAimKind.AimedShot, 19);
        Assert.Equal((0, false), (gone.Accuracy, gone.InRange));
        // A range of 1 or 2 reaches one diagonal step.
        var (arena, _, _) = Armed(gun: "maxRange: 1");
        Assert.False(BattleState.IsOutOfRange(arena.Rules, GunItem(arena), 3));
        Assert.True(BattleState.IsOutOfRange(arena.Rules, GunItem(arena), 4));
        var (two, _, _) = Armed(gun: "maxRange: 2");
        Assert.False(BattleState.IsOutOfRange(two.Rules, GunItem(two), 6));
        Assert.True(BattleState.IsOutOfRange(two.Rules, GunItem(two), 7));
    }

    private static BattleItem GunItem(Arena arena) => arena.Battle.InventoryOf(arena.Soldier).Single(static item => item.ItemType == "GUN");

    [Fact]
    public void AThrowLosesAccuracyOnlyPastItsDropoffRangeAndTheNoLineOfSightPenaltyCutsTheAccuracyOfAnUnseenTile()
    {
        var (arena, _, grenade) = Armed(grenade: "tuThrow: 25\nthrowDropoffRange: 3\nthrowDropoff: 5", leftHand: true);
        // Dropoff of a throw starts at 200 tiles unless battleUFOExtenderAccuracy is on (it is off), so it is the plain throwing accuracy.
        Assert.Equal(arena.Soldier.Stats.Throwing, AimAt(arena, grenade!, BattleAimKind.Throw, 15).Accuracy);
        var (hidden, gun, _) = Armed(gun: "accuracyAimed: 100\nnoLOSAccuracyPenalty: 50");
        var seen = AimAt(hidden, gun, BattleAimKind.AimedShot, 15).Accuracy;
        Assert.Equal(Firing(hidden.Soldier), seen);
        // A wall between: the alien is no longer seen, and a tile past the wall has no sight of it either.
        hidden.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        hidden.See();
        Assert.Equal(seen * 50 / 100, AimAt(hidden, gun, BattleAimKind.AimedShot, 15).Accuracy);
        // A tile the unit sees has no penalty.
        Assert.Equal(seen, AimAt(hidden, gun, BattleAimKind.AimedShot, 11).Accuracy);
    }

    [Fact]
    public void TheHitChanceTablesTurnTheAccuracyIntoAChanceByDistance()
    {
        var table = new int[40 * 61];
        for (var index = 0; index < table.Length; index++) table[index] = index;
        // Even accuracies read the table; odd ones the rounded mean of their neighbours; both ends clamp.
        Assert.Equal((2 - 1) * 61 + 2, BattleState.HitChance(2, 4, table));
        Assert.Equal(64, BattleState.HitChance(2, 5, table)); // (63 + 64) / 2 = 63.5 rounds up
        Assert.Equal(0, BattleState.HitChance(0, -4, table)); // distance 0 is 1, accuracy below 0 is 0
        Assert.Equal(39 * 61 + 60, BattleState.HitChance(50, 130, table)); // distance 40, accuracy 120
        Assert.Equal(77, BattleState.HitChance(3, 77, null)); // no table: the accuracy itself
        // A mod's table is used through the cursor: every row holds its distance times 10.
        var rows = new StringBuilder("\nhitChancesTable:\n  - unitSize: 1\n    distances:\n");
        for (var distance = 1; distance <= 40; distance++) rows.Append(System.Globalization.CultureInfo.InvariantCulture, $"      {distance}: [{string.Join(", ", Enumerable.Repeat(distance * 10, 61))}]\n");
        var (arena, gun, _) = Armed(gun: "accuracyAimed: 100", global: rows.ToString());
        var view = AimAt(arena, gun, BattleAimKind.AimedShot, 15);
        Assert.Equal(Firing(arena.Soldier), view.Accuracy);
        Assert.Equal(50, view.HitChance);
        // Without a table the chance is the accuracy.
        var (plain, plainGun, _) = Armed(gun: "accuracyAimed: 100");
        var untabled = AimAt(plain, plainGun, BattleAimKind.AimedShot, 15);
        Assert.Equal(untabled.Accuracy, untabled.HitChance);
    }

    [Fact]
    public void TheUniformSpreadOfShootingSpreadMode1ShiftsTheTargetByDrawsInsideACircle()
    {
        var (arena, gun, _) = Armed();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        var origin = new BattlePosition(100, 100, 18);
        // The target 80 voxels east: xyShift is 80 * 0.839 = 67, zShift 67; at 50% accuracy a draw of 80 makes a deviation of 30 + 50 = 80, scaled
        // 67 * 80 / 200 = 26. The draws 20 and 5 shift (7, -8), inside the circle of radius 13; the vertical draw 9 / 2 - 26 / 8 = +1.
        var draws = new ScriptedDraws(80, 20, 5, 9);
        arena.Battle.Random = draws;
        var target = new BattlePosition(180, 100, 18);
        new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, target).ApplyAccuracy(origin, ref target, 0.5, false, false);
        Assert.Equal(new BattlePosition(187, 92, 19), target);
        Assert.Equal([(0, 100), (0, 26), (0, 26), (0, 13)], draws.Calls.Select(static call => (call.Minimum, call.Maximum)).ToArray());

        // A draw outside the circle is redrawn with a deviation scaled by 0.85 (22): two corners (26, 26), then the middle (11, 11).
        var redraw = new ScriptedDraws(80, 26, 26, 11, 11, 0);
        arena.Battle.Random = redraw;
        target = new BattlePosition(180, 100, 18);
        new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, target).ApplyAccuracy(origin, ref target, 0.5, false, false);
        Assert.Equal(new BattlePosition(180, 100, 16), target);
        Assert.Equal([(0, 100), (0, 26), (0, 26), (0, 22), (0, 22), (0, 11)], redraw.Calls.Select(static call => (call.Minimum, call.Maximum)).ToArray());
    }

    [Fact]
    public void ABetterAccuracyTightensTheSpreadAndTheLineIsExtendedPastTheTarget()
    {
        var (arena, gun, _) = Armed();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        var origin = new BattlePosition(100, 100, 18);
        // A draw of 0 with an accuracy of 109% or more: deviation (0 - 109) + 10 = -99 + ... the spread is the tightest (1).
        var draws = new ScriptedDraws(0, 0, 0, 0);
        arena.Battle.Random = draws;
        var target = new BattlePosition(180, 100, 18);
        new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, target).ApplyAccuracy(origin, ref target, 1.09, false, true);
        Assert.Equal((0, 1), (draws.Calls[1].Minimum, draws.Calls[1].Maximum));
        // Extended 16000 voxels (1000 tiles) along the line, east.
        Assert.True(target.X > 16000);
        Assert.InRange(target.Y, 90, 110);
        // A hit's line stops at 46 voxels.
        var melee = new BattleAimAction(arena.Soldier, BattleActionType.Hit, gun, new BattlePosition(11, 10, 0));
        var hitTarget = new BattlePosition(116, 100, 18);
        arena.Battle.Random = new ScriptedDraws(0, 0, 0, 0);
        new BattleProjectile(arena.Battle, arena.Rules, melee, arena.Soldier.RequiredPosition, hitTarget).ApplyAccuracy(origin, ref hitTarget, 1.0, true, true);
        Assert.InRange(hitTarget.X, 100 + 44, 100 + 47);
    }

    [Fact]
    public void AFirstShotWhoseLineStopsShortOfTheTargetTileGivesNoLineOfFireAndAWallBesideItDoesNot()
    {
        var (arena, gun, _) = Armed();
        arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
        arena.See();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0)) { AutoShotCounter = 1 };
        var origin = arena.Battle.Sight().OriginVoxel(action);
        var target = new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 12);
        var projectile = new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, target);
        Assert.True(projectile.LineOfFireBlocked(origin));
        Assert.Empty(projectile.Trajectory);
        // The same shot is not checked on the second shot of a burst, forced, or for an artificial intelligence's unit.
        action.AutoShotCounter = 2;
        arena.Battle.Random = new ScriptedDraws(0, 0, 0, 0);
        Assert.Equal(BattleVoxelType.Object, projectile.CalculateTrajectory(1.0, origin));
        Assert.Equal(12, projectile.Trajectory[^1].X / 16);
    }

    // ---- canUseWeapon ----

    [Fact]
    public void CanUseWeaponRefusesAnUnresearchedWeaponAHostileUnitBeforeTheWeaponsDelayAndEquipmentOfTheWrongDepth()
    {
        var (arena, gun, _) = Armed(gun: "requires: [RES_GUN]\nai: {useDelay: 3}", global: ResearchText.TrimStart('\n'));
        var battle = arena.Battle;
        var rules = arena.Rules;
        var nothing = new BattleResearchGate(static _ => false, false);
        Assert.False(battle.CanUseWeapon(rules, nothing, gun, arena.Soldier, false, BattleActionType.AimedShot).Usable);
        Assert.True(battle.CanUseWeapon(rules, new BattleResearchGate(static id => id == "RES_GUN", false), gun, arena.Soldier, false, BattleActionType.AimedShot).Usable);
        Assert.True(battle.CanUseWeapon(rules, new BattleResearchGate(static _ => false, true), gun, arena.Soldier, false, BattleActionType.AimedShot).Usable); // the debug mode
        // A hostile unit's research does not matter, but its turn does: the weapon's delay of 3.
        var alien = arena.Alien;
        var alienGun = new BattleItem(battle.TakeItemId(), "GUN") { OwnerUnitId = alien.Id, Slot = "STR_RIGHT_HAND", IsWeaponWithAmmo = true };
        battle.AddItem(alienGun);
        battle.Turn = 2;
        Assert.False(battle.CanUseWeapon(rules, nothing, alienGun, alien, false, BattleActionType.None).Usable);
        battle.Turn = 3;
        Assert.True(battle.CanUseWeapon(rules, nothing, alienGun, alien, false, BattleActionType.None).Usable);
        // No weapon or unit is no use.
        Assert.False(battle.CanUseWeapon(rules, nothing, null, alien, false, BattleActionType.None).Usable);
        Assert.False(battle.CanUseWeapon(rules, nothing, alienGun, null, false, BattleActionType.None).Usable);
    }

    [Fact]
    public void CanUseWeaponChecksTheDepthOfTheWeaponAndOfTheAmmunitionTheActionUses()
    {
        var (arena, gun, _) = Armed(gun: "underwaterOnly: true");
        var water = arena.Battle.CanUseWeapon(arena.Rules, BattleResearchGate.Everything, gun, arena.Soldier, false, BattleActionType.AimedShot);
        Assert.Equal((false, "STR_UNDERWATER_EQUIPMENT"), (water.Usable, water.Message));
        // The ammunition's own flag counts for an action that names it, and not for the standard artificial intelligence's unknown action.
        var (ammoArena, ammoGun, _) = Armed(clip: "underwaterOnly: true");
        var byAction = ammoArena.Battle.CanUseWeapon(ammoArena.Rules, BattleResearchGate.Everything, ammoGun, ammoArena.Soldier, false, BattleActionType.AimedShot);
        Assert.Equal((false, "STR_UNDERWATER_EQUIPMENT"), (byAction.Usable, byAction.Message));
        Assert.True(ammoArena.Battle.CanUseWeapon(ammoArena.Rules, BattleResearchGate.Everything, ammoGun, ammoArena.Soldier, false, BattleActionType.None).Usable);
    }

    [Fact]
    public void AWeaponThatBlocksBothHandsNeedsAFreeHandExceptForABerserkOrNonPlayerUnit()
    {
        var (arena, gun, _) = Armed(gun: "blockBothHands: true", grenade: "tuThrow: 25", leftHand: true);
        var battle = arena.Battle;
        var refused = battle.CanUseWeapon(arena.Rules, BattleResearchGate.Everything, gun, arena.Soldier, false, BattleActionType.AimedShot);
        Assert.Equal((false, "STR_MUST_USE_BOTH_HANDS"), (refused.Usable, refused.Message));
        Assert.True(battle.CanUseWeapon(arena.Rules, BattleResearchGate.Everything, gun, arena.Soldier, true, BattleActionType.AimedShot).Usable); // berserk
        Assert.True(battle.CanUseWeapon(arena.Rules, BattleResearchGate.Everything, gun, arena.Alien, false, BattleActionType.AimedShot).Usable); // not X-COM's side
        // With a hand free it is fine.
        var (free, freeGun, _) = Armed(gun: "blockBothHands: true");
        Assert.True(free.Battle.CanUseWeapon(free.Rules, BattleResearchGate.Everything, freeGun, free.Soldier, false, BattleActionType.AimedShot).Usable);
    }

    [Fact]
    public void ApsiWeaponNeedsPsiSkillAndAManaWeaponNeedsTheFeatureAndItsResearch()
    {
        var (psi, psiGun, _) = Armed(gun: "psiRequired: true");
        psi.Soldier.Stats = psi.Soldier.Stats with { PsiSkill = 0 };
        Assert.False(psi.Battle.CanUseWeapon(psi.Rules, BattleResearchGate.Everything, psiGun, psi.Soldier, false, BattleActionType.AimedShot).Usable);
        psi.Soldier.Stats = psi.Soldier.Stats with { PsiSkill = 20 };
        Assert.True(psi.Battle.CanUseWeapon(psi.Rules, BattleResearchGate.Everything, psiGun, psi.Soldier, false, BattleActionType.AimedShot).Usable);

        // The mana feature is off by default: X-COM's unit cannot use a mana weapon.
        var (off, offGun, _) = Armed(gun: "manaRequired: true");
        Assert.False(off.Battle.CanUseWeapon(off.Rules, BattleResearchGate.Everything, offGun, off.Soldier, false, BattleActionType.AimedShot).Usable);
        Assert.True(off.Battle.CanUseWeapon(off.Rules, BattleResearchGate.Everything, offGun, off.Alien, false, BattleActionType.AimedShot).Usable); // a hostile unit is not gated
        // On, with an unlock research: until it is researched.
        var (on, onGun, _) = Armed(gun: "manaRequired: true", global: "mana:\n  enabled: true\n  unlockResearch: RES_GUN\n" + ResearchText.TrimStart('\n'));
        Assert.True(on.Rules.Campaign.Combat.ManaEnabled);
        Assert.Equal("RES_GUN", on.Rules.Campaign.Combat.ManaUnlockResearch);
        Assert.False(on.Battle.CanUseWeapon(on.Rules, new BattleResearchGate(static _ => false, false), onGun, on.Soldier, false, BattleActionType.AimedShot).Usable);
        Assert.True(on.Battle.CanUseWeapon(on.Rules, new BattleResearchGate(static id => id == "RES_GUN", false), onGun, on.Soldier, false, BattleActionType.AimedShot).Usable);
        Assert.True(on.Battle.CanUseWeapon(on.Rules, new BattleResearchGate(static _ => false, true), onGun, on.Soldier, false, BattleActionType.AimedShot).Usable);
        // Enabled with no research named: unlocked.
        var (open, openGun, _) = Armed(gun: "manaRequired: true", global: "mana:\n  enabled: true\n");
        Assert.True(open.Battle.CanUseWeapon(open.Rules, new BattleResearchGate(static _ => false, false), openGun, open.Soldier, false, BattleActionType.AimedShot).Usable);
    }

    // ---- the queries ----

    [Fact]
    public void TheMenuListsTheShotsOfAFirearmWithTheirAccuracyAndCostAndAThrowForAnItemThatIsNotFixed()
    {
        var (arena, gun, _) = Armed(gun: "accuracyAimed: 80\naccuracySnap: 60\naccuracyAuto: 40\ntuAimed: 30\ntuSnap: 18\ntuAuto: 24\ntuThrow: 20\ntuMelee: 12\naccuracyMelee: 50");
        var menu = arena.Battle.AimMenu(arena.Rules, BattleResearchGate.Everything, Player, arena.Soldier.Id, gun.Id)!;
        var firing = Firing(arena.Soldier);
        Assert.Equal(["Throw", "AutoShot", "SnapShot", "AimedShot", "Hit"], menu.Items.Select(static item => item.Kind.ToString()).ToArray());
        var timeUnits = arena.Soldier.Stats.TimeUnits;
        Assert.Equal([arena.Soldier.Stats.Throwing, firing * 40 / 100, firing * 60 / 100, firing * 80 / 100, arena.Soldier.Stats.Melee * 50 / 100],
            menu.Items.Select(static item => item.Accuracy).ToArray());
        // The costs are percentages of the unit's time units.
        Assert.Equal([timeUnits * 20 / 100, timeUnits * 24 / 100, timeUnits * 18 / 100, timeUnits * 30 / 100, timeUnits * 12 / 100], menu.Items.Select(static item => item.TimeUnits).ToArray());
        Assert.Equal("", menu.Reason);
        Assert.Equal("STR_HIT_MELEE", menu.Items[^1].Name);
        // A fixed item cannot be thrown; a psi weapon the unit cannot use shows only the throw.
        var (fixedArena, fixedGun, _) = Armed(gun: "fixedWeapon: true\ntuAimed: 30\ntuThrow: 20");
        Assert.DoesNotContain(fixedArena.Battle.AimMenu(fixedArena.Rules, BattleResearchGate.Everything, Player, fixedArena.Soldier.Id, fixedGun.Id)!.Items, static item => item.Kind == BattleAimKind.Throw);
        var (psiArena, psiGun, _) = Armed(gun: "psiRequired: true\ntuAimed: 30\ntuThrow: 20");
        psiArena.Soldier.Stats = psiArena.Soldier.Stats with { PsiSkill = 0 };
        Assert.Equal(["Throw"], psiArena.Battle.AimMenu(psiArena.Rules, BattleResearchGate.Everything, Player, psiArena.Soldier.Id, psiGun.Id)!.Items.Select(static item => item.Kind.ToString()).ToArray());
        // A launcher offers its launch instead of the aimed shot.
        var (launcher, launchGun, _) = Armed(gun: "waypoints: 1\ntuAimed: 30\ntuAuto: 20\ntuSnap: 18");
        var kinds = launcher.Battle.AimMenu(launcher.Rules, BattleResearchGate.Everything, Player, launcher.Soldier.Id, launchGun.Id)!.Items.Select(static item => item.Kind).ToArray();
        // A launcher's shots share their ammunition slot with the ones it replaces, so only the launch (and the throw) remain.
        Assert.Equal([BattleAimKind.Throw, BattleAimKind.Launch], kinds);
        Assert.DoesNotContain(BattleAimKind.AimedShot, kinds);
    }

    [Fact]
    public void TheMenuNamesWhyAWeaponCannotBeUsed()
    {
        var (arena, gun, _) = Armed(gun: "underwaterOnly: true\ntuAimed: 30");
        Assert.Equal("STR_UNDERWATER_EQUIPMENT", arena.Battle.AimMenu(arena.Rules, BattleResearchGate.Everything, Player, arena.Soldier.Id, gun.Id)!.Reason);
        var (usable, usableGun, _) = Armed(gun: "tuAimed: 30");
        Assert.Equal("", usable.Battle.AimMenu(usable.Rules, BattleResearchGate.Everything, Player, usable.Soldier.Id, usableGun.Id)!.Reason);
    }

    [Fact]
    public void TheAimViewShowsTheCostAndWhatTheUnitCannotPay()
    {
        var (arena, gun, _) = Armed(gun: "accuracyAimed: 80\ntuAimed: 30");
        var view = AimAt(arena, gun, BattleAimKind.AimedShot, 15);
        Assert.Equal((arena.Soldier.Stats.TimeUnits * 30 / 100, true, "", true, true), (view.TimeUnits, view.CanPay, view.ShortfallReason, view.InRange, view.HasLineOfFire));
        arena.Soldier.TimeUnits = 3;
        var poor = AimAt(arena, gun, BattleAimKind.AimedShot, 15);
        Assert.Equal((false, "STR_NOT_ENOUGH_TIME_UNITS"), (poor.CanPay, poor.ShortfallReason));
    }

    [Fact]
    public void TheAimQueriesAnswerOnlyForTheViewersUnitOnItsTurnAtATileItHasDiscovered()
    {
        var (arena, gun, _) = Armed(gun: "accuracyAimed: 80\ntuAimed: 30");
        var queries = (IBattleViewQuery)arena.Campaign.GetQuery<IBattleViewQuery>()!;
        var target = new BattlePosition(15, 10, 0);
        Assert.NotNull(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, target));
        Assert.NotNull(queries.QueryAimMenu(Player, arena.Soldier.Id, gun.Id));
        // Another side's unit, and another side's viewer.
        Assert.Null(queries.QueryAim(Player, arena.Alien.Id, gun.Id, BattleAimKind.AimedShot, target));
        Assert.Null(queries.QueryAim(new BattleViewer(BattleFaction.Hostile), arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, target));
        Assert.Null(queries.QueryAimMenu(new BattleViewer(BattleFaction.Hostile), arena.Soldier.Id, gun.Id));
        // An item the unit does not hold, a launch and a hit (their ranges belong to their actions), and a tile outside the map.
        Assert.Null(queries.QueryAim(Player, arena.Soldier.Id, 9999, BattleAimKind.AimedShot, target));
        Assert.Null(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.Launch, target));
        Assert.Null(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.Hit, target));
        Assert.Null(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, new BattlePosition(99, 10, 0)));
        // A tile the viewer has not discovered.
        arena.Map.SetDiscovered(arena.Tile(15, 10), Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, false, BattleFaction.Player);
        Assert.Null(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, target));
        // Off the side's turn.
        arena.Map.SetDiscovered(arena.Tile(15, 10), Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, true, BattleFaction.Player);
        Assert.NotNull(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, target));
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.Null(queries.QueryAim(Player, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, target));
        Assert.Null(queries.QueryAimMenu(Player, arena.Soldier.Id, gun.Id));
    }

    private static void SetDepth(BattleState battle, int depth) =>
        typeof(BattleState).GetField("<Depth>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(battle, depth);

    [Fact]
    public void LandEquipmentIsRefusedUnderWaterByTheWeaponOrTheAmmunitionOfTheAction()
    {
        var (arena, gun, _) = Armed(gun: "landOnly: true");
        SetDepth(arena.Battle, 1);
        var refused = arena.Battle.CanUseWeapon(arena.Rules, BattleResearchGate.Everything, gun, arena.Soldier, false, BattleActionType.AimedShot);
        Assert.Equal((false, "STR_LAND_EQUIPMENT"), (refused.Usable, refused.Message));
        var (ammoArena, ammoGun, _) = Armed(clip: "landOnly: true");
        SetDepth(ammoArena.Battle, 1);
        Assert.Equal("STR_LAND_EQUIPMENT", ammoArena.Battle.CanUseWeapon(ammoArena.Rules, BattleResearchGate.Everything, ammoGun, ammoArena.Soldier, false, BattleActionType.AutoShot).Message);
        Assert.True(ammoArena.Battle.CanUseWeapon(ammoArena.Rules, BattleResearchGate.Everything, ammoGun, ammoArena.Soldier, false, BattleActionType.None).Usable);
    }

    [Fact]
    public void AThrowIsNotGatedByCanUseWeaponAndTheMenuSaysWhyAWeaponIsRefused()
    {
        var (arena, gun, grenade) = Armed(gun: "requires: [RES_GUN]\ntuAimed: 30", grenade: "requires: [RES_GUN]\ntuThrow: 25", global: ResearchText.TrimStart('\n'), leftHand: true);
        var nothing = new BattleResearchGate(static _ => false, false);
        arena.Map.SetDiscovered(arena.Tile(15, 10), Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, true, BattleFaction.Player);
        var throwView = arena.Battle.AimView(arena.Rules, nothing, Player, arena.Soldier.Id, grenade!.Id, BattleAimKind.Throw, new BattlePosition(15, 10, 0), false)!;
        Assert.True(throwView.Usable);
        var shot = arena.Battle.AimView(arena.Rules, nothing, Player, arena.Soldier.Id, gun.Id, BattleAimKind.AimedShot, new BattlePosition(15, 10, 0), false)!;
        Assert.Equal((false, "STR_UNABLE_TO_USE_ALIEN_ARTIFACT_UNTIL_RESEARCHED"), (shot.Usable, shot.Reason));
        var menu = arena.Battle.AimMenu(arena.Rules, nothing, Player, arena.Soldier.Id, gun.Id)!;
        Assert.Equal((false, "STR_UNABLE_TO_USE_ALIEN_ARTIFACT_UNTIL_RESEARCHED"), (menu.Usable, menu.Reason));
        var (psi, psiGun, _) = Armed(gun: "psiRequired: true\ntuAimed: 30");
        psi.Soldier.Stats = psi.Soldier.Stats with { PsiSkill = 0 };
        Assert.Equal("STR_UNKNOWN", psi.Battle.AimMenu(psi.Rules, BattleResearchGate.Everything, Player, psi.Soldier.Id, psiGun.Id)!.Reason);
        // The mana gate of the menu: a mana weapon without the feature lists the throw only.
        var (mana, manaGun, _) = Armed(gun: "manaRequired: true\ntuAimed: 30");
        Assert.Equal([BattleAimKind.Throw], mana.Battle.AimMenu(mana.Rules, BattleResearchGate.Everything, Player, mana.Soldier.Id, manaGun.Id)!.Items.Select(static item => item.Kind).ToArray());
    }

    [Fact]
    public void AStunRodHitIsNamedStunAndAPanickedUnitAimsAtTheTilesMiddle()
    {
        var (arena, gun, grenade) = Armed(grenade: "tuMelee: 10\ndamageType: 6\nclipSize: -1", leftHand: true,
            more: static rules => Replaced(rules, "- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    battleType: 3"));
        Assert.Equal("STR_STUN", arena.Battle.AimMenu(arena.Rules, BattleResearchGate.Everything, Player, arena.Soldier.Id, grenade!.Id)!.Items.Single(static item => item.Kind == BattleAimKind.Hit).Name);
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0)) { PanicHandled = false };
        Assert.Equal(new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 12), arena.Battle.ChooseTargetVoxel(arena.Rules, action, arena.Soldier.RequiredPosition).Voxel);
    }

    [Fact]
    public void TheDummyPartOfAnEmptyTileNeverMatchesAUnitAndKeepsScanning()
    {
        var (arena, gun, _) = Armed();
        var sight = arena.Battle.Sight();
        var origin = sight.OriginVoxel(new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0)));
        var obstacles = new List<BattleObstacle>();
        Assert.False(sight.CanTargetTile(origin, arena.Tile(15, 10), BattleSight.DummyPart, out _, arena.Soldier, obstacles));
        Assert.NotEmpty(obstacles); // every line that met the alien was recorded
        Assert.All(obstacles, static obstacle => Assert.Equal(BattleVoxelType.Unit, obstacle.Type));
    }

    [Fact]
    public void AThrowWithNoStrengthAndNoWeightKeepsTheLowestCurveAndAKneelingThrowerCurvesMore()
    {
        var (arena, _, grenade) = Armed(grenade: "tuThrow: 25", leftHand: true,
            more: static rules => Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1", "- type: GRENADE\n    battleType: 4\n    weight: 0"));
        var sight = arena.Battle.Sight();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.Throw, grenade!, new BattlePosition(15, 10, 0));
        var origin = sight.OriginVoxel(action);
        var aim = new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 1);
        arena.Soldier.Stats = arena.Soldier.Stats with { Strength = 0 };
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out var curve, out _, false));
        Assert.Equal(0.48, curve);
        arena.Soldier.Stats = arena.Soldier.Stats with { Strength = 40 };
        action.Target = new BattlePosition(13, 10, 0);
        action.Type = BattleActionType.AimedShot; // an arcing shot assumes a strength of 70 and a weight of 10
        aim = new BattlePosition(13 * 16 + 8, 10 * 16 + 8, 1);
        Assert.True(sight.ValidateThrow(action, origin, aim, 0, out var standing, out _, false));
        arena.Soldier.Kneeled = true;
        Assert.True(sight.ValidateThrow(action, sight.OriginVoxel(action), aim, 0, out var kneeling, out _, false));
        Assert.Equal(standing + 0.1, kneeling, 9);
    }

    [Fact]
    public void TheUnderwaterThrowFactorShortensAThrowUnderWater()
    {
        var (arena, _, grenade) = Armed(grenade: "tuThrow: 25", leftHand: true, global: "constants:\n  extendedUnderwaterThrowFactor: 1\n");
        var sight = arena.Battle.Sight();
        var action = new BattleAimAction(arena.Soldier, BattleActionType.Throw, grenade!, new BattlePosition(15, 10, 0));
        var origin = sight.OriginVoxel(action);
        var tile = arena.Tile(15, 10);
        Assert.True(sight.ValidThrowRange(action, origin, tile, 0));
        Assert.False(sight.ValidThrowRange(action, origin, tile, 1));
        Assert.True(sight.ValidThrowRange(new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, grenade!, action.Target), origin, tile, 1)); // a shot has no throw range
    }

    [Fact]
    public void ADrawnThrowLandsAndALaunchDriftsByAFixedAccuracyAndExtendsOnlyForOneWaypoint()
    {
        var (arena, gun, grenade) = Armed(grenade: "tuThrow: 25", leftHand: true);
        var thrown = new BattleAimAction(arena.Soldier, BattleActionType.Throw, grenade!, new BattlePosition(15, 10, 0));
        var draws = new ScriptedDraws(50, 0, 0, 0, 0, 0, 0, 0);
        arena.Battle.Random = draws;
        var projectile = new BattleProjectile(arena.Battle, arena.Rules, thrown, arena.Soldier.RequiredPosition, default);
        Assert.NotEqual(BattleVoxelType.OutOfBounds, projectile.CalculateThrow(1.0));
        Assert.NotEmpty(projectile.Trajectory);
        Assert.NotEmpty(draws.Calls);
        // A launch with two waypoints is not extended; with one it goes 1000 tiles on.
        foreach (var (waypoints, far) in new[] { (2, false), (1, true) })
        {
            var launch = new BattleAimAction(arena.Soldier, BattleActionType.Launch, gun, new BattlePosition(15, 10, 0));
            for (var i = 0; i < waypoints; i++) launch.Waypoints.Add(new BattlePosition(15, 10, 0));
            arena.Battle.Random = new ScriptedDraws(0, 0, 0, 0);
            var flight = new BattleProjectile(arena.Battle, arena.Rules, launch, arena.Soldier.RequiredPosition, new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 16));
            flight.CalculateTrajectory(0.0);
            Assert.Equal(far, flight.TargetVoxel.X > 16000);
        }
    }

    [Fact]
    public void TheNoLineOfSightPenaltyWidensTheSpreadOfAShotAtAnUnseenTile()
    {
        static int Spread(bool wall)
        {
            var (arena, gun, _) = Armed(gun: "noLOSAccuracyPenalty: 25");
            if (wall) arena.Put(12, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.ObjectPart, Wall);
            arena.See();
            var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
            var draws = new ScriptedDraws(60, 0, 0, 0);
            arena.Battle.Random = draws;
            var target = new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 12);
            new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, target).ApplyAccuracy(new BattlePosition(175, 168, 18), ref target, 0.8, false, false);
            return draws.Calls[1].Maximum;
        }
        Assert.True(Spread(true) > Spread(false));
    }

    [Fact]
    public void CloseQuartersAccuracyAndTheEmptyHandRuleOfSpecialWeaponsApply()
    {
        var (arena, gun, _) = Armed(gun: "twoHanded: true\naccuracyAimed: 100\noneHandedPenalty: 50\naccuracyCloseQuarters: 50");
        var soldier = arena.Soldier;
        var combat = arena.Rules.Battle.Equipment.Items["GUN"].Combat;
        Assert.Equal(combat.StatBonuses.CloseQuarters.Evaluate(soldier) * 50 / 100, arena.Battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.CloseQuarters));
        // Alone in a hand it is held with an empty hand: no penalty.
        Assert.Equal(Firing(soldier), arena.Battle.FiringAccuracy(arena.Rules, soldier, gun, BattleActionType.AimedShot));
        var (special, specialGun, _) = Armed(gun: "twoHanded: true\naccuracyAimed: 100\noneHandedPenalty: 50\nspecialUseEmptyHand: true");
        Assert.Equal(Firing(special.Soldier) * 50 / 100, special.Battle.FiringAccuracy(special.Rules, special.Soldier, specialGun, BattleActionType.AimedShot));
    }

    [Fact]
    public void AWallOnTheFarEdgeOfTheTargetTileIsAllowedOnTheFirstShotAndOneBeyondIsNot()
    {
        var (arena, gun, _) = Armed();
        arena.Battle.SetTile(arena.Alien, new BattlePosition(27, 0, 0));
        var action = new BattleAimAction(arena.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0)) { AutoShotCounter = 1 };
        var origin = arena.Battle.Sight().OriginVoxel(action);
        bool Blocked(BattlePosition voxel) =>
            new BattleProjectile(arena.Battle, arena.Rules, action, arena.Soldier.RequiredPosition, voxel).LineOfFireBlocked(origin);
        // The north wall of the tile south of the target is the target's south edge: allowed (hit tile y - 1 is the target's y).
        arena.Put(15, 11, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.NorthWall, Wall);
        Assert.False(Blocked(new BattlePosition(15 * 16 + 8, 11 * 16 + 4, 12)));
        // Two tiles south it is not.
        arena.Clear(15, 11, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.NorthWall);
        arena.Put(15, 12, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.NorthWall, Wall);
        Assert.True(Blocked(new BattlePosition(15 * 16 + 8, 12 * 16 + 4, 12)));
        // The west wall of the tile east of the target likewise.
        arena.Put(16, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.WestWall, Wall);
        Assert.False(Blocked(new BattlePosition(16 * 16 + 4, 10 * 16 + 8, 12)));
        arena.Clear(16, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.WestWall);
        arena.Put(17, 10, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.WestWall, Wall);
        Assert.True(Blocked(new BattlePosition(17 * 16 + 4, 10 * 16 + 8, 12)));
    }
}
