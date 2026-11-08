using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Savegames.Oxce;
using Xunit;
using static Oxce.CompatibilityTests.BattleMovementGroundworkTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>Phase 7 branch 4 S4 audit: the sight branches and refusals <see cref="BattleSightTests"/> leaves out.</summary>
public sealed class BattleSightCoverageTests
{
    private static BattlePosition At(int x, int y, int z) => new(x, y, z);

    // ---- levels ----

    [Fact]
    public void AUnitSeesAcrossLevelsFromAboveAndFromBelow()
    {
        var above = Flat();
        above.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        above.Battle.SetTile(above.Soldier, At(10, 10, 1));
        above.See();
        Assert.True(Arena.Sees(above.Soldier, above.Alien)); // aims down at the target (the top and bottom slices)
        var below = Flat();
        below.Put(15, 10, 1, BattleTilePart.Floor, Floor);
        below.Battle.SetTile(below.Alien, At(15, 10, 1));
        below.See();
        Assert.True(Arena.Sees(below.Soldier, below.Alien)); // aims up
        Assert.True(Arena.Sees(below.Alien, below.Soldier)); // the artificial intelligence's form
    }

    [Fact]
    public void ACandidateUnitIsTargetedAsIfItStoodOnAnEmptyTile()
    {
        var arena = Flat();
        var sight = arena.Battle.Sight();
        var origin = sight.SightOriginVoxel(arena.Soldier);
        Assert.False(sight.CanTargetUnit(origin, arena.Tile(13, 10), out _, arena.Soldier));
        Assert.True(sight.CanTargetUnit(origin, arena.Tile(13, 10), out _, arena.Soldier, arena.Alien));
        Assert.False(sight.CanTargetUnit(origin, arena.Tile(15, 10), out _, arena.Alien));
    }

    [Fact]
    public void VoxelsOfGravLiftsOpenDoorsAndTallUnitsReachingUp()
    {
        var arena = Flat(soldierHeight: 30);
        arena.Put(12, 10, 0, BattleTilePart.Floor, GravLift);
        arena.Put(13, 10, 0, BattleTilePart.WestWall, UfoDoor);
        var sight = arena.Battle.Sight();
        Assert.Equal(BattleVoxelType.Floor, sight.VoxelCheck(new BattlePosition(12 * 16 + 3, 168, 1), null));
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(new BattlePosition(12 * 16 + 3, 168, 2), null));
        var voxel = new BattlePosition(13 * 16 + 3, 168, 10);
        Assert.Equal(BattleVoxelType.WestWall, sight.VoxelCheck(voxel, null));
        arena.Map.SetDoorFrame(arena.Tile(13, 10), BattleTilePart.WestWall, true);
        Assert.Equal(BattleVoxelType.Empty, sight.VoxelCheck(voxel, null));
        // The 30 voxel soldier pokes 6 voxels into the open tile above, which holds its pillar there.
        Assert.Equal(BattleVoxelType.Unit, sight.VoxelCheck(new BattlePosition(168, 168, 26), null));
        Assert.Equal(arena.Soldier, arena.Battle.OverlappingUnit(arena.Rules, arena.Tile(10, 10, 1)));
        var short22 = Flat();
        Assert.Null(short22.Battle.OverlappingUnit(short22.Rules, short22.Tile(10, 10, 1)));
    }

    [Fact]
    public void TheOriginsOfBigAndTallUnits()
    {
        var big = Flat(distance: 6, bigAlien: true);
        Assert.Equal(new BattlePosition(16 * 16 + 16, 10 * 16 + 16, 22), big.Battle.Sight().SightOriginVoxel(big.Alien));
        var tall = Flat(soldierHeight: 30);
        var target = At(15, 10, 0);
        // Open air above: the snap shot's origin keeps its height; under a ceiling it is cut to the tile and cut again by the weapon shift.
        Assert.Equal(new BattlePosition(175, 168, 26), tall.Battle.Sight().SnapShotOriginVoxel(tall.Soldier, target));
        tall.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        Assert.Equal(new BattlePosition(175, 168, 19), tall.Battle.Sight().SnapShotOriginVoxel(tall.Soldier, target));
    }

    [Fact]
    public void AnUpdateAroundAChangeCanSkipTheTilesAndTheDarknessThresholdIsExact()
    {
        var arena = Flat();
        var tiles = arena.Soldier.VisibleTiles.Count;
        arena.Soldier.ClearVisibleUnits();
        arena.Battle.Sight().CalculateFov(At(12, 10, 0), 1, updateTiles: false);
        Assert.Equal(tiles, arena.Soldier.VisibleTiles.Count);
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));

        // Tile shade 9 is not dark (darker than maxDarknessToSeeUnits is), shade 10 is.
        var dark = Flat(15, 12);
        dark.Map.AddLight(dark.Tile(22, 10), 6, BattleLightLayer.Items);
        Assert.Equal(9, dark.Battle.TileShade(At(22, 10, 0)));
        dark.Forget();
        dark.Battle.RecalculateFov();
        Assert.True(Arena.Sees(dark.Soldier, dark.Alien));
        dark.Map.ResetLightMulti(dark.Tile(22, 10), BattleLightLayer.Items);
        dark.Map.AddLight(dark.Tile(22, 10), 5, BattleLightLayer.Items);
        dark.Forget();
        dark.Battle.RecalculateFov();
        Assert.False(Arena.Sees(dark.Soldier, dark.Alien));
    }

    [Fact]
    public void ASoldiersSightAddsItsBonusesAndIsClamped()
    {
        var arena = Flat();
        var armor = arena.Rules.Battle.Equipment.Armors["SUIT"];
        var stats = new Dictionary<string, short>();
        RuntimeSoldierBonusRule Bonus(int dark, int day, int psi, int smoke, int fire) =>
            new(0, stats) { VisibilityAtDark = dark, VisibilityAtDay = day, PsiVision = psi, VisibilityThroughSmoke = smoke, VisibilityThroughFire = fire };
        var vision = BattleUnitVision.ForSoldier(arena.Rules, armor, [Bonus(3, -5, 2, 10, -20), Bonus(1, 0, 1, 5, 0)]);
        Assert.Equal(new BattleUnitVision(13, 15, 3, 15, 80), vision);
        Assert.Equal(new BattleUnitVision(20, 1, 0, 0, 100), BattleUnitVision.ForSoldier(arena.Rules, armor, [Bonus(50, -50, 0, 0, 0)]));
        Assert.Equal(1, BattleUnitVision.ForSoldier(arena.Rules, armor, [Bonus(-50, 0, 0, 0, 0)]).MaxViewDistanceAtDark);
        // A unit rule's unit: hostile ones see as far in the dark as by day, the rest 9.
        Assert.Equal((20, 20), (arena.Alien.Vision.MaxViewDistanceAtDark, arena.Alien.Vision.MaxViewDistanceAtDay));
        Assert.Equal((9, 20), (arena.Soldier.Vision.MaxViewDistanceAtDark, arena.Soldier.Vision.MaxViewDistanceAtDay));
    }

    // ---- the inventory, loading, refusals ----

    [Fact]
    public void AMoveInTheInventoryOfARunningBattleSeesAgain()
    {
        var arena = Flat(edit: BattleInventoryTests.WithCosts);
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall); // a change the sight has not met
        arena.Relight();
        Assert.True(Arena.Sees(arena.Soldier, arena.Alien));
        var item = arena.Battle.InventoryOf(arena.Soldier).First(static held => held.Slot == "STR_RIGHT_HAND");
        var result = arena.Campaign.Execute(new MoveInventoryItem(arena.Soldier.Id, item.Id, "STR_LEFT_HAND", 0, 0));
        var moved = Assert.Single(result.Events); Assert.True(moved is InventoryItemMoved, (moved as CampaignActionBlocked)?.Reason);
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
    }

    [Fact]
    public void ABriefingSaveWaitsForTheFirstTurnsSight()
    {
        var (campaign, _) = Requested(SightRules(0));
        Started(campaign);
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
        var loaded = Battle(restored);
        Assert.True(loaded.IsBeforeGame);
        Assert.All(loaded.Units, static unit => Assert.Empty(unit.VisibleUnits));
        for (var tile = 0; tile < loaded.Map.TileCount; tile++)
            Assert.Equal(Battle(campaign).Map.IsDiscovered(tile, BattleTilePart.Floor), loaded.Map.IsDiscovered(tile, BattleTilePart.Floor));
    }

    private static string WithVisibilityScript(string rules) => rules.Replace("  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n",
        "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    scripts:\n      visibilityUnit: |\n        return;\n");

    [Fact]
    public void ARunningBattleWithAVisibilityScriptRefusesItsCommandsAndKeepsItsSavedSpottingOnLoad()
    {
        var (campaign, _) = Requested(WithVisibilityScript(SightRules(0)));
        Started(campaign);
        var battle = Battle(campaign);
        battle.Phase = BattlePhase.InProgress;
        battle.Turn = 1;
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SelectNextUnit(battle.Units[0].Id, false, false)).Events));
        Assert.Equal("battle.visibility-script-unsupported", blocked.Code);
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
        Assert.True(Battle(restored).IsBeforeGame is false);
        Assert.All(Battle(restored).Units, static unit => Assert.Empty(unit.VisibleUnits));
    }

    [Fact]
    public void AGlobalVisibilityEventAndMissingTemplatesAreRefused()
    {
        var events = SightRules(0) + "\nextended:\n  scripts:\n    visibilityUnit:\n      - new: global-sight\n        offset: 1\n        code: |\n          return;\n";
        var (scripted, _) = Requested(events);
        Started(scripted);
        Assert.Equal("battle.visibility-script-unsupported", Battle(scripted).VisibilityScriptRefusal(scripted.Content)!.Code);

        var (campaign, battle, _, _) = Open(SightRules(0), Terrain);
        battle.VoxelData = new BattleVoxelData(new ushort[16]);
        Assert.Equal("battle.line-of-fire-data-invalid", battle.FirstTurnRefusal(campaign.Content, BattleRestoreCampaign.From(campaign))!.Code);

        // A save naming a template the file lacks is corrupted; so is a discovery tile outside the map.
        var snapshot = campaign.Capture().Battle!;
        var content = campaign.Content;
        var shortFile = new BattleTerrainLoader(content.RuntimeRules.Battle, TerrainFiles(static files => files["GEODATA/LOFTEMPS.DAT"] = new byte[32]));
        Assert.Contains("template", Assert.Throws<InvalidDataException>(() => BattleState.Restore(snapshot, content, shortFile,
            "", BattleRestoreCampaign.From(campaign))).Message, StringComparison.Ordinal);
        var loader = new BattleTerrainLoader(content.RuntimeRules.Battle, TerrainFiles(Terrain));
        var outside = snapshot with { FactionDiscovery = [new BattleFactionDiscovery(99_999, 1, 0)] };
        Assert.Contains("discovery tile", Assert.Throws<InvalidDataException>(() => BattleState.Restore(outside, content, loader, "",
            BattleRestoreCampaign.From(campaign))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{hostile: 1}", "tile")]
    [InlineData("{tile: 3, hostile: 16}", "flags")]
    [InlineData("{tile: 3, neutral: -1}", "flags")]
    public void AMalformedDiscoveryEntryIsRefusedOnRead(string entry, string message)
    {
        var (campaign, _, _, _) = Open(SightRules(0), Terrain);
        var snapshot = BattleSaveUnitTests.SightFree(campaign.Capture().Battle!);
        var text = BattleSaveRootTests.Emit(OxceBattleSave.Write(snapshot)) + "oxcePortBattle:\n  discovery:\n    - " + entry + "\n";
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(BattleSaveRootTests.Parse(text), campaign.Content)).Message,
            StringComparison.Ordinal);
        var twice = BattleSaveRootTests.Emit(OxceBattleSave.Write(snapshot)) + "oxcePortBattle:\n  discovery:\n    - {tile: 3, hostile: 1}\n    - {tile: 3, neutral: 1}\n";
        Assert.Contains("twice", Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(BattleSaveRootTests.Parse(twice), campaign.Content)).Message,
            StringComparison.Ordinal);
    }
}
