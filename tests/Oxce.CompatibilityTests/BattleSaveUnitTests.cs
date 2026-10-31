using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;
using static Oxce.CompatibilityTests.BattleSaveRootTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 2 S3: the battle's units through the save module (<c>SavedBattleGame::load</c>'s unit passes,
/// <c>BattleUnit::load</c> and <c>save</c>, <c>AIModule::load</c> and <c>save</c>, <c>BattleUnitStatistics</c>,
/// <c>resetUnitTiles</c>; <c>src/Savegame/BattleUnit.cpp</c>, <c>SavedBattleGame.cpp</c> and <c>Battlescape/AIModule.cpp</c>
/// at 4df3a5e) and the BattleUnit constructors the load and the generator share.
/// </summary>
public sealed class BattleSaveUnitTests
{
    // A painted armor and a unit rule with the constructor's inputs, added to the battle rules' lists.
    private static readonly string UnitRules = BattleStartTests.Rules
        .Replace("\narmors:\n", "\narmors:\n" +
            "  - type: PAINTED_ARMOR\n" +
            "    frontArmor: 12\n" +
            "    sideArmor: 8\n" +
            "    leftArmorDiff: 1\n" +
            "    rearArmor: 5\n" +
            "    underArmor: 2\n" +
            "    movementType: 3\n" +
            "    spriteFaceGroup: 6\n" +
            "    spriteFaceColor: [96, 97, 98, 99, 100, 101, 102, 103]\n" +
            "    spriteRankGroup: 9\n" +
            "    spriteRankColor: [1, 2, 3, 4, 5, 6, 7, 8]\n" +
            "    moveCost: {basePercent: [90, 80]}\n" +
            "    corpseBattle: [CORPSE]\n" +
            "    corpseGeo: CORPSE\n" +
            "    loftempsSet: [0]\n" +
            "", StringComparison.Ordinal)
        .Replace("\nstartingConditions:\n", "\nstartingConditions:\n" +
            "  - type: SC_BAN\n" +
            "    forbiddenArmorsInNextStage: [SMALL_VEHICLE_ARMOR, PAINTED_ARMOR]\n", StringComparison.Ordinal)
        .Replace("\nunits:\n", "\nunits:\n" +
            "  - type: PAINTED\n" +
            "    armor: PAINTED_ARMOR\n" +
            "    rank: STR_LIVE_LEADER\n" +
            "    aggression: 3\n" +
            "    spawnUnit: ALIEN\n" +
            "    vip: true\n" +
            "    pickUpWeaponsMoreActively: 1\n" +
            "    stats: {tu: 40, health: 20, stamina: 30, mana: 7}\n" +
            "", StringComparison.Ordinal) +
        "\nextended:\n  tags:\n    BattleUnit: {UNIT_TAG: int, UNIT_LIST: RuleList}\n" +
        // A starting condition banning armors in the next stage, a soldier bonus with side armor, the mod's AI pick-up
        // default, and armor multipliers per difficulty.
        "soldierBonuses:\n  - name: ARMOR_BONUS\n    frontArmor: 3\n    sideArmor: -100\n" +
        "ai: {pickUpWeaponsMoreActively: true}\n" +
        "armorMultipliers: [0.5, 1, 2]\narmorMultipliersAbs: [0, 0, 3]\n";

    private static readonly Lazy<(CampaignState Campaign, BattleState Battle)> Fixture = new(() => Generated(UnitRules));

    private static RuntimeContent Content => Fixture.Value.Campaign.Content;

    private static BattleRestoreCampaign Campaign => BattleRestoreCampaign.From(Fixture.Value.Campaign);

    private const string Root = "selectedUnit: -1\nwidth: 4\nlength: 4\nheight: 2\n";

    private const string Alien = "{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, status: 0, position: [1, 1, 0]";

    private static BattleSnapshot Read(string units, IDiagnosticSink? diagnostics = null) =>
        OxceBattleSave.Read(Parse(Root + units), Content, diagnostics);

    private static BattleState Restore(string units) =>
        BattleState.Restore(Read(units), Content, Terrain(), campaign: Campaign);

    private static BattleUnit Unit(BattleState battle, int id) => battle.Units.Single(unit => unit.Id == id);

    private static YamlMappingNode WrittenUnit(BattleState battle, int index = 0) =>
        (YamlMappingNode)((YamlSequenceNode)Value(OxceBattleSave.Write(battle.Capture()), "units")).Items[index];

    private static YamlNode Value(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? node! : throw new KeyNotFoundException(key);

    private static string Text(YamlMappingNode map, string key) => ((YamlScalarNode)Value(map, key)).Value;

    private static string Failure(Action action) => Assert.Throws<InvalidDataException>(action).Message;

    /// <summary>A snapshot as the load leaves it: resetUnitTiles makes the player's units visible.</summary>
    internal static BattleSnapshot Loaded(BattleSnapshot snapshot) => snapshot with
    {
        Units = [.. snapshot.Units.Select(static unit => unit.Faction == BattleFaction.Player ? unit with { Visible = true } : unit)],
    };

    /// <summary>
    /// A loaded rewrite of <paramref name="text"/> is stable: the first rewrite of a battle before its first turn adds what
    /// the load sets (resetUnitTiles' visibility), and a second rewrite is byte-stable.
    /// </summary>
    internal static void AssertLoadedRewriteIsStable(BattleState restored, string text, RuntimeContent content, BattleRestoreCampaign campaign)
    {
        var first = Emit(OxceBattleSave.Write(restored.Capture(), Parse(text)));
        var again = BattleState.Restore(OxceBattleSave.Read(Parse(first), content), content, Terrain(), campaign: campaign);
        Assert.Equal(first, Emit(OxceBattleSave.Write(again.Capture(), Parse(first))));
    }

    [Fact]
    public void GeneratedUnitsWriteReloadAndConform()
    {
        var (campaign, battle) = Fixture.Value;
        var snapshot = battle.Capture();
        var soldier = snapshot.Units.Single(static unit => unit.Faction == BattleFaction.Player);
        var alien = snapshot.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        // BattleUnit(Soldier*): type SOLDIER, the soldier's rank and auto-combat; the aggression left 0 (ADR 0039).
        Assert.Equal((BattleUnitSnapshot.SoldierType, 0, 0, 0, true, 0), (soldier.UnitType, soldier.RankInt, soldier.RankIntUnified,
            soldier.KilledBy, soldier.AllowAutoCombat, soldier.Aggression));
        // createTempUnit: a hostile unit's AI module starts at its spawn node, aiming at the player; killedBy is its faction.
        Assert.NotNull(alien.Ai);
        Assert.Equal((battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile).StartNodeId ?? -1, -1, 0, (int?)0, (int?)1),
            (alien.Ai!.FromNode, alien.Ai.ToNode, alien.Ai.Mode, alien.Ai.TargetFaction, alien.KilledBy));
        // adjustStats on the zero sides of its armor: the multiplier keeps 0, armorMultipliersAbs (3 at this difficulty) adds.
        Assert.Equal([3, 3, 3, 3, 3, 0, 0, 0, 0], alien.Armor);

        var written = OxceBattleSave.Write(snapshot);
        BattleSaveConformance.Check(written);
        var text = Emit(written);
        var restored = BattleState.Restore(OxceBattleSave.Read(Parse(text), campaign.Content), campaign.Content, Terrain(),
            campaign: BattleRestoreCampaign.From(campaign));
        Assert.Equivalent(Loaded(snapshot), restored.Capture(), strict: true);
        Assert.Equal(battle.SelectedUnitId, restored.SelectedUnitId);
        AssertLoadedRewriteIsStable(restored, text, campaign.Content, BattleRestoreCampaign.From(campaign));
    }

    [Fact]
    public void TheUnitRuleConstructorGivesWhatTheSaveLeavesOut()
    {
        var battle = Restore("units:\n  - {id: 1000000, genUnitType: PAINTED, genUnitArmor: PAINTED_ARMOR, faction: 2, status: 0, position: [1, 1, 0]}\n");
        var unit = Unit(battle, 1000000);
        // updateArmorFromNonSoldier: the statistics and full time units, energy, health and mana; the five sides (left is
        // side + leftArmorDiff), the diagonal ones 0; the armor's move costs and movement type by depth (float walks on land).
        Assert.Equal((40, 30, 20, 7, 100), (unit.TimeUnits, unit.Energy, unit.Health, unit.Mana, unit.Morale));
        Assert.Equal([12, 9, 8, 5, 2, 0, 0, 0, 0], unit.CurrentArmor);
        Assert.Equal(((90, 80), 0, 0), (unit.MoveCosts.Base, unit.MovementType, unit.OriginalMovementType));
        // The unit rule's aggression, VIP flag and weapon pick-up; the save's spawn unit (none here, the rule's is dropped).
        Assert.Equal((3, true, true, "", 2), (unit.Aggression, unit.IsVip, unit.PickUpWeaponsMoreActively, unit.SpawnUnit, unit.KilledBy));
        // A neutral unit's unified rank is a seedless draw of 0-7; the recolor pairs are the armor's groups shifted by 4.
        Assert.InRange(unit.RankIntUnified, 0, 7);
        Assert.Equal([96, 144], unit.Recolor.Select(static pair => pair.Group));
        // findWithFallback: the look's entry, else its lower bits' (here the low three bits always find one).
        Assert.InRange(unit.Recolor[0].Color, 96, 103);
        Assert.Equal(unit.RankIntUnified + 1, unit.Recolor[1].Color);
        Assert.Null(unit.Ai);
        // The written unit gives the constructor's values back; the armor's move cost is not written.
        var written = WrittenUnit(battle);
        Assert.Equal(("40", "12", "3"), (Text(written, "tu"), ((YamlScalarNode)((YamlSequenceNode)Value(written, "armor")).Items[0]).Value,
            Text(written, "aggression")));
        Assert.False(written.TryGet("moveCost", out _));
        Assert.False(written.TryGet("movementType", out _));
        Assert.Equal("true", Text(written, "vip"));
    }

    [Fact]
    public void AHostileUnitsConstructorRanksItByItsRankString()
    {
        var unit = Unit(Restore("units:\n  - {id: 1000000, genUnitType: PAINTED, genUnitArmor: PAINTED_ARMOR, faction: 1, status: 0, position: [1, 1, 0]}\n"),
            1000000);
        // deriveHostileRank: STR_LIVE_LEADER is the fifth rank (4).
        Assert.Equal((4, 1), (unit.RankIntUnified, unit.KilledBy));
    }

    [Fact]
    public void TheSoldierConstructorGivesWhatTheSaveLeavesOut()
    {
        var soldier = Fixture.Value.Battle.Units.Single(static unit => unit.SoldierId is not null);
        var battle = Restore($"units:\n  - {{id: {soldier.Id}, genUnitType: SOLDIER, faction: 0, status: 0, position: [1, 1, 0]}}\n");
        var unit = Unit(battle, soldier.Id);
        // The soldier's own armor (genUnitArmor is not read; here it is absent), statistics, auto-combat and rank.
        Assert.Equal((soldier.ArmorId, soldier.TimeUnits, soldier.Health, soldier.Stats, true, 0),
            (unit.ArmorId, unit.TimeUnits, unit.Health, unit.Stats, unit.AllowAutoCombat, unit.Rank));
        Assert.Equal(soldier.UnitType, unit.UnitType);
        Assert.Equal((BattleControllerKind.Player, 0), (unit.Controller.Kind, unit.Controller.PlayerSlot));
        Assert.Equal("SOLDIER", Text(WrittenUnit(battle), "genUnitType"));
    }

    [Fact]
    public void ASoldierUnitWithoutItsSoldierIsACorruptedSave()
    {
        var message = Failure(() => Restore("units:\n  - {id: 77, faction: 0, status: 0, position: [1, 1, 0]}\n"));
        Assert.Contains("soldier 77", message, StringComparison.Ordinal);
        // Without a campaign, no soldier can be found.
        Assert.Contains("soldier 1", Failure(() => BattleState.Restore(Read("units:\n  - {id: 1, faction: 0, position: [1, 1, 0]}\n"), Content,
            Terrain())), StringComparison.Ordinal);
    }

    [Fact]
    public void AUnitWhoseRuleOrArmorIsGoneIsLeftOutWithAWarning()
    {
        var diagnostics = new DiagnosticCollector(10);
        var snapshot = Read("units:\n  - {id: 1000000, genUnitType: GONE, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1}\n" +
            "  - {id: 1000001, genUnitType: ALIEN, genUnitArmor: GONE_ARMOR, faction: 1}\n  - " + Alien.Replace("1000000", "1000002") + "}\n",
            diagnostics);
        Assert.Equal([1000002], snapshot.Units.Select(static unit => unit.Id));
        Assert.Equal(2, diagnostics.Snapshot().Count(static item => item.Code == OxceBattleSave.SkippedCode));
        // The skipped units are gone from the rewrite too.
        Assert.Single(((YamlSequenceNode)Value(OxceBattleSave.Write(snapshot), "units")).Items);
    }

    [Theory]
    [InlineData("{genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1}", "'id'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR}", "'faction'")]
    [InlineData("{id: 1000000, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1}", "'genUnitType'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, faction: 1}", "'genUnitArmor'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 5}", "'faction'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, originalFaction: -1}", "'originalFaction'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, tu: many}", "'tu'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, position: [1, 2]}", "'position'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, armor: [1, x]}", "'armor'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, recolor: [[1]]}", "'recolor'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, moveCost: {basePercent: [1]}}", "'moveCost'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, AI: {fromNode: x}}", "'fromNode'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, spawnUnit: [A]}", "'spawnUnit'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, currStats: {tu: x}}", "'currStats'")]
    [InlineData("{id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, previousOwner: x}", "'previousOwner'")]
    public void UnreadableUnitsFailNamingTheKey(string unit, string key)
    {
        Assert.Contains(key, Failure(() => Read("units:\n  - " + unit + "\n")), StringComparison.Ordinal);
    }

    [Fact]
    public void AUnitThatIsNoMappingFails()
    {
        Assert.Contains("'units'", Failure(() => Read("units: [3]\n")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOriginalFactionDefaultsToTheFaction()
    {
        var snapshot = Read("units:\n  - " + Alien + "}\n  - " + Alien.Replace("1000000", "1000001").Replace("[1, 1, 0]", "[2, 1, 0]") +
            ", originalFaction: 2}\n");
        Assert.Equal([BattleFaction.Hostile, BattleFaction.Neutral], snapshot.Units.Select(static unit => unit.OriginalFaction));
        var battle = BattleState.Restore(snapshot, Content, Terrain(), campaign: Campaign);
        var written = OxceBattleSave.Write(battle.Capture());
        var units = ((YamlSequenceNode)Value(written, "units")).Items.Cast<YamlMappingNode>().ToArray();
        // save: originalFaction only when it differs from the faction.
        Assert.False(units[0].TryGet("originalFaction", out _));
        Assert.Equal("2", Text(units[1], "originalFaction"));
    }

    [Fact]
    public void AMindControlledAlienLosesItsAiModuleAndJoinsThePlayer()
    {
        var battle = Restore("units:\n  - {id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 0, originalFaction: 1, status: 0," +
            " position: [1, 1, 0], AI: {fromNode: 0, toNode: -1, AIMode: 2, wasHitBy: [3]}}\n");
        var unit = Unit(battle, 1000000);
        // The load keeps no AI module for a unit of the player's faction; its original faction stays hostile.
        Assert.Null(unit.Ai);
        Assert.Equal((BattleFaction.Player, BattleFaction.Hostile, BattleControllerKind.Player), (unit.Faction, unit.OriginalFaction, unit.Controller.Kind));
        // It is the player's first standing unit, so the selected one; the player sees it.
        Assert.Equal(1000000, battle.SelectedUnitId);
        Assert.True(unit.IsSpottedBy(BattleFaction.Player));
        Assert.False(WrittenUnit(battle).TryGet("AI", out _));
    }

    [Fact]
    public void TheAiModuleIsKeptOnlyForAStandingUnitThatIsNotThePlayers()
    {
        const string ai = ", AI: {fromNode: 0, toNode: 9, AIMode: 2, wasHitBy: [3, 4], weaponPickedUp: true}}";
        var battle = Restore("units:\n  - " + Alien + ai + "\n  - " + Alien.Replace("1000000", "1000001").Replace("status: 0", "status: 6") + ai + "\n  - " +
            Alien.Replace("1000000", "1000002").Replace("status: 0", "status: 10") + ai + "\n  - " +
            Alien.Replace("1000000", "1000003").Replace("status: 0", "status: 7") + ai + "\n");
        // Dead (6) and ignored (10) units lose it; an unconscious (7) one keeps it.
        Assert.Equal([true, false, false, true], battle.Units.Select(static unit => unit.Ai is not null));
        var kept = Unit(battle, 1000000).Ai!;
        // AIModule::load: a node ID outside the battle's nodes (none here) is none.
        Assert.Equal((-1, -1, 2, true), (kept.FromNode, kept.ToNode, kept.Mode, kept.WeaponPickedUp));
        Assert.Equal([3, 4], kept.WasHitBy);
        var written = (YamlMappingNode)Value(WrittenUnit(battle), "AI");
        Assert.Equal(["fromNode", "toNode", "AIMode", "wasHitBy", "weaponPickedUp"], written.Entries.Select(static entry => entry.ScalarKey));
    }

    [Fact]
    public void TheAiModuleWritesTheIdsOfItsNodes()
    {
        // AIModule::load reads fromNode and toNode as indexes; AIModule::save writes the nodes' getID(). A dummy node's
        // saved ID is not its index (Node() sets 0).
        var battle = Restore("nodes:\n  - {id: 0, position: [1, 1, 0]}\n  - {id: 0, position: [1, 1, 0], dummy: true}\n  - {id: 2, position: [1, 1, 0]}\n" +
            "units:\n  - " + Alien + ", AI: {fromNode: 1, toNode: 2}}\n");
        Assert.Equal((1, 2), (Unit(battle, 1000000).Ai!.FromNode, Unit(battle, 1000000).Ai!.ToNode));
        var written = (YamlMappingNode)Value(WrittenUnit(battle), "AI");
        Assert.Equal(("0", "2"), (Text(written, "fromNode"), Text(written, "toNode")));
        // As in the reference, the reread resolves the dummy's ID 0 to node 0, a real node; from there the rewrite is stable.
        var reread = BattleState.Restore(OxceBattleSave.Read(OxceBattleSave.Write(battle.Capture()), Content), Content, Terrain(), campaign: Campaign);
        Assert.Equal((0, 2), (Unit(reread, 1000000).Ai!.FromNode, Unit(reread, 1000000).Ai!.ToNode));
        var rewritten = (YamlMappingNode)Value(WrittenUnit(reread), "AI");
        Assert.Equal(("0", "2"), (Text(rewritten, "fromNode"), Text(rewritten, "toNode")));
    }

    [Fact]
    public void AnAiTargetFactionIsWrittenOnlyForAHostileUnitTurnedNeutral()
    {
        var battle = Restore("units:\n  - " + Alien.Replace("faction: 1", "faction: 2, originalFaction: 1") + ", AI: {targetFaction: 1}}\n  - " +
            Alien.Replace("1000000", "1000001").Replace("[1, 1, 0]", "[2, 1, 0]") + ", AI: {targetFaction: 2}}\n");
        Assert.Equal("1", Text((YamlMappingNode)Value(WrittenUnit(battle, 0), "AI"), "targetFaction"));
        // A hostile unit's own target is read but the save leaves it out.
        Assert.Equal(2, Unit(battle, 1000001).Ai!.TargetFaction);
        Assert.False(((YamlMappingNode)Value(WrittenUnit(battle, 1), "AI")).TryGet("targetFaction", out _));
        // Without the key, the constructor's: a hostile unit targets the player.
        Assert.Equal(0, Unit(Restore("units:\n  - " + Alien + ", AI: {}}\n"), 1000000).Ai!.TargetFaction);
    }

    [Fact]
    public void ThePointerValuedDuplicatesReadTheFirstValueCutToItsLowByte()
    {
        var snapshot = Read("units:\n  - " + Alien + ", turnsSinceSpotted: 0x5803c54, turnsLeftSpottedForSnipers: 0x5803c57, turnsSinceStunned: 3," +
            " turnsSinceSpotted: 255, turnsLeftSpottedForSnipers: 0, turnsSinceStunned: 9, turnsSinceSpottedByXcom: 4, turnsSinceSpottedByCivilian: 300}\n");
        var unit = snapshot.Units[0];
        // useIndex reads the first of repeated keys; tryReadAs<int> into a Uint8 keeps the low byte.
        Assert.Equal([4, 0x54, 300 & 0xFF], unit.TurnsSinceSpotted);
        Assert.Equal([0, 0x57, 0], unit.TurnsLeftSpottedForSnipers);
        Assert.Equal(3, unit.TurnsSinceStunned);
        // Written once each, in integer form, which the reference reads back the same.
        var written = (YamlMappingNode)((YamlSequenceNode)Value(OxceBattleSave.Write(snapshot), "units")).Items[0];
        Assert.Single(written.Entries, static entry => entry.ScalarKey == "turnsSinceSpotted");
        Assert.Single(written.Entries, static entry => entry.ScalarKey == "turnsSinceStunned");
        Assert.Equal(("84", "87", "3", "4", "44"), (Text(written, "turnsSinceSpotted"), Text(written, "turnsLeftSpottedForSnipers"),
            Text(written, "turnsSinceStunned"), Text(written, "turnsSinceSpottedByXcom"), Text(written, "turnsSinceSpottedByCivilian")));
        // The defaults are not written (tryWriteAs).
        Assert.False(written.TryGet("turnsLeftSpottedForSnipersByXcom", out _));
    }

    [Fact]
    public void ArmorSidesAreReadAsSavedAndAShortListKeepsTheConstructorsSides()
    {
        var full = Read("units:\n  - " + Alien.Replace("ALIEN", "PAINTED").Replace("SMALL_VEHICLE_ARMOR", "PAINTED_ARMOR").Replace("faction: 1", "faction: 2") +
            ", armor: [1, 2, 3, 4, 5, 2621472, -555819297, 7, 8, 99]}\n");
        // At most SIDE_MAX (9) sides; the diagonal ones are kept as saved.
        Assert.Equal([1, 2, 3, 4, 5, 2621472, -555819297, 7, 8], full.Units[0].Armor);
        var battle = Restore("units:\n  - " + Alien.Replace("ALIEN", "PAINTED").Replace("SMALL_VEHICLE_ARMOR", "PAINTED_ARMOR")
            .Replace("faction: 1", "faction: 2") + ", armor: [1, 2]}\n");
        Assert.Equal([1, 2, 8, 5, 2, 0, 0, 0, 0], Unit(battle, 1000000).CurrentArmor);
        Assert.Equal(["1", "2", "8", "5", "2", "0", "0", "0", "0"],
            ((YamlSequenceNode)Value(WrittenUnit(battle), "armor")).Items.Cast<YamlScalarNode>().Select(static value => value.Value));
    }

    [Fact]
    public void SavedValuesOverrideTheConstructorsAndRoundTrip()
    {
        const string unit = "{id: 1000000, genUnitType: PAINTED, genUnitArmor: PAINTED_ARMOR, faction: 2, status: 7, wantsToSurrender: true, isSurrendering: true," +
            " position: [3, 2, 1], direction: 5, directionTurret: 6, tu: 11, health: 12, mana: 13, stunlevel: 14, energy: 15, morale: 16, kneeled: true," +
            " floating: true, fatalWounds: [1, 2], fire: 2, expBravery: 1, expReactions: 2, expFiring: 3, expThrowing: 4, expPsiSkill: 5," +
            " expPsiStrength: 6, expMana: 7, expMelee: 8, currStats: {tu: 70, mana: 9}, turretType: 3, visible: true, turnsSinceSeenByHostile: 2," +
            " tileLastSpottedByPlayer: 17, tileLastSpottedForBlindShotByNeutral: 18, rankInt: 3, rankIntUnified: 5, moraleRestored: 4," +
            " notificationShown: 2, killedBy: 0, kills: 3, aiMedikitUsed: true, spawnUnit: ALIEN, respawn: true," +
            " spawnUnitFaction: 0, motionPoints: 6, customMarker: 2, alreadyRespawned: true, activeHand: STR_LEFT_HAND," +
            " preferredHandForReactions: STR_RIGHT_HAND, reactionsDisabledForLeftHand: true, reactionsDisabledForRightHand: true, murdererId: 4," +
            " fatalShotSide: 2, fatalShotBodyPart: 3, murdererWeapon: GUN, murdererWeaponAmmo: CLIP, recolor: [[16, 5]], mindControllerID: 1000001," +
            " summonedPlayerUnit: true, resummonedFakeCivilian: true, pickUpWeaponsMoreActively: true, disableIndicators: true, movementType: 1," +
            " moveCost: {baseFlyPercent: [50, 60]}, bannedInNextStage: true, meleeAttackedBy: [1000001], allowAutoCombat: false," +
            " aggression: 9, hasPanickedLastTurn: true, tags: {UNIT_TAG: 4}}";
        var battle = Restore("units:\n  - " + unit + "\n");
        var restored = Unit(battle, 1000000);
        Assert.Equal((BattleUnitStatus.Unconscious, 11, 12, 13, 14, 15, 16), (restored.Status, restored.TimeUnits, restored.Health, restored.Mana,
            restored.StunLevel, restored.Energy, restored.Morale));
        Assert.Equal((70, 9, 30), (restored.Stats.TimeUnits, restored.Stats.Mana, restored.Stats.Stamina));
        Assert.Equal([1, 2, 0, 0, 0, 0], restored.FatalWounds);
        Assert.Equal(((90, 80), (50, 60), 1, 0), (restored.MoveCosts.Base, restored.MoveCosts.Fly, restored.MovementType, restored.OriginalMovementType));
        Assert.Equal([new BattleRecolor(16, 5)], restored.Recolor);
        Assert.Equal(("ALIEN", true, 0, true, true), (restored.SpawnUnit, restored.Respawn, restored.SpawnUnitFaction, restored.IsVip,
            restored.PickUpWeaponsMoreActively));
        Assert.Equal(4, restored.ScriptValues!.Get("UNIT_TAG"));
        // An unconscious unit is out: it stands on no tile, keeping its position.
        Assert.Null(restored.Position);
        Assert.Equal(new BattlePosition(3, 2, 1), restored.SavedPosition);

        var written = WrittenUnit(battle);
        BattleSaveConformance.Check(OxceBattleSave.Write(battle.Capture()));
        // The changed movement type and move cost are written.
        Assert.Equal("1", Text(written, "movementType"));
        Assert.Equal(["baseFlyPercent"], ((YamlMappingNode)Value(written, "moveCost")).Entries.Select(static entry => entry.ScalarKey));
        // The vip flag is written when set; allowAutoCombat, aggression and hasPanickedLastTurn are always written.
        Assert.Equal("true", Text(written, "vip"));
        Assert.Equal(("false", "9", "true"), (Text(written, "allowAutoCombat"), Text(written, "aggression"), Text(written, "hasPanickedLastTurn")));
        var reread = BattleState.Restore(OxceBattleSave.Read(Parse(Emit(OxceBattleSave.Write(battle.Capture()))), Content), Content, Terrain(),
            campaign: Campaign);
        Assert.Equivalent(battle.Capture(), reread.Capture(), strict: true);
    }

    [Fact]
    public void FlagsTheSaveWritesOnlyWhenSetComeBackAsTheConstructorsAfterAReload()
    {
        // save writes pickUpWeaponsMoreActively and vip only when set, dontReselect only for the player's units and isBrutal never, so
        // a reload of a false pick-up or vip gives the unit rule's (on here) and the others are lost, as in the reference.
        var battle = Restore("units:\n  - {id: 1000000, genUnitType: PAINTED, genUnitArmor: PAINTED_ARMOR, faction: 2, position: [1, 1, 0]," +
            " pickUpWeaponsMoreActively: false, vip: false, dontReselect: true, isBrutal: true}\n");
        var unit = Unit(battle, 1000000);
        Assert.Equal((false, false, true, true), (unit.PickUpWeaponsMoreActively, unit.IsVip, unit.DontReselect, unit.IsBrutal));
        var written = WrittenUnit(battle);
        Assert.False(written.TryGet("pickUpWeaponsMoreActively", out _));
        Assert.False(written.TryGet("dontReselect", out _));
        Assert.False(written.TryGet("isBrutal", out _));
        var reloaded = Unit(BattleState.Restore(OxceBattleSave.Read(Parse(Emit(OxceBattleSave.Write(battle.Capture()))), Content), Content, Terrain(),
            campaign: Campaign), 1000000);
        Assert.Equal((true, true, false, false), (reloaded.PickUpWeaponsMoreActively, reloaded.IsVip, reloaded.DontReselect, reloaded.IsBrutal));
    }

    [Fact]
    public void AnUnknownSpawnUnitIsNoneAndItsRespawnIsNotRead()
    {
        var unit = Read("units:\n  - " + Alien + ", spawnUnit: GONE, respawn: true, spawnUnitFaction: 0}\n").Units[0];
        Assert.Equal(("", false, 1), (unit.SpawnUnit, unit.Respawn, unit.SpawnUnitFaction));
    }

    [Fact]
    public void TheSelectedAndUndoUnitsAreResolvedAmongThePlayersUnits()
    {
        var soldier = Fixture.Value.Battle.Units.Single(static unit => unit.SoldierId is not null).Id;
        string Player(int id, int status, int x) => $"{{id: {id}, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 0, status: {status}, position: [{x}, 0, 0]}}";
        var units = "units:\n  - " + Player(1000001, 6, 0) + "\n  - " + Player(1000002, 0, 1) + "\n  - " + Player(1000003, 0, 2) + "\n  - " + Alien + "}\n";
        BattleState Load(int selected, int undo) => BattleState.Restore(OxceBattleSave.Read(Parse(
            Root.Replace("selectedUnit: -1", $"selectedUnit: {selected}\nundoUnit: {undo}") + units), Content), Content, Terrain(), campaign: Campaign);
        // -1: the first player unit that is not out.
        Assert.Equal((1000002, (int?)null), (Load(-1, -1).SelectedUnitId, Load(-1, -1).UndoUnitId));
        // A saved unit wins, even when out; a hostile unit is never selected.
        Assert.Equal((1000001, (int?)1000003), (Load(1000001, 1000003).SelectedUnitId, Load(1000001, 1000003).UndoUnitId));
        Assert.Equal((1000002, (int?)null), (Load(1000000, 1000000).SelectedUnitId, Load(1000000, 1000000).UndoUnitId));
        Assert.True(soldier < 1000000);
    }

    [Fact]
    public void PreviousOwnersNameALoadedUnitOrNone()
    {
        var battle = Restore("units:\n  - " + Alien + ", previousOwner: 1000001}\n  - " + Alien.Replace("1000000", "1000001").Replace("[1, 1, 0]", "[2, 1, 0]") +
            ", previousOwner: 5}\n");
        Assert.Equal([1000001, (int?)null], battle.Units.Select(static unit => unit.PreviousOwnerId));
        Assert.Equal("1000001", Text(WrittenUnit(battle), "previousOwner"));
        Assert.False(WrittenUnit(battle, 1).TryGet("previousOwner", out _));
    }

    [Fact]
    public void AnInvalidStatusIsReportedAsTheReferenceLogsIt()
    {
        var diagnostics = new DiagnosticCollector(10);
        var snapshot = Read("units:\n  - " + Alien.Replace("status: 0", "status: 8") + "}\n  - " +
            Alien.Replace("1000000", "1000001").Replace("status: 0", "status: 6") + "}\n", diagnostics);
        var reported = Assert.Single(diagnostics.Snapshot());
        Assert.Equal(OxceBattleSave.UnitStatusCode, reported.Code);
        Assert.Contains("id: 1000000", reported.Message, StringComparison.Ordinal);
        // The unit keeps its status and, not being out, stands on its tile.
        Assert.Equal(new BattlePosition(1, 1, 0), BattleState.Restore(snapshot, Content, Terrain(), campaign: Campaign).Units[0].Position);
    }

    [Fact]
    public void UnitsOnTheMapAndOutOfItRestoreTheirTiles()
    {
        var battle = Restore("units:\n  - " + Alien + "}\n  - " + Alien.Replace("1000000", "1000001").Replace("status: 0", "status: 6") + "}\n  - " +
            Alien.Replace("1000000", "1000002").Replace("[1, 1, 0]", "[9, 9, 0]") + "}\n");
        // resetUnitTiles: a standing unit takes its tile; a dead one and one outside the map take none.
        Assert.Equal([new BattlePosition(1, 1, 0), null, null], battle.Units.Select(static unit => unit.Position));
        Assert.Equal(1000000, battle.Map.UnitAt(battle.TileIndex(new BattlePosition(1, 1, 0))));
        // Written where they are.
        Assert.Equal(["1", "1", "0"], ((YamlSequenceNode)Value(WrittenUnit(battle, 1), "position")).Items.Cast<YamlScalarNode>().Select(static v => v.Value));
        Assert.Equal(["9", "9", "0"], ((YamlSequenceNode)Value(WrittenUnit(battle, 2), "position")).Items.Cast<YamlScalarNode>().Select(static v => v.Value));
        // Two standing units on one tile, or a repeated ID, break the restored battle.
        Assert.Contains("corrupted", Failure(() => Restore("units:\n  - " + Alien + "}\n  - " + Alien.Replace("1000000", "1000001") + "}\n")),
            StringComparison.Ordinal);
        Assert.Contains("corrupted", Failure(() => Restore("units:\n  - " + Alien + "}\n  - " + Alien.Replace("[1, 1, 0]", "[2, 1, 0]") + "}\n")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRefusesAUnitThatIsOutButStandsOnTheMap()
    {
        var battle = Restore("units:\n  - " + Alien + "}\n");
        Unit(battle, 1000000).Status = BattleUnitStatus.Dead;
        Assert.Contains("is out", Assert.Throws<InvalidOperationException>(battle.Validate).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporaryStatisticsRoundTripAndAnEmptyOneIsNull()
    {
        var battle = Restore("units:\n  - " + Alien + ", tempUnitStatistics: {wasUnconcious: true, shotAtCounter: 2, slaveKills: 1, kills: [" +
            "{type: ALIEN, name: IGNORED, rank: R, race: X, weapon: GUN, weaponAmmo: CLIP, status: 6, faction: 1, mission: 3, turn: 4, side: 258, bodypart: 1, id: 9}," +
            " {name: Bob}]}}\n  - " + Alien.Replace("1000000", "1000001").Replace("[1, 1, 0]", "[2, 1, 0]") + ", tempUnitStatistics: ~}\n");
        var statistics = Unit(battle, 1000000).Statistics;
        Assert.Equal((true, 2, 1), (statistics.WasUnconscious, statistics.ShotAtCounter, statistics.SlaveKills));
        // BattleUnitKills::load: a name only without a type; the side is a Uint8; the defaults are the constructor's.
        Assert.Equal(("ALIEN", "", 2, 9), (statistics.Kills[0].Type, statistics.Kills[0].Name, statistics.Kills[0].Side, statistics.Kills[0].Id));
        Assert.Equal(("Bob", 10, 1), (statistics.Kills[1].Name, statistics.Kills[1].Status, statistics.Kills[1].Faction));
        var written = (YamlMappingNode)Value(WrittenUnit(battle), "tempUnitStatistics");
        Assert.Equal(["wasUnconcious", "kills", "shotAtCounter", "slaveKills"], written.Entries.Select(static entry => entry.ScalarKey));
        Assert.False(((YamlMappingNode)((YamlSequenceNode)Value(written, "kills")).Items[0]).TryGet("name", out _));
        Assert.Equal("~", Assert.IsType<YamlNullNode>(Value(WrittenUnit(battle, 1), "tempUnitStatistics")).Spelling);
    }

    [Fact]
    public void UnitTagsAreReadSkippedOrRefusedAsTheRootsAre()
    {
        var diagnostics = new DiagnosticCollector(10);
        var unit = Read("units:\n  - " + Alien + ", tags: {UNIT_TAG: 3, GONE_TAG: 1}}\n", diagnostics).Units[0];
        Assert.Equal(["UNIT_TAG"], unit.ScriptValues.Select(static value => value.TagName));
        Assert.Contains(diagnostics.Snapshot(), static item => item.Code == OxceBattleSave.SkippedCode && item.Message.Contains("GONE_TAG", StringComparison.Ordinal));
        Assert.Equal(OxceBattleSave.UnsupportedTagType("UNIT_LIST", "RuleList"),
            Failure(() => Read("units:\n  - " + Alien + ", tags: {UNIT_LIST: 1}}\n")));
    }

    [Fact]
    public void ControllersAndSpottingGoInThePortNodeOnlyWhenTheyDifferFromTheDefaults()
    {
        var battle = Restore("units:\n  - " + Alien + "}\n");
        Assert.False(OxceBattleSave.Write(battle.Capture()).TryGet(OxceBattleSave.PortKey, out _));
        var unit = Unit(battle, 1000000);
        unit.Controller = BattleController.Human(2);
        battle.SetSpotted(unit, BattleFaction.Neutral, true);
        var written = OxceBattleSave.Write(battle.Capture());
        Assert.Equal(Normalized(Parse("oxcePortBattle: {units: [{id: 1000000, controller: player, playerSlot: 2, spottedBy: [2]}]}\n")),
            Normalized(new YamlMappingNode(written.Span, [written.Entries.Single(static entry => entry.ScalarKey == OxceBattleSave.PortKey)], null, null)));
        var reread = BattleState.Restore(OxceBattleSave.Read(Parse(Emit(written)), Content), Content, Terrain(), campaign: Campaign);
        Assert.Equal((BattleController.Human(2), true, false), (reread.Units[0].Controller, reread.Units[0].IsSpottedBy(BattleFaction.Neutral),
            reread.Units[0].IsSpottedBy(BattleFaction.Hostile)));
        Assert.Contains("controller", Failure(() => Read("units:\n  - " + Alien + "}\noxcePortBattle: {units: [{id: 1000000, controller: robot}]}\n")),
            StringComparison.Ordinal);
        Assert.Contains("spottedBy", Failure(() => Read("units:\n  - " + Alien + "}\noxcePortBattle: {units: [{id: 1000000, spottedBy: [0]}]}\n")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownUnitKeysSurviveALoadedRewriteInTheSourcesOrder()
    {
        var source = Parse(Root + "units:\n  - {portFuture: 7, " + Alien[1..] + ", isNotBrutal: true}\n");
        var battle = BattleState.Restore(OxceBattleSave.Read(source, Content), Content, Terrain(), campaign: Campaign);
        var written = (YamlMappingNode)((YamlSequenceNode)Value(OxceBattleSave.Write(battle.Capture(), source), "units")).Items[0];
        Assert.Equal(["portFuture", "id", "genUnitType"], written.Entries.Take(3).Select(static entry => entry.ScalarKey));
        Assert.False(written.TryGet("isNotBrutal", out _));
        // A battle without units writes no units key.
        Assert.False(OxceBattleSave.Write(new BattleSnapshot(1, 1, 1, "M")).TryGet("units", out _));
    }

    /// <summary>A campaign whose only soldier is the fixture's first soldier with <paramref name="adjust"/> applied.</summary>
    private static (int Id, BattleRestoreCampaign Campaign) SoldierCampaign(Func<SoldierPersonalState, SoldierPersonalState> adjust, int difficulty = 0)
    {
        var id = Fixture.Value.Battle.Units.Single(static unit => unit.SoldierId is not null).Id;
        var soldier = Campaign.Soldiers(id)!;
        var changed = soldier with { Personal = adjust(soldier.Personal) };
        return (id, new BattleRestoreCampaign(candidate => candidate == id ? changed : null, difficulty));
    }

    [Theory]
    [InlineData(0, new[] { 6, 4, 4, 2, 1 })]
    [InlineData(2, new[] { 27, 21, 19, 13, 7 })]
    public void AHostileUnitsArmorTakesTheCampaignDifficultysMultiplier(int difficulty, int[] sides)
    {
        // adjustStats: each side times armorMultipliers[difficulty] (truncated), plus armorMultipliersAbs[difficulty].
        var battle = BattleState.Restore(Read("units:\n  - {id: 1000000, genUnitType: PAINTED, genUnitArmor: PAINTED_ARMOR, faction: 1, position: [1, 1, 0]}\n"),
            Content, Terrain(), campaign: new BattleRestoreCampaign(Campaign.Soldiers, difficulty));
        Assert.Equal([.. sides, 0, 0, 0, 0], Unit(battle, 1000000).CurrentArmor);
        // A neutral unit's armor is not adjusted.
        Assert.Equal([12, 9, 8, 5, 2, 0, 0, 0, 0], Unit(BattleState.Restore(Read("units:\n  - {id: 1000000, genUnitType: PAINTED," +
            " genUnitArmor: PAINTED_ARMOR, faction: 2, position: [1, 1, 0]}\n"), Content, Terrain(), campaign: new BattleRestoreCampaign(Campaign.Soldiers,
            difficulty)), 1000000).CurrentArmor);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(7, 0)]
    public void TheSoldierConstructorTakesTheSoldiersRankBonusArmorAndLook(int rank, int rankInt)
    {
        var (id, campaign) = SoldierCampaign(personal => personal with
        {
            Armor = "PAINTED_ARMOR",
            Rank = rank,
            Gender = 1,
            Look = 2,
            LookVariant = 0,
            TransformationBonuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["ARMOR_BONUS"] = 1 },
        });
        var battle = BattleState.Restore(Read($"units:\n  - {{id: {id}, genUnitType: SOLDIER, genUnitArmor: PAINTED_ARMOR, faction: 0, position: [1, 1, 0]}}\n"),
            Content, Terrain(), campaign: campaign);
        var unit = Unit(battle, id);
        // deriveSoldierRank: rookie to commander keep their rank, any other is 0; the unified rank follows.
        Assert.Equal((rankInt, rankInt), (unit.Rank, unit.RankIntUnified));
        // The armor's sides plus the bonus's (front +3, sides -100), each at least 0.
        Assert.Equal([15, 0, 0, 5, 2, 0, 0, 0, 0], unit.CurrentArmor);
        // setRecolor(look, look, rank) with look = gender + 2 * look + 8 * lookVariant = 5: the face color at 5, the rank's.
        Assert.Equal([new BattleRecolor(96, 101), new BattleRecolor(144, rankInt + 1)], unit.Recolor);
    }

    [Fact]
    public void ARestoredSoldierFailsOnUnknownArmorAndKeepsItsOriginalArmor()
    {
        var (id, unknown) = SoldierCampaign(static personal => personal with { Armor = "GONE_ARMOR" });
        Assert.Contains("unknown armor 'GONE_ARMOR'", Failure(() => BattleState.Restore(Read($"units:\n  - {{id: {id}, faction: 0, position: [1, 1, 0]}}\n"),
            Content, Terrain(), campaign: unknown)), StringComparison.Ordinal);
        // The geoscape soldier's transformedArmor (else replacedArmor) is the armor it wore before the battle.
        var armor = Campaign.Soldiers(id)!.Personal.Armor;
        foreach (var (transformed, replaced, expected) in new[] { ("SUIT", "", "SUIT"), ("", "FLOAT_SUIT", "FLOAT_SUIT"), ("SUIT", "FLOAT_SUIT", "SUIT") })
        {
            var (_, campaign) = SoldierCampaign(personal => personal with { TransformedArmor = transformed, ReplacedArmor = replaced });
            var unit = Unit(BattleState.Restore(Read($"units:\n  - {{id: {id}, genUnitArmor: {armor}, faction: 0, position: [1, 1, 0]}}\n"), Content,
                Terrain(), campaign: campaign), id);
            Assert.Equal(expected, unit.OriginalArmorId);
        }
    }

    [Fact]
    public void ASoldierSavedInOtherArmorThanTheCampaignsSoldierTakesTheSoldiersArmor()
    {
        // new BattleUnit(mod, getSoldier(id), ...): the soldier's armor (which StartBattle set to the battle armor);
        // genUnitArmor is not read, and the rewrite writes the soldier's armor.
        var (id, campaign) = SoldierCampaign(static personal => personal);
        var armor = campaign.Soldiers(id)!.Personal.Armor;
        Assert.NotEqual("SUIT", armor);
        var battle = BattleState.Restore(Read($"units:\n  - {{id: {id}, genUnitArmor: SUIT, faction: 0, position: [1, 1, 0]}}\n"), Content,
            Terrain(), campaign: campaign);
        Assert.Equal(armor, Unit(battle, id).ArmorId);
        Assert.Equal(armor, battle.Capture().Units.Single(unit => unit.Id == id).ArmorId);
    }

    [Fact]
    public void ADeadSoldierIsFoundAmongTheCampaignsDead()
    {
        var (campaign, battle) = Generated(UnitRules);
        var soldier = battle.Units.Single(static unit => unit.SoldierId is not null);
        var owner = campaign.BaseStates.Single(candidate => candidate.Soldiers.Exists(state => state.Id == soldier.Id));
        campaign.KillSoldier(owner, owner.Soldiers.Single(state => state.Id == soldier.Id));
        var dead = BattleRestoreCampaign.From(campaign).Soldiers(soldier.Id);
        Assert.NotNull(dead);
        Assert.Equal(soldier.UnitType, dead!.SoldierType);
        Assert.Null(BattleRestoreCampaign.From(campaign).Soldiers(999));
    }

    [Fact]
    public void TheStartingConditionBansTheArmorsItForbidsInTheNextStageOnlyAtGeneration()
    {
        var battle = BattleState.Restore(new BattleSnapshot(4, 4, 2, "M") { StartingConditionId = "SC_BAN" }, Content, Terrain(), campaign: Campaign);
        var rules = Content.RuntimeRules;
        var (alienRule, alienArmor) = battle.UnitRule(rules, "ALIEN");
        // BattleUnit's constructors with the starting condition (prepareBannedFlag): createTempUnit's and the soldier's.
        Assert.True(battle.TempUnit(rules, 1000005, BattleFaction.Hostile, BattleController.Ai, "ALIEN", alienRule, alienArmor).BannedInNextStage);
        var (id, campaign) = SoldierCampaign(static personal => personal with { Armor = "PAINTED_ARMOR" });
        var soldier = campaign.Soldiers(id)!;
        var painted = rules.Armors[rules.Armors.GetRequired("PAINTED_ARMOR")].Value;
        Assert.True(battle.SoldierUnit(rules, id, soldier, "PAINTED_ARMOR", painted, "").BannedInNextStage);
        Assert.False(battle.SoldierUnit(rules, id, soldier, "PAINTED_ARMOR", painted, "", startingCondition: false).BannedInNextStage);
        Assert.False(battle.TempUnit(rules, 1000006, BattleFaction.Neutral, BattleController.Ai, "PAINTED", rules.Battle.Equipment.Units["PAINTED"],
            rules.Armors[rules.Armors.GetRequired("ARMOR")].Value).BannedInNextStage);
        // The load passes no starting condition: only the save's flag counts.
        var loaded = BattleState.Restore(OxceBattleSave.Read(Parse(Root + "startingConditionType: SC_BAN\nunits:\n  - " + Alien + "}\n  - " +
            Alien.Replace("1000000", "1000001").Replace("[1, 1, 0]", "[2, 1, 0]") + ", bannedInNextStage: true}\n"), Content), Content, Terrain(),
            campaign: Campaign);
        Assert.Equal([false, true], loaded.Units.Select(static unit => unit.BannedInNextStage));
        Assert.Equal("true", Text(WrittenUnit(loaded, 1), "bannedInNextStage"));
    }

    [Fact]
    public void AUnitRulesDefaultWeaponPickUpIsTheModsAiSettingForItsFaction()
    {
        // pickUpWeaponsMoreActively -1: the ai section's pickUpWeaponsMoreActively (on here) for a hostile unit and
        // pickUpWeaponsMoreActivelyCiv (off) for the others.
        var battle = Restore("units:\n  - " + Alien + "}\n  - " + Alien.Replace("1000000", "1000001").Replace("[1, 1, 0]", "[2, 1, 0]")
            .Replace("faction: 1", "faction: 2") + "}\n");
        Assert.Equal([true, false], battle.Units.Select(static unit => unit.PickUpWeaponsMoreActively));
    }

    [Fact]
    public void ADeploymentThatDoesNotMarkCiviliansKeepsTheUnitRulesVip()
    {
        var rules = BattleStartTests.Rules.Replace("markCiviliansAsVIP: true", "markCiviliansAsVIP: false", StringComparison.Ordinal)
            .Replace("  - type: CIVILIAN\n    armor: SMALL_VEHICLE_ARMOR\n", "  - type: CIVILIAN\n    armor: SMALL_VEHICLE_ARMOR\n    vip: true\n",
                StringComparison.Ordinal);
        var (campaign, _) = BattleStartTests.Requested(rules, "UFO_CIVILIANS");
        BattleStartTests.Started(campaign);
        var civilians = BattleStartTests.Battle(campaign).Units.Where(static unit => unit.Faction == BattleFaction.Neutral).ToArray();
        Assert.NotEmpty(civilians);
        // markAsVIP only adds to the rule's vip flag (BattleUnit(Unit*) sets it from the rule).
        Assert.All(civilians, static civilian => Assert.True(civilian.IsVip));
    }

    [Fact]
    public void ALargeUnitWhoseFootprintLeavesTheMapIsACorruptedSave()
    {
        Assert.Contains("outside the map", Failure(() => Restore("units:\n  - {id: 1000000, genUnitType: VEHICLE, genUnitArmor: VEHICLE_ARMOR, faction: 0," +
            " position: [3, 3, 0]}\n")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("oxcePortBattle: 3\n", "must be a mapping")]
    [InlineData("oxcePortBattle: {units: [3]}\n", "units must be mappings")]
    [InlineData("oxcePortBattle: {units: [{controller: ai}]}\n", "'id' is missing")]
    [InlineData("oxcePortBattle: {units: [{id: 1000000, controller: player, playerSlot: -1}]}\n", "negative 'playerSlot'")]
    [InlineData("oxcePortBattle: {units: [{id: 1000000}, {id: 1000000}]}\n", "twice")]
    public void MalformedPortNodesFail(string port, string message)
    {
        Assert.Contains(message, Failure(() => Read("units:\n  - " + Alien + "}\n" + port)), StringComparison.Ordinal);
    }
}
