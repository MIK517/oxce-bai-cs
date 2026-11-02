using Oxce.Savegames.Oxce;
using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleMovementGroundworkTests;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S4: sight and field of view (<c>src/Battlescape/TileEngine.cpp</c>: <c>calculateLineTile</c>, <c>calculateTilesInFOV</c>,
/// <c>calculateUnitsInFOV</c>, <c>calculateFOV</c>, <c>recalculateFOV</c>, <c>setupEventVisibilitySector</c>, <c>visible</c>,
/// <c>canTargetUnit</c>, <c>getSightOriginVoxel</c>, <c>getOriginVoxel</c>, <c>voxelCheck</c>, <c>calculateLineVoxel</c>;
/// <c>BattleUnit</c>'s sight lists and spotting fields) on synthetic maps. The battle is the suited-soldier one past its first turn; the tests
/// clear its 30x30x2 map, give the units the 8x8 pillar of line of fire template 3 and chosen heights, and put the soldier at (10, 10) facing
/// east and the alien east of it. The distances are worked out from the reference's arithmetic: a unit sees a target while the quality
/// <c>maxVoxelDistance - voxelDistance - smoke</c> is above zero, the maximum being the distance in tiles times 16 plus 4.
/// </summary>
public sealed class BattleSightTests
{
    // TESTSET's records after the three the battle generator uses.
    internal const int Floor = 3; // a solid walkable floor
    internal const int Wall = 4; // fills its tile for lines of fire (template 1 in every layer) and stops sight
    internal const int LowWall = 5; // fills the lower six layers (12 voxels), does not stop sight
    internal const int Post = 6; // a thin slit along the middle of the tile (template 4 in every layer), does not stop sight

    private static byte[] Solid(byte template, int layers, bool stopsSight)
    {
        var values = Enumerable.Range(8, layers).Select(offset => (offset, template)).ToList();
        if (stopsSight) values.Add((31, (byte)1));
        return Record([.. values]);
    }

    internal const int GravLift = 7; // a grav lift floor
    internal const int UfoDoor = 8; // a UFO door filling its tile for lines of fire

    private static byte[] UfoDoorRecord() => Record([.. Enumerable.Range(8, 12).Select(offset => (offset, (byte)1)), (30, (byte)1)]);

    internal static void Terrain(Dictionary<string, byte[]> files) => files["TERRAIN/TESTSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3)),
        Record((42, 4), (59, 2)), Record((39, 4), (40, 4), (41, 4)), Solid(1, 12, true), Solid(1, 6, false), Solid(4, 12, false), Record((34, 1), (39, 4)), UfoDoorRecord());

    private const string SuitBlock = "  - type: SUIT\n    movementType: 1\n    builtInWeapons: [GUN, CLIP, GRENADE]\n    specialWeapon: AMP\n    storeItem: SUPPLY\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]";
    private const string SmallBlock = "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    allowInv: false\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]";
    private const string BigBlock = "  - type: VEHICLE_ARMOR\n    size: 2\n    allowInv: false\n    specialWeapon: RAM\n    stats: {tu: 10}\n    corpseBattle: [CORPSE, CORPSE, CORPSE, CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0, 0, 0, 0]";

    private static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    /// <summary>The rules for a sight test: the soldier and the alien (or a big one) have the pillar template and a height of 22.</summary>
    internal static string SightRules(int shade, string soldierExtra = "", string alienExtra = "", string? unitExtra = null, int soldierHeight = 22)
    {
        var rules = Replaced(Rules, "  - type: UFO_SUITED\n    width: 30", $"  - type: UFO_SUITED\n    shade: {shade}\n    width: 30");
        rules = Replaced(rules, SuitBlock, SuitBlock.Replace("[0]", "[3]", StringComparison.Ordinal) + $"\n    standHeight: {soldierHeight}\n    kneelHeight: 14\n" + soldierExtra);
        rules = Replaced(rules, SmallBlock, SmallBlock.Replace("[0]", "[3]", StringComparison.Ordinal) + "\n    standHeight: 22\n    kneelHeight: 14\n" + alienExtra);
        rules = Replaced(rules, BigBlock, BigBlock.Replace("[0, 0, 0, 0]", "[3, 3, 3, 3]", StringComparison.Ordinal) + "\n    standHeight: 22\n    kneelHeight: 14");
        return unitExtra is null ? rules : Replaced(rules, "  - type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR\n", "  - type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR\n" + unitExtra);
    }

    internal sealed record Arena(CampaignState Campaign, BattleState Battle, BattleUnit Soldier, BattleUnit Alien, RuntimeRuleCatalog Rules)
    {
        public BattleMap Map => Battle.Map;

        public int Tile(int x, int y, int z = 0) => Map.TileIndex(new BattlePosition(x, y, z));

        public void Put(int x, int y, int z, BattleTilePart part, int id) => Place(Battle, new BattlePosition(x, y, z), part, id);

        public void Clear(int x, int y, int z, BattleTilePart part) => Map.SetPart(Tile(x, y, z), part, -1, -1);

        public void Stand(BattleUnit unit, int x, int y, int direction)
        {
            Battle.SetTile(unit, new BattlePosition(x, y, 0));
            unit.SetDirection(direction);
        }

        public void Relight() => Battle.CalculateLighting(Rules, BattleLightLayer.Ambient, null, 0, true);

        /// <summary>Forgets what the first turn's sight found: the spotting, the discovery and the visibility counts of every unit and tile.</summary>
        public void Forget()
        {
            foreach (var unit in Battle.Units)
            {
                foreach (var faction in Enum.GetValues<BattleFaction>())
                    if (faction != unit.Faction) Battle.SetSpotted(unit, faction, false);
                unit.UnitsSpottedThisTurn.Clear();
                unit.ClearVisibleUnits();
                unit.ClearVisibleTiles(Map);
                Array.Fill(unit.TurnsSinceSpotted, (byte)255);
                Array.Fill(unit.TurnsLeftSpottedForSnipers, (byte)0);
                Array.Fill(unit.TurnsSinceSeenBy, 255);
                Array.Fill(unit.TileLastSpottedBy, -1);
                Array.Fill(unit.TileLastSpottedForBlindShotBy, -1);
            }
            for (var tile = 0; tile < Map.TileCount; tile++)
            {
                Map.SetVisibility(tile, 0);
                foreach (var faction in Enum.GetValues<BattleFaction>())
                    foreach (var part in Enum.GetValues<BattleTilePart>()) Map.SetDiscovered(tile, part, false, faction);
            }
        }

        /// <summary>Relights the map and sees again from nothing.</summary>
        public void See()
        {
            Relight();
            Forget();
            Battle.RecalculateFov();
        }

        public static bool Sees(BattleUnit unit, BattleUnit other) => unit.VisibleUnits.Contains(other.Id);
    }

    /// <summary>A flat arena: a floor over the whole ground level, the soldier at (10, 10) facing east and the alien at (10 + distance, 10) facing west.</summary>
    internal static Arena Flat(int shade = 0, int distance = 5, string soldierExtra = "", string alienExtra = "", string? unitExtra = null,
        bool personalLight = false, bool bigAlien = false, bool soldierAtWest = false, int soldierHeight = 22, Func<string, string>? edit = null)
    {
        var rules = SightRules(shade, soldierExtra, alienExtra, unitExtra, soldierHeight);
        if (edit is not null) rules = edit(rules);
        if (bigAlien) rules = Replaced(rules, "  - type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR\n", "  - type: ALIEN\n    armor: VEHICLE_ARMOR\n");
        var (campaign, battle, soldier, set) = Open(rules, Terrain);
        var map = battle.Map;
        for (var tile = 0; tile < map.TileCount; tile++)
            foreach (var part in Enum.GetValues<BattleTilePart>()) map.SetPart(tile, part, -1, -1);
        var alien = battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        var arena = new Arena(campaign, battle, soldier, alien, set);
        for (var x = 0; x < 30; x++)
            for (var y = 0; y < 30; y++) arena.Put(x, y, 0, BattleTilePart.Floor, Floor);
        var corner = 29;
        foreach (var unit in battle.Units.Where(unit => unit != soldier && unit != alien)) battle.SetTile(unit, new BattlePosition(corner--, 29, 0));
        var from = soldierAtWest ? 1 : 10;
        battle.SetTile(alien, new BattlePosition(27, 0, 0)); // out of the way while the soldier moves
        arena.Stand(soldier, from, 10, 2);
        arena.Stand(alien, from + distance, 10, 6);
        battle.TogglePersonalLight = personalLight;
        arena.See();
        return arena;
    }

    // ---- seeing ----

    [Fact]
    public void AUnitInTheOpenIsSeenAndTheSeeingFactionsSpottingFieldsAreSet()
    {
        var arena = Flat(unitExtra: "    spotter: 3\n");
        var (soldier, alien) = (arena.Soldier, arena.Alien);
        var alienTile = arena.Tile(15, 10);
        var soldierTile = arena.Tile(10, 10);
        Assert.Equal([alien.Id], soldier.VisibleUnits);
        Assert.Equal([soldier.Id], alien.VisibleUnits);
        Assert.Equal([alien.Id], soldier.UnitsSpottedThisTurn);
        // The player's sight marks the alien visible; the alien's own faction's sight marks the soldier spotted by the hostile faction.
        Assert.True(alien.IsSpottedBy(BattleFaction.Player));
        Assert.True(soldier.IsSpottedBy(BattleFaction.Hostile));
        Assert.False(soldier.IsSpottedBy(BattleFaction.Neutral));
        Assert.False(alien.IsSpottedBy(BattleFaction.Neutral));
        // setTileLastSpotted (and the blind shot's), setTurnsSinceSeen and setTurnsSinceSpotted for the seeing faction.
        Assert.Equal((alienTile, alienTile, 0, 0), (alien.TileLastSpottedBy[(int)BattleFaction.Player], alien.TileLastSpottedForBlindShotBy[(int)BattleFaction.Player],
            alien.TurnsSinceSeenBy[(int)BattleFaction.Player], alien.TurnsSinceSpotted[(int)BattleFaction.Player]));
        Assert.Equal((soldierTile, soldierTile, 0, 0), (soldier.TileLastSpottedBy[(int)BattleFaction.Hostile],
            soldier.TileLastSpottedForBlindShotBy[(int)BattleFaction.Hostile], soldier.TurnsSinceSeenBy[(int)BattleFaction.Hostile],
            soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile]));
        Assert.Equal(-1, alien.TileLastSpottedBy[(int)BattleFaction.Hostile]);
        // The spotted unit's sniper turns: the seeing unit's spotter duration, for the hostile entry and the seeing faction's entry. The soldier
        // (no unit rule) spots for 0 turns; the alien spots for 3.
        Assert.Equal([0, 0, 0], alien.TurnsLeftSpottedForSnipers.Select(static turns => (int)turns));
        Assert.Equal([0, 3, 0], soldier.TurnsLeftSpottedForSnipers.Select(static turns => (int)turns));
        Assert.Equal(0, soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile]);
    }

    [Fact]
    public void ASpotterDurationOfMinusOneIsTheUnitsIntelligence()
    {
        var arena = Flat(unitExtra: "    spotter: -1\n    intelligence: 5\n");
        Assert.Equal(5, arena.Soldier.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile]);
        Assert.Equal(5, arena.Soldier.SpotterDuration(arena.Rules) + arena.Alien.SpotterDuration(arena.Rules) - 0);
    }

    [Fact]
    public void AWallBetweenTheUnitsHidesTheUnitAndASeenUnitThatIsHiddenLeavesTheList()
    {
        var arena = Flat();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.Relight();
        arena.Battle.Sight().CalculateFov(new BattlePosition(12, 10, 0), 1, true, true);
        // The partial update (the event's sector) removes the unit the wall now hides, as a full recheck does.
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.False(Arena.Sees(arena.Alien, arena.Soldier));
        arena.See();
        Assert.Empty(arena.Soldier.VisibleUnits);
        Assert.Empty(arena.Alien.VisibleUnits);
    }

    [Fact]
    public void ALowWallLetsASoldierSeeOverItButAPostOnTheLineDoesNotHideAUnit()
    {
        var arena = Flat();
        // The wall fills 12 voxels; the eye is at 21 and the target spans 1 to 22.
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, LowWall);
        arena.See();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.True(Arena.Sees(arena.Alien, arena.Soldier));
        // A slit along the middle of a tile blocks the centre line but not the slices beside it: both forms of canTargetUnit see past it.
        arena.Clear(12, 10, 0, BattleTilePart.ObjectPart);
        arena.Put(13, 10, 0, BattleTilePart.ObjectPart, Post);
        arena.See();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien)); // the player's slices
        Assert.True(Arena.Sees(arena.Alien, arena.Soldier)); // the artificial intelligence's slices
    }

    [Fact]
    public void ADistantUnitIsSeenAtTheGlobalMaximumAndNotBeyond()
    {
        // By day a unit sees 20 tiles: the quality is 324 less about 318 voxels.
        var near = Flat(distance: 20, soldierAtWest: true);
        Assert.True(Arena.Sees(near.Soldier, near.Alien));
        Assert.True(Arena.Sees(near.Alien, near.Soldier));
        // Nobody sees 21 tiles, whatever the light.
        var far = Flat(distance: 21, soldierAtWest: true);
        Assert.False(Arena.Sees(far.Soldier, far.Alien));
        Assert.False(Arena.Sees(far.Alien, far.Soldier));
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void InTheDarkASoldierSeesNineTiles(int distance, bool seen)
    {
        // Shade 15 with the personal light off: the target tile is dark, so the 9 tiles of the dark view apply (quality 148 less about 142).
        var arena = Flat(15, distance);
        Assert.Equal(seen, Arena.Sees(arena.Soldier, arena.Alien));
        // An alien sees as far in the dark as by day (a hostile unit's view at dark is the maximum).
        Assert.Equal(distance <= 20, Arena.Sees(arena.Alien, arena.Soldier));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void ACamouflagedTargetIsSeenOnlyWithinItsCamouflageDistanceInTheDark(int distance, bool seen)
    {
        // camouflageAtDark 3 sets the distance to 3 tiles (quality 52 less about 45); a farther target is out of it.
        var arena = Flat(15, distance, alienExtra: "    camouflageAtDark: 3\n");
        Assert.Equal(seen, Arena.Sees(arena.Soldier, arena.Alien));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void AntiCamouflageGivesBackSomeOfTheDistance(int distance, bool seen)
    {
        // 3 tiles of camouflage and 2 of anti-camouflage make 5.
        var arena = Flat(15, distance, "    antiCamouflageAtDark: 2\n", "    camouflageAtDark: 3\n");
        Assert.Equal(seen, Arena.Sees(arena.Soldier, arena.Alien));
    }

    [Fact]
    public void ARelativeCamouflageCutsTheDistanceAndNeverBelowOneTile()
    {
        // camouflageAtDark -4 leaves 9 - 4 = 5 tiles; -20 leaves the least, 1.
        var five = Flat(15, 5, alienExtra: "    camouflageAtDark: -4\n");
        Assert.True(Arena.Sees(five.Soldier, five.Alien));
        var six = Flat(15, 6, alienExtra: "    camouflageAtDark: -4\n");
        Assert.False(Arena.Sees(six.Soldier, six.Alien));
        var one = Flat(15, 2, alienExtra: "    camouflageAtDark: -20\n");
        Assert.False(Arena.Sees(one.Soldier, one.Alien));
        var adjacent = Flat(15, 1, alienExtra: "    camouflageAtDark: -20\n");
        Assert.True(Arena.Sees(adjacent.Soldier, adjacent.Alien));
    }

    [Fact]
    public void ABurningUnitIsSeenBeyondTheDarkViewAndHasNoCamouflage()
    {
        var dark = Flat(15, 12, alienExtra: "    camouflageAtDark: 3\n");
        Assert.False(Arena.Sees(dark.Soldier, dark.Alien));
        dark.Alien.Fire = 4;
        dark.See();
        // Fire cancels the camouflage and the darkness: the day view of 20 tiles applies.
        Assert.True(Arena.Sees(dark.Soldier, dark.Alien));
    }

    [Fact]
    public void ALightOnTheTargetsTileEndsItsDarkness()
    {
        var arena = Flat(15, 12);
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        // The soldier's flare-less personal light cannot reach 12 tiles; a lit tile (shade 9 or less) is not dark. The ambient layer of the
        // alien's tile carries a light source of 20 (TESTSET object 15 in the lighting tests, here a direct light).
        arena.Map.AddLight(arena.Tile(22, 10), 8, BattleLightLayer.Items);
        Assert.Equal(7, arena.Battle.TileShade(new BattlePosition(22, 10, 0)));
        arena.Forget();
        arena.Battle.RecalculateFov();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
    }

    [Fact]
    public void ThePersonalLightTogglesWhatTheSoldierSeesInTheDark()
    {
        // A soldier who sees 6 tiles in the dark: the alien at 8 tiles is lit by the personal light (15 - 8 = 7 of light, shade 8) and seen
        // by the day view, and not seen once the light is off, which recalculates the field of view.
        var arena = Flat(15, 8, "    visibilityAtDark: 6\n", personalLight: true);
        Assert.Equal(8, arena.Battle.TileShade(new BattlePosition(18, 10, 0)));
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        arena.Battle.TogglePersonalLighting(arena.Rules);
        Assert.False(arena.Battle.TogglePersonalLight);
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        arena.Battle.TogglePersonalLighting(arena.Rules);
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
    }

    [Fact]
    public void SmokeBetweenTheUnitsReducesTheQualityOfSightUntilItIsGone()
    {
        // Day, 6 tiles: quality 324 - 94 - (smoke density * 100) * 324 / 96000. Two tiles of smoke 15 (480) leave 68; four (960) leave -94.
        var arena = Flat(distance: 6);
        for (var x = 13; x <= 14; x++) arena.Map.SetSmoke(arena.Tile(x, 10), 15);
        arena.See();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        for (var x = 12; x <= 15; x++) arena.Map.SetSmoke(arena.Tile(x, 10), 15);
        arena.See();
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.False(Arena.Sees(arena.Alien, arena.Soldier));
        // Smoke within the first two tiles of the line counts half: three tiles of smoke 15 (720) leave -13 at x 13 to 15, and 48 at x 11 to 13,
        // where 360 of it lies near the unit and only 540 counts.
        foreach (var x in new[] { 12, 13, 14, 15 }) arena.Map.SetSmoke(arena.Tile(x, 10), 0);
        for (var x = 13; x <= 15; x++) arena.Map.SetSmoke(arena.Tile(x, 10), 15);
        arena.See();
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        for (var x = 13; x <= 15; x++) arena.Map.SetSmoke(arena.Tile(x, 10), 0);
        for (var x = 11; x <= 13; x++) arena.Map.SetSmoke(arena.Tile(x, 10), 15);
        arena.See();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
    }

    [Fact]
    public void HeatVisionSeesThroughSmokeAndAFireIsSeenThroughByDefault()
    {
        var smoky = Flat(distance: 6, soldierExtra: "    heatVision: 100\n");
        for (var x = 12; x <= 15; x++) smoky.Map.SetSmoke(smoky.Tile(x, 10), 15);
        smoky.See();
        Assert.True(Arena.Sees(smoky.Soldier, smoky.Alien)); // the smoke factor is 0
        // The smoke of a tile on fire counts as fire density, which the default armor sees through (visibilityThroughFire 100).
        var burning = Flat(distance: 6);
        for (var x = 12; x <= 15; x++)
        {
            burning.Map.SetSmoke(burning.Tile(x, 10), 15);
            burning.Map.SetFire(burning.Tile(x, 10), 3);
        }
        burning.See();
        Assert.True(Arena.Sees(burning.Soldier, burning.Alien));
        var blinded = Flat(distance: 6, soldierExtra: "    visibilityThroughFire: 0\n");
        for (var x = 12; x <= 15; x++)
        {
            blinded.Map.SetSmoke(blinded.Tile(x, 10), 15);
            blinded.Map.SetFire(blinded.Tile(x, 10), 3);
        }
        blinded.See();
        Assert.False(Arena.Sees(blinded.Soldier, blinded.Alien));
    }

    // ---- psi vision ----

    [Theory]
    [InlineData("", true)] // 6 tiles within the 8 of psi vision
    [InlineData("    psiCamouflage: 4\n", false)] // the camouflage cuts it to 4
    [InlineData("    psiCamouflage: 7\n", true)] // a camouflage above the vision leaves it
    [InlineData("    psiCamouflage: -3\n", false)] // 8 - 3 = 5
    [InlineData("    psiCamouflage: -1\n", true)] // 8 - 1 = 7
    [InlineData("    fearImmune: true\n", false)] // a fear immune unit is not sensed
    public void PsiVisionSensesAUnitBehindAWallUnlessItsArmorHidesIt(string alienExtra, bool seen)
    {
        var arena = Flat(distance: 6, soldierExtra: "    psiVision: 8\n", alienExtra: alienExtra);
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.See();
        Assert.Equal(seen, Arena.Sees(arena.Soldier, arena.Alien));
        // The alien has no psi vision, and the wall hides the soldier from it.
        Assert.False(Arena.Sees(arena.Alien, arena.Soldier));
    }

    // ---- view sectors ----

    [Fact]
    public void AUnitBehindTheViewerIsNotSeenUntilItTurnsRound()
    {
        var arena = Flat(distance: 5);
        arena.Stand(arena.Soldier, 10, 10, 6); // west: the alien is east
        arena.See();
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        arena.Stand(arena.Soldier, 10, 10, 2);
        arena.See();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        // Each direction's quadrant: the soldier sees along its facing, to its side and not behind.
        var expected = new Dictionary<int, bool> { [0] = false, [1] = true, [2] = true, [3] = true, [4] = false, [5] = false, [6] = false, [7] = false };
        foreach (var (direction, canSee) in expected)
        {
            arena.Soldier.SetDirection(direction);
            Assert.Equal(canSee, arena.Soldier.CheckViewSector(new BattlePosition(15, 10, 0)));
        }
    }

    [Fact]
    public void ABigUnitIsSeenThroughItsFourTiles()
    {
        var arena = Flat(distance: 6, bigAlien: true);
        Assert.Equal(2, arena.Alien.ArmorSize);
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        Assert.True(Arena.Sees(arena.Alien, arena.Soldier));
        Assert.Equal([arena.Alien.Id], arena.Soldier.VisibleUnits);
        // A wall in front of the whole unit hides it.
        arena.Put(13, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.Put(13, 11, 0, BattleTilePart.ObjectPart, Wall);
        arena.See();
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
    }

    // ---- the lists, the player's and the other factions' spotting ----

    [Fact]
    public void ANeutralUnitIsMarkedVisibleToThePlayerButIsNeverListed()
    {
        var arena = Flat();
        // The reference lists only hostile and non-hostile pairs: a neutral unit is not in the player's visible list, nor the player's in its.
        arena.Alien.Faction = BattleFaction.Neutral;
        arena.See();
        Assert.Empty(arena.Soldier.VisibleUnits);
        Assert.Empty(arena.Alien.VisibleUnits);
        Assert.True(arena.Alien.IsSpottedBy(BattleFaction.Player));
        Assert.False(arena.Soldier.IsSpottedBy(BattleFaction.Neutral));
        Assert.Equal(255, arena.Alien.TurnsSinceSeenBy[(int)BattleFaction.Player]);
    }

    [Fact]
    public void ANeutralUnitSeesAHostileUnitAndItsFactionSpotsIt()
    {
        var arena = Flat();
        // Make the soldier the neutral one: the hostile alien is then spotted by the neutral faction.
        arena.Soldier.Faction = BattleFaction.Neutral;
        arena.See();
        Assert.Equal([arena.Alien.Id], arena.Soldier.VisibleUnits);
        Assert.True(arena.Alien.IsSpottedBy(BattleFaction.Neutral));
        Assert.False(arena.Alien.IsSpottedBy(BattleFaction.Player));
        Assert.Equal(arena.Tile(15, 10), arena.Alien.TileLastSpottedBy[(int)BattleFaction.Neutral]);
        // The hostile alien sees the non-hostile soldier too.
        Assert.True(arena.Soldier.IsSpottedBy(BattleFaction.Hostile));
    }

    [Fact]
    public void TheUnitsAddedToTheSpottedListAreCountedOnceAndASpottedUnitStaysSpottedWhenHidden()
    {
        var arena = Flat();
        Assert.Equal([arena.Alien.Id], arena.Soldier.UnitsSpottedThisTurn);
        // A second look adds no one to the spotted list.
        arena.Battle.RecalculateFov();
        Assert.Equal([arena.Alien.Id], arena.Soldier.UnitsSpottedThisTurn);
        // The wall now hides the alien: it leaves the visible list but stays spotted (the walk, S8, clears the flag).
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.Relight();
        arena.Battle.RecalculateFov();
        Assert.Empty(arena.Soldier.VisibleUnits);
        Assert.Equal([arena.Alien.Id], arena.Soldier.UnitsSpottedThisTurn);
        Assert.True(arena.Alien.IsSpottedBy(BattleFaction.Player));
    }

    [Fact]
    public void TheListReturnsTrueOnlyForNewSightings()
    {
        var arena = Flat();
        var sight = arena.Battle.Sight();
        // Everything is seen already: the second pass spots no one new.
        Assert.False(sight.CalculateUnitsInFov(arena.Soldier));
        arena.Soldier.UnitsSpottedThisTurn.Clear();
        arena.Soldier.ClearVisibleUnits();
        Assert.True(sight.CalculateUnitsInFov(arena.Soldier));
    }

    [Fact]
    public void ASeenUnitLeavesTheVisibleListByTheLastUnitTakingItsPlace()
    {
        var arena = Flat();
        var soldier = arena.Soldier;
        soldier.ClearVisibleUnits();
        Assert.True(soldier.AddToVisibleUnits(7));
        Assert.True(soldier.AddToVisibleUnits(8));
        Assert.True(soldier.AddToVisibleUnits(9));
        Assert.False(soldier.AddToVisibleUnits(8));
        Assert.True(soldier.RemoveFromVisibleUnits(7));
        Assert.Equal([9, 8], soldier.VisibleUnits);
        Assert.False(soldier.RemoveFromVisibleUnits(7));
        Assert.Equal([7, 8, 9], soldier.UnitsSpottedThisTurn.Skip(soldier.UnitsSpottedThisTurn.Count - 3));
        Assert.True(soldier.HasVisibleUnit(arena.Soldier));
        Assert.False(soldier.HasVisibleUnit(arena.Alien));
    }

    // ---- tiles ----

    [Fact]
    public void ThePlayersSightDiscoversTheTilesInFrontAndTheWallsEastAndSouthOfThem()
    {
        var arena = Flat();
        var map = arena.Map;
        // In front, in the cone: the floor, and the west wall of the tile to the east and the north wall of the tile to the south.
        Assert.True(map.IsDiscovered(arena.Tile(14, 10), BattleTilePart.Floor));
        Assert.True(map.IsDiscovered(arena.Tile(15, 10), BattleTilePart.WestWall));
        Assert.True(map.IsDiscovered(arena.Tile(14, 11), BattleTilePart.NorthWall));
        // Behind the soldier nothing is discovered.
        Assert.False(map.IsDiscovered(arena.Tile(5, 10), BattleTilePart.Floor));
        Assert.True(arena.Soldier.HasVisibleTile(arena.Tile(14, 10)));
        Assert.False(arena.Soldier.HasVisibleTile(arena.Tile(5, 10)));
        // The tiles of the cone only: nothing beyond 20 tiles, nothing in the other quadrants.
        Assert.False(arena.Soldier.HasVisibleTile(arena.Tile(10, 25)));
        // The counts: two per tile for the player (addToVisibleTiles adds one and the field of view another), and the explored turn.
        Assert.Equal(2, map.Visibility(arena.Tile(14, 8)));
        Assert.Equal(arena.Battle.Turn, map.LastExplored(arena.Tile(14, 8), BattleFaction.Player));
        Assert.Equal(0, map.LastExplored(arena.Tile(10, 10), BattleFaction.Hostile));
        // A full recomputation takes one per tile away and adds two again: the reference's count grows by one each time. Clearing the tiles of
        // another faction's unit takes one away too, though it added none: (14, 10) is also in the alien's sight, so it stays at 2.
        arena.Battle.RecalculateFov();
        Assert.Equal(3, map.Visibility(arena.Tile(14, 8)));
        Assert.Equal(2, map.Visibility(arena.Tile(14, 10)));
    }

    [Fact]
    public void AWallEndsTheLineOfSightAndTheTileItStopsAtIsNotSeen()
    {
        var arena = Flat();
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        arena.See();
        // A wall object stops a line entering its tile unless the tile is the target (the wall's face is revealed): it is seen, nothing behind it.
        Assert.True(arena.Soldier.HasVisibleTile(arena.Tile(11, 10)));
        Assert.True(arena.Soldier.HasVisibleTile(arena.Tile(12, 10)));
        Assert.False(arena.Soldier.HasVisibleTile(arena.Tile(13, 10)));
        // The diagonal beside it is open.
        Assert.True(arena.Soldier.HasVisibleTile(arena.Tile(12, 8)));
        // The west wall of the wall's tile is discovered with the tile in front of it, and the floor behind stays dark.
        Assert.True(arena.Map.IsDiscovered(arena.Tile(12, 10), BattleTilePart.WestWall));
        Assert.False(arena.Map.IsDiscovered(arena.Tile(13, 10), BattleTilePart.Floor));
        var trajectory = new List<BattlePosition>();
        Assert.Equal(256, arena.Battle.Sight().CalculateLineTile(new BattlePosition(10, 10, 0), new BattlePosition(14, 10, 0), trajectory));
        Assert.Equal(new BattlePosition(12, 10, 0), trajectory[^1]);
        trajectory.Clear();
        Assert.Equal(0, arena.Battle.Sight().CalculateLineTile(new BattlePosition(10, 10, 0), new BattlePosition(14, 6, 0), trajectory));
        Assert.Equal(5, trajectory.Count);
    }

    [Fact]
    public void TheHostileAndNeutralFactionsDiscoverTilesTheSameWay()
    {
        var arena = Flat();
        var map = arena.Map;
        // The alien faces west from (15, 10): its floor tiles to the west are discovered by the hostile faction, not the neutral, and the
        // player's flags are the soldier's.
        Assert.True(map.IsDiscovered(arena.Tile(12, 10), BattleTilePart.Floor, BattleFaction.Hostile));
        Assert.True(map.IsDiscovered(arena.Tile(11, 10), BattleTilePart.WestWall, BattleFaction.Hostile));
        Assert.False(map.IsDiscovered(arena.Tile(12, 10), BattleTilePart.Floor, BattleFaction.Neutral));
        Assert.False(map.IsDiscovered(arena.Tile(22, 10), BattleTilePart.Floor, BattleFaction.Hostile));
        Assert.True(map.IsDiscovered(arena.Tile(12, 10), BattleTilePart.Floor, BattleFaction.Player));
        // Only the player's sight counts the tile as visible; the hostile faction's explored turn is its own turn's.
        Assert.Equal(0, map.LastExplored(arena.Tile(12, 10), BattleFaction.Hostile));
        // The viewers' level views show what each faction has discovered.
        var player = arena.Battle.LevelView(new BattleViewer(BattleFaction.Player), 0)!;
        var hostile = arena.Battle.LevelView(new BattleViewer(BattleFaction.Hostile), 0)!;
        var neutral = arena.Battle.LevelView(new BattleViewer(BattleFaction.Neutral), 0)!;
        // (22, 10) lies in front of the soldier and behind the alien; (3, 10) the other way round.
        bool Shows(BattleLevelView view, int x) => view.Tiles.Any(tile => tile.Position == new BattlePosition(x, 10, 0) && tile.Parts.Count != 0);
        Assert.Equal((true, false), (Shows(player, 22), Shows(player, 3)));
        Assert.Equal((false, true), (Shows(hostile, 22), Shows(hostile, 3)));
        Assert.Equal((false, false), (Shows(neutral, 22), Shows(neutral, 3)));
    }

    [Fact]
    public void AnEventsSectorCoversTheTilesBehindItAndAPartialUpdateAppendsWhatTheChangeReveals()
    {
        // A wall hides a unit; removing it and updating around it appends the newly seen tiles and unit, as a full recomputation finds them.
        var partial = Flat(distance: 8);
        partial.Put(13, 10, 0, BattleTilePart.ObjectPart, Wall);
        partial.See();
        Assert.False(Arena.Sees(partial.Soldier, partial.Alien));
        var before = partial.Soldier.VisibleTiles.ToHashSet();
        partial.Clear(13, 10, 0, BattleTilePart.ObjectPart);
        partial.Relight();
        partial.Battle.Sight().CalculateFov(new BattlePosition(13, 10, 0), 1, true, true);
        Assert.True(Arena.Sees(partial.Soldier, partial.Alien));
        Assert.True(before.IsSubsetOf(partial.Soldier.VisibleTiles));

        var full = Flat(distance: 8);
        Assert.True(Arena.Sees(full.Soldier, full.Alien));
        Assert.Equal(full.Soldier.VisibleTiles.OrderBy(static tile => tile), partial.Soldier.VisibleTiles.OrderBy(static tile => tile));
        for (var tile = 0; tile < full.Map.TileCount; tile++)
            Assert.Equal(full.Map.IsDiscovered(tile, BattleTilePart.Floor), partial.Map.IsDiscovered(tile, BattleTilePart.Floor));
    }

    [Fact]
    public void AnEventBehindTheUnitChangesNothing()
    {
        var arena = Flat();
        var tiles = arena.Soldier.VisibleTiles.ToArray();
        var count = arena.Map.Visibility(arena.Tile(14, 10));
        // The one-tile event at (3, 10) is behind the east-facing soldier: calculateTilesInFOV returns before it clears anything.
        arena.Battle.Sight().CalculateTilesInFov(arena.Soldier, new BattlePosition(3, 10, 0), 1);
        Assert.Equal(tiles, arena.Soldier.VisibleTiles);
        Assert.Equal(count, arena.Map.Visibility(arena.Tile(14, 10)));
    }

    [Fact]
    public void AnOutUnitSeesNothingAndLosesItsTiles()
    {
        var arena = Flat();
        Assert.NotEmpty(arena.Soldier.VisibleTiles);
        arena.Soldier.Status = BattleUnitStatus.Dead;
        Assert.False(arena.Battle.Sight().CalculateFov(arena.Soldier));
        Assert.Empty(arena.Soldier.VisibleTiles);
        // clearVisibleTiles takes one from the two the tile holds.
        Assert.Equal(1, arena.Map.Visibility(arena.Tile(14, 10)));
        // A unit that is out is not seen either.
        arena.Soldier.Status = BattleUnitStatus.Standing;
        arena.Alien.Status = BattleUnitStatus.Dead;
        arena.See();
        Assert.Empty(arena.Soldier.VisibleUnits);
    }

    // ---- voxels ----

    [Fact]
    public void TheVoxelMapHoldsTerrainByItsLayersAndUnitsByTheirTemplates()
    {
        var arena = Flat();
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, LowWall);
        var sight = arena.Battle.Sight();
        // The low wall fills the lower 12 voxels of its tile and nothing above.
        Assert.Equal(BattleVoxelType.Object, sight.VoxelCheck(new BattlePosition(12 * 16 + 8, 10 * 16 + 8, 5), null));
        Assert.Equal(BattleVoxelType.Object, sight.VoxelCheck(new BattlePosition(12 * 16 + 8, 10 * 16 + 8, 11), null));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(new BattlePosition(12 * 16 + 8, 10 * 16 + 8, 12), null));
        // The soldier's pillar: 22 voxels tall above its tile's floor, 8 wide in the middle; the unit excluded or not.
        var soldier = new BattlePosition(10 * 16 + 8, 10 * 16 + 8, 10);
        Assert.Equal(BattleVoxelType.Unit, sight.VoxelCheck(soldier, null));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(soldier, arena.Soldier));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(soldier with { Z = 23 }, null));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(soldier with { X = 10 * 16 + 1 }, null));
        Assert.Equal(BattleVoxelType.OutOfBounds, sight.VoxelCheck(new BattlePosition(-1, 5, 5), null));
        Assert.Equal(BattleVoxelType.OutOfBounds, sight.VoxelCheck(new BattlePosition(30 * 16, 5, 5), null));
        // onlyVisible skips a unit the player has not spotted; excludeAllBut keeps only the one named.
        Assert.Equal(BattleVoxelType.Unit, sight.VoxelCheck(soldier, null, onlyVisible: true));
        var alien = new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 10);
        Assert.Equal(BattleVoxelType.Unit, sight.VoxelCheck(alien, null, onlyVisible: true));
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, false);
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(alien, null, onlyVisible: true));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(alien, null, excludeAllBut: arena.Soldier));
        Assert.Equal(BattleVoxelType.Unit, sight.VoxelCheck(alien, null, excludeAllBut: arena.Alien));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(alien, null, excludeAllUnits: true));
    }

    [Fact]
    public void ALineOfVoxelsStopsAtTheFirstThingAndRecordsItsTrajectory()
    {
        var arena = Flat();
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        var sight = arena.Battle.Sight();
        var origin = new BattlePosition(10 * 16 + 8, 10 * 16 + 8, 21);
        var trajectory = new List<BattlePosition>();
        // The wall fills x 192-207: the line from x 168 stops at 192, and the impact voxel is stored again after the others.
        var hit = sight.CalculateLineVoxel(origin, new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 21), true, trajectory, arena.Soldier);
        Assert.Equal(BattleVoxelType.Object, hit);
        Assert.Equal(25 + 1, trajectory.Count);
        Assert.Equal(new BattlePosition(12 * 16, 10 * 16 + 8, 21), trajectory[^1]);
        Assert.Equal(trajectory[^1], trajectory[^2]);
        // Without storing, only the impact is kept; a clear line returns empty.
        trajectory.Clear();
        sight.CalculateLineVoxel(origin, new BattlePosition(15 * 16 + 8, 10 * 16 + 8, 21), false, trajectory, arena.Soldier);
        Assert.Equal([new BattlePosition(12 * 16, 10 * 16 + 8, 21)], trajectory);
        trajectory.Clear();
        Assert.Equal(BattleVoxelType.Empty, sight.CalculateLineVoxel(origin, new BattlePosition(10 * 16 + 8, 14 * 16 + 8, 21), true, trajectory, arena.Soldier));
        Assert.Equal(4 * 16 + 1, trajectory.Count);
        // The step sizes: 0, 1, 1 and then half the distance over two voxels.
        Assert.Equal([0f, 1f, 1f, 1f], Enumerable.Range(0, 4).Select(index => BattleSight.TrajectoryStepSize(trajectory, index)));
        Assert.Equal(0f, BattleSight.TrajectoryStepSize(trajectory, trajectory.Count));
    }

    [Fact]
    public void BeforeTheGameNoUnitStopsALine()
    {
        var (campaign, _) = Requested(SightRules(0));
        Started(campaign);
        var battle = Battle(campaign);
        Assert.True(battle.IsBeforeGame);
        var soldier = battle.Units.Single(static unit => unit.Faction == BattleFaction.Player);
        var sight = battle.Sight();
        var inside = soldier.Position!.Value;
        var origin = new BattlePosition(inside.X * 16 + 8, inside.Y * 16 + 8, inside.Z * 24 + 21);
        Assert.Equal(BattleVoxelType.Empty, sight.CalculateLineVoxel(origin with { Z = 10 }, origin with { Z = 12 }, false, null, null));
        Assert.Empty(soldier.VisibleUnits);
    }

    [Fact]
    public void TheSightOriginIsTheEyeAndTheSnapShotOriginIsFourVoxelsBelowTheTop()
    {
        var arena = Flat();
        var sight = arena.Battle.Sight();
        // Standing at (10, 10, 0): the middle of the tile at the height 22 - 1.
        Assert.Equal(new BattlePosition(168, 168, 21), sight.SightOriginVoxel(arena.Soldier));
        arena.Soldier.Kneel(true);
        Assert.Equal(new BattlePosition(168, 168, 13), sight.SightOriginVoxel(arena.Soldier));
        arena.Soldier.Kneel(false);
        // A snap shot from the unit's tile towards a target tile: the top less 4, shifted to the tile's edge in the direction (east: 14, 1... by
        // the reference's table 8, 14, 15, 15, 8, 1, 1, 1 for x and 1, 1, 8, 15, 15, 15, 8, 1 for y).
        Assert.Equal(new BattlePosition(160 + 15, 160 + 8, 22 - 4), sight.SnapShotOriginVoxel(arena.Soldier, new BattlePosition(15, 10, 0)));
        Assert.Equal(new BattlePosition(160 + 8, 160 + 1, 22 - 4), sight.SnapShotOriginVoxel(arena.Soldier, new BattlePosition(10, 5, 0)));
        Assert.Equal(new BattlePosition(160 + 1, 160 + 8, 22 - 4), sight.SnapShotOriginVoxel(arena.Soldier, new BattlePosition(5, 10, 0)));
        // A unit tall enough to reach the next level has its eye there when the tile above has no floor, and cut to the tile's top under a ceiling.
        var tall = Flat(soldierHeight: 30);
        Assert.Equal(new BattlePosition(168, 168, 29), tall.Battle.Sight().SightOriginVoxel(tall.Soldier));
        tall.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        Assert.Equal(new BattlePosition(168, 168, 23), tall.Battle.Sight().SightOriginVoxel(tall.Soldier));
    }

    // ---- loading ----

    [Fact]
    public void ALoadedBattlesSightEqualsTheNeverSavedBattlesAndTheSavedSpottingStays()
    {
        var (campaign, _, _, _) = Open(SightRules(0), Terrain);
        var battle = Battle(campaign);
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles(Terrain));
        var loaded = Battle(restored);
        Assert.Equal(battle.Units.Count, loaded.Units.Count);
        foreach (var unit in battle.Units)
        {
            var other = loaded.Units.Single(candidate => candidate.Id == unit.Id);
            Assert.Equal(unit.VisibleUnits, other.VisibleUnits);
            Assert.Equal(unit.VisibleTiles.OrderBy(static tile => tile), other.VisibleTiles.OrderBy(static tile => tile));
            foreach (var faction in Enum.GetValues<BattleFaction>()) Assert.Equal(unit.IsSpottedBy(faction), other.IsSpottedBy(faction));
        }
        // The save keeps the tiles that are not void (the reference's quirk for the player's discovery, which the generator also sets on bare
        // ground); on those the loaded discovery equals the never-saved one, elsewhere the field of view alone has set it.
        var saved = campaign.Capture().Battle!.Tiles.Select(static tile => tile.Index).ToHashSet();
        for (var tile = 0; tile < battle.Map.TileCount; tile++)
        {
            Assert.Equal(battle.Map.Visibility(tile), loaded.Map.Visibility(tile));
            foreach (var faction in Enum.GetValues<BattleFaction>())
            {
                var expected = battle.Map.DiscoveredFlags(tile, faction);
                var actual = loaded.Map.DiscoveredFlags(tile, faction);
                if (saved.Contains(tile) || faction != BattleFaction.Player) Assert.Equal(expected, actual);
                else Assert.Equal(actual, actual & expected);
            }
        }
    }

    [Fact]
    public void ADiscoveryOfAHostileViewerIsSavedAndReloadedAndALoadOnlyAddsToIt()
    {
        var arena = Flat();
        var map = arena.Map;
        // Hostile and neutral discovery the units' sight does not reach: the far corner.
        var corner = arena.Tile(0, 0);
        map.SetDiscovered(corner, BattleTilePart.Floor, true, BattleFaction.Hostile);
        map.SetDiscovered(corner, BattleTilePart.NorthWall, true, BattleFaction.Neutral);
        var snapshot = arena.Battle.Capture();
        Assert.Contains(snapshot.FactionDiscovery, entry => entry.Tile == corner && entry.HostileFlags == 7 && entry.NeutralFlags == 4);
        var written = OxceBattleSave.Write(snapshot);
        var reread = OxceBattleSave.Read(BattleSaveRootTests.Parse(BattleSaveRootTests.Emit(written)), arena.Campaign.Content);
        Assert.Equal(snapshot.FactionDiscovery, reread.FactionDiscovery);
        var restored = CampaignState.Restore(arena.Campaign.Capture(), arena.Campaign.Content, new SplitMix64RandomSource(3), TerrainFiles(Terrain));
        var loaded = Battle(restored);
        Assert.Equal(7, loaded.Map.DiscoveredFlags(corner, BattleFaction.Hostile));
        Assert.Equal(4, loaded.Map.DiscoveredFlags(corner, BattleFaction.Neutral));
        // The load added the units' own sight on top of it.
        Assert.True(loaded.Map.IsDiscovered(arena.Tile(12, 10), BattleTilePart.Floor, BattleFaction.Hostile));
    }

    [Fact]
    public void TheFirstTurnsSightListsWhatTheSoldiersSeeAndTheirInfoLists()
    {
        var (campaign, battle, soldier, _) = Open(SightRules(0), Terrain);
        var view = campaign.GetQuery<IBattleViewQuery>()!;
        var info = view.QueryUnitInfo(BattleViewer.LocalPlayer, soldier.Id)!;
        // updateSoldierInfo: the unit's own visible units, then the spotted hostile ones; both lists agree here.
        Assert.Equal(soldier.VisibleUnits, info.VisibleUnitIds.Take(soldier.VisibleUnits.Count));
        Assert.Equal(info.VisibleUnitIds.Distinct().Count(), info.VisibleUnitIds.Count);
        foreach (var id in info.VisibleUnitIds) Assert.True(battle.UnitById(id)!.IsSpottedBy(BattleFaction.Player));
    }

    // ---- refusals ----

    [Fact]
    public void ABoundVisibilityScriptRefusesTheBriefingsConfirmationAndATerrainWithoutLineOfFireDataStops()
    {
        var rules = Replaced(Rules, "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n", "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    scripts:\n      visibilityUnit: |\n        return;\n");
        var (campaign, _) = Requested(rules, "UFO_ALIENS");
        Started(campaign);
        Assert.IsType<BriefingOpened>(Assert.Single(campaign.Execute(new BeginBriefing("T", "C")).Events));
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        Assert.Equal("battle.visibility-script-unsupported", blocked.Code);
        Assert.Equal(BattlePhase.Briefing, Battle(campaign).Phase);

        // An armor that points past the end of LOFTEMPS.DAT is where the reference's sight would throw.
        var (broken, _) = Requested(Replaced(Rules, "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    allowInv: false\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]",
            "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    allowInv: false\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [9]"));
        var failed = Assert.IsType<CampaignActionBlocked>(Assert.Single(broken.Execute(new StartBattle()).Events));
        Assert.Equal(CampaignBattles.GenerationFailedCode, failed.Code);
        Assert.Contains("template 9", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABattleWithoutLoftempsFilesIsNotGenerated()
    {
        var (campaign, _) = Requested(Rules);
        campaign.BattleTerrainFiles = TerrainFiles(static files => files.Remove("GEODATA/LOFTEMPS.DAT"));
        var failed = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal(CampaignBattles.GenerationFailedCode, failed.Code);
        Assert.Contains("LOFTEMPS", failed.Reason, StringComparison.Ordinal);
    }
}
