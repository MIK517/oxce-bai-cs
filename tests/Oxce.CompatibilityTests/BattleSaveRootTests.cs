using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 2 S1: the battle save module reads and writes the battle root of <c>battleGame</c>
/// (<c>SavedBattleGame::load</c> and <c>save</c>, <c>ItemContainer</c>, <c>ScriptValues</c>), the battle captures and
/// restores it through <see cref="BattleSnapshot"/>, and generation sets the root fields the reference's generator sets
/// (the terrain's ambience).
/// </summary>
public sealed class BattleSaveRootTests
{
    private static readonly string Rules = BattleStartTests.Rules + """

        extended:
          tags:
            BattleGame: {BATTLE_TAG: int, OTHER_TAG: int, LIST_TAG: RuleList}
        """;

    private static readonly Lazy<RuntimeContent> SharedContent = new(() => Generated().Campaign.Content);

    private static (Oxce.Gameplay.Campaigns.CampaignState Campaign, BattleState Battle) Generated(string? rules = null)
    {
        var (campaign, _) = BattleStartTests.Requested(rules ?? Rules);
        BattleStartTests.Started(campaign);
        return (campaign, BattleStartTests.Battle(campaign));
    }

    private static YamlMappingNode Parse(string yaml) =>
        (YamlMappingNode)YamlCompatibilityReader.Parse(yaml, "battle.yaml").Documents[0].Root;

    private static string Emit(YamlMappingNode node) => YamlCompatibilityWriter.Emit(new YamlDocumentSet("battle.yaml",
        [new YamlDocument(node, node.Span)]));

    private static string Normalized(YamlMappingNode node) =>
        YamlSemanticNormalizer.NormalizeToJson(new YamlDocumentSet("battle.yaml", [new YamlDocument(node, node.Span)]));

    /// <summary>A minimal root: the one key the reference requires.</summary>
    private const string Minimal = "selectedUnit: -1\n";

    [Fact]
    public void GeneratedBattleRootWritesReloadsAndConforms()
    {
        var (_, battle) = Generated();
        var snapshot = battle.Capture();
        // run and setupObjectives: the deployment's limits and shade, no briefing yet, turn 0 before the first turn.
        Assert.Equal((20, 20, 2, "STR_UFO_GROUND_ASSAULT", 15, 2, 12, 9, 0, "", ""),
            (snapshot.Width, snapshot.Length, snapshot.Height, snapshot.MissionType, snapshot.TurnLimit, snapshot.ChronoTrigger,
                snapshot.CheatTurn, snapshot.GlobalShade, snapshot.Turn, snapshot.TargetName, snapshot.CraftOrBaseName));
        // The SavedBattleGame constructor's values for what generation leaves alone.
        Assert.Equal((false, 0, -1, 0, false, true, false, 0, -1, 0), (snapshot.NameDisplay, snapshot.AnimFrame, snapshot.UndoUnitId,
            snapshot.TimeUnitsReserved, snapshot.KneelReserved, snapshot.TogglePersonalLight, snapshot.ToggleNightVision,
            snapshot.ToggleBrightness, snapshot.ObjectiveType, snapshot.ObjectivesNeeded));
        Assert.Empty(snapshot.ReinforcementsMemory);
        Assert.Empty(snapshot.ScriptValues);

        var written = OxceBattleSave.Write(snapshot);
        BattleSaveConformance.Check(written);
        var text = Emit(written);
        var reread = OxceBattleSave.Read(Parse(text), SharedContent.Value);
        Assert.Equivalent(snapshot, reread, strict: true);
        var restored = BattleState.Restore(reread, SharedContent.Value);
        Assert.Equivalent(snapshot, restored.Capture(), strict: true);
        // The derived state: the deployment, from the mission type's deployment or the UFO in battle.
        Assert.Equal(battle.DeploymentId, BattleState.Restore(reread, SharedContent.Value, "UFO_SCOUT").DeploymentId);
        // A second write of the reloaded battle is byte-stable.
        Assert.Equal(text, Emit(OxceBattleSave.Write(restored.Capture(), Parse(text))));
    }

    [Fact]
    public void GenerationCopiesTheTerrainsLoopingAmbience()
    {
        var rules = Rules.Replace("  - name: TEST_TERRAIN\n    mapDataSets", "  - name: TEST_TERRAIN\n    ambience: 7\n    ambientVolume: 0.3\n    mapDataSets",
            StringComparison.Ordinal);
        var (_, battle) = Generated(rules);
        // generateMap: the looping sound and the volume; the random ambience stays empty and its delay at 0.
        Assert.Equal((7, 0.3, 0, 20, 60, 0), (battle.Ambience, battle.AmbientVolume, battle.AmbienceRandom.Count,
            battle.MinimumAmbienceRandomDelay, battle.MaximumAmbienceRandomDelay, battle.CurrentAmbienceDelay));
        Assert.Equal("0.3", ((YamlScalarNode)Value(OxceBattleSave.Write(battle.Capture()), "ambientVolume")).Value);
    }

    [Fact]
    public void GenerationCopiesTheTerrainsRandomAmbienceWithoutDrawingFromTheGame()
    {
        var rules = Rules.Replace("  - name: TEST_TERRAIN\n    mapDataSets",
            "  - name: TEST_TERRAIN\n    ambienceRandom: [3, 4]\n    ambienceRandomDelay: [2, 5]\n    mapDataSets", StringComparison.Ordinal);
        var (campaign, battle) = Generated(rules);
        Assert.Equal((-1, 0.5, 2, 5), (battle.Ambience, battle.AmbientVolume, battle.MinimumAmbienceRandomDelay, battle.MaximumAmbienceRandomDelay));
        Assert.Equal([3, 4], battle.AmbienceRandom);
        // resetCurrentAmbienceDelay: RNG::seedless(min * 10, max * 10).
        Assert.InRange(battle.CurrentAmbienceDelay, 20, 50);
        // The delay draws from the seedless stream: the game's stream is where it is without the random ambience.
        var (control, _) = Generated();
        Assert.Equal(control.Random.State, campaign.Random.State);
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(5, 3, 50)]
    public void TheAmbienceDelayIsAtLeastOneSecondAndTakesTheMinimumOfAReversedRange(int minimum, int maximum, int expected)
    {
        var battle = BattleState.Restore(OxceBattleSave.Read(Parse(Minimal + "width: 1\nlength: 1\nheight: 1\n"), SharedContent.Value),
            SharedContent.Value);
        battle.SetAmbience(new Oxce.Mods.Rulesets.Runtime.RuntimeTerrain("T", [], [], "", [], 0, 0, "", [])
        {
            MinimumAmbienceDelay = minimum,
            MaximumAmbienceDelay = maximum,
        }, new SplitMix64RandomSource(1));
        Assert.Equal(expected, battle.CurrentAmbienceDelay);
    }

    [Fact]
    public void AnAbsentRootKeepsTheConstructorsValues()
    {
        var snapshot = OxceBattleSave.Read(Parse(Minimal), SharedContent.Value);
        Assert.Equivalent(new BattleSnapshot(0, 0, 0, ""), snapshot, strict: true);
    }

    [Fact]
    public void VipAndObjectiveBlocksAreWrittenOnlyWhenTheBattleHasThem()
    {
        const string blocks = "vipEscapeType: 2\nvipSurvivalPercentage: 60\nvipsSaved: 1\nvipsLost: 2\nvipsWaitingOutside: 3\n" +
            "vipsSavedScore: 4\nvipsLostScore: 5\nvipsWaitingOutsideScore: 6\nobjectivesDestroyed: 1\nobjectivesNeeded: 3\nobjectiveType: 2\n";
        var source = Parse(blocks + Minimal);
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value);
        Assert.Equal((2, 60, 1, 2, 3, 4, 5, 6, 1, 3, 2), (snapshot.VipEscapeType, snapshot.VipSurvivalPercentage, snapshot.VipsSaved,
            snapshot.VipsLost, snapshot.VipsWaitingOutside, snapshot.VipsSavedScore, snapshot.VipsLostScore, snapshot.VipsWaitingOutsideScore,
            snapshot.ObjectivesDestroyed, snapshot.ObjectivesNeeded, snapshot.ObjectiveType));
        var written = OxceBattleSave.Write(snapshot, source);
        Assert.Equal(Normalized(source), Normalized(Keep(written, source)));
        // Without a survival percentage or objectives the blocks go, also from a loaded source that had them.
        var cleared = OxceBattleSave.Write(snapshot with { VipSurvivalPercentage = 0, ObjectivesNeeded = 0 }, source);
        foreach (var key in new[] { "vipEscapeType", "vipSurvivalPercentage", "vipsSaved", "vipsLost", "vipsWaitingOutside", "vipsSavedScore",
            "vipsLostScore", "vipsWaitingOutsideScore", "objectivesDestroyed", "objectivesNeeded", "objectiveType" })
            Assert.False(cleared.TryGet(key, out _), key);
        // A new battle without them writes neither block.
        var fresh = OxceBattleSave.Write(new BattleSnapshot(1, 1, 1, "M"));
        Assert.False(fresh.TryGet("vipSurvivalPercentage", out _));
        Assert.False(fresh.TryGet("objectivesNeeded", out _));
        BattleSaveConformance.Check(fresh);
    }

    [Theory]
    [InlineData("startingConditionType", "SC_GEAR", "SC_GEAR")]
    [InlineData("startingConditionType", "SC_UNKNOWN", "")]
    [InlineData("enviroEffectsType", "ENV_SUIT", "ENV_SUIT")]
    [InlineData("enviroEffectsType", "ENV_UNKNOWN", "")]
    public void StartingConditionAndEnviroEffectsAreKeptWhenKnownAndDroppedWhenUnknown(string key, string value, string expected)
    {
        var diagnostics = new DiagnosticCollector(10);
        var source = Parse($"{key}: {value}\n" + Minimal + "width: 1\nlength: 1\nheight: 1\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value, diagnostics);
        var read = key == "startingConditionType" ? snapshot.StartingConditionId : snapshot.EnviroEffectsId;
        Assert.Equal(expected, read);
        // getStartingCondition and getEnviroEffects give null for an unknown type, silently: the battle has none. The port warns.
        Assert.Equal(expected.Length == 0 ? 1 : 0, diagnostics.Snapshot().Count(static item => item.Code == OxceBattleSave.SkippedCode));
        var written = OxceBattleSave.Write(snapshot, source);
        Assert.Equal(expected.Length != 0, written.TryGet(key, out _));
        var battle = BattleState.Restore(snapshot, SharedContent.Value);
        // The enviro effects give the armor transformations of the units the battle creates later.
        if (key == "enviroEffectsType")
            Assert.Equal(expected.Length == 0 ? null : "SUIT", battle.ArmorTransformations?.GetValueOrDefault("ARMOR"));
    }

    [Fact]
    public void AbsentStartingConditionAndEnviroEffectsAreNotWritten()
    {
        var written = OxceBattleSave.Write(OxceBattleSave.Read(Parse(Minimal), SharedContent.Value));
        Assert.False(written.TryGet("startingConditionType", out _));
        Assert.False(written.TryGet("enviroEffectsType", out _));
    }

    [Fact]
    public void UnknownRootKeysSurviveALoadedRewrite()
    {
        var source = Parse("width: 1\nportFuture: {a: [1, 2]}\n" + Minimal + "turn: 3\nmapdatasets: [BLANKS]\n");
        var written = OxceBattleSave.Write(OxceBattleSave.Read(source, SharedContent.Value) with { Turn = 4 }, source);
        Assert.Equal(Normalized(Parse("portFuture: {a: [1, 2]}\n")), Normalized(Keep(written, Parse("portFuture: {a: [1, 2]}\n"))));
        // Keys of later slices stay as loaded; the root keys take the battle's values, in the source's order.
        Assert.Equal(Normalized(Parse("mapdatasets: [BLANKS]\n")), Normalized(Keep(written, Parse("mapdatasets: [BLANKS]\n"))));
        Assert.Equal("4", ((YamlScalarNode)Value(written, "turn")).Value);
        Assert.Equal(["width", "portFuture", "selectedUnit", "turn", "mapdatasets"], written.Entries.Take(5).Select(static entry => entry.ScalarKey));
    }

    [Fact]
    public void TheSelectedUnitIsRequired()
    {
        Assert.Contains("'selectedUnit'", Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(Parse("turn: 1\n"), SharedContent.Value)).Message,
            StringComparison.Ordinal);
        Assert.Contains("'selectedUnit'", Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(Parse("selectedUnit: x\n"), SharedContent.Value))
            .Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("turn: abc")]
    [InlineData("kneelReserved: maybe")]
    [InlineData("missionType: [A]")]
    [InlineData("ambientVolume: loud")]
    [InlineData("reinforcementsMemory: {WAVE: x}")]
    public void AValueOfTheWrongTypeFailsNamingItsKey(string line)
    {
        var key = line[..line.IndexOf(':', StringComparison.Ordinal)];
        var failure = Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(Parse(Minimal + line + "\n"), SharedContent.Value));
        Assert.Contains($"'{key}'", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("width: wide")]
    [InlineData("height: [1]")]
    [InlineData("undoUnit: none")]
    public void DefaultedValuesFailWhenPresentAndUnreadable(string line)
    {
        // readVal(default) falls back only for a missing node; tryReadVal throws a type error for an unreadable one.
        var key = line[..line.IndexOf(':', StringComparison.Ordinal)];
        var failure = Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(Parse(Minimal + line + "\n"), SharedContent.Value));
        Assert.Contains($"'{key}'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCraftLineKeepsItsControlCharacter()
    {
        var source = Parse(Minimal + "strCraftOrBase: \"CRAFT> \\x01SKYRANGER-1\"\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value);
        Assert.Equal("CRAFT> \u0001SKYRANGER-1", snapshot.CraftOrBaseName);
        var text = Emit(OxceBattleSave.Write(snapshot, source));
        Assert.Contains("strCraftOrBase: \"CRAFT> \\x01SKYRANGER-1\"", text, StringComparison.Ordinal);
        Assert.Equal(snapshot.CraftOrBaseName, OxceBattleSave.Read(Parse(text), SharedContent.Value).CraftOrBaseName);
    }

    [Fact]
    public void BaseItemsKeepKnownItemsInTypeOrder()
    {
        var diagnostics = new DiagnosticCollector(10);
        var source = Parse(Minimal + "baseItems: {GUN: 2, NOT_AN_ITEM: 4, CLIP: 3}\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value, diagnostics);
        // ItemContainer::load skips an unknown item (with an error log); save sorts by type.
        Assert.Equal([("CLIP", 3), ("GUN", 2)], snapshot.BaseItems.Select(static pair => (pair.Key, pair.Value)));
        Assert.Single(diagnostics.Snapshot(), static item => item.Code == OxceBattleSave.SkippedCode);
        Assert.Equal(["CLIP", "GUN"], ((YamlMappingNode)Value(OxceBattleSave.Write(snapshot, source), "baseItems")).Entries.Select(static entry => entry.ScalarKey));
        // A base items node that is not a mapping is no items.
        Assert.Empty(OxceBattleSave.Read(Parse(Minimal + "baseItems: [GUN]\n"), SharedContent.Value).BaseItems);
        Assert.Equal(snapshot.BaseItems, BattleState.Restore(snapshot with { Width = 1, Length = 1, Height = 1 }, SharedContent.Value).BaseStorageItems);
    }

    [Fact]
    public void RootTagsKeepKnownTagsAndWriteNonZeroValues()
    {
        var diagnostics = new DiagnosticCollector(10);
        var source = Parse(Minimal + "width: 1\nlength: 1\nheight: 1\ntags: {OTHER_TAG: 0, BATTLE_TAG: 5, GONE_TAG: 1}\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value, diagnostics);
        // loadBase: an unknown tag is logged and skipped.
        Assert.Equal([("OTHER_TAG", 0), ("BATTLE_TAG", 5)], snapshot.ScriptValues.Select(static value => (value.TagName, value.Value)));
        Assert.Single(diagnostics.Snapshot(), static item => item.Code == OxceBattleSave.SkippedCode);
        // saveBase: only non-zero values, in tag order.
        var battle = BattleState.Restore(snapshot, SharedContent.Value);
        Assert.Equal([("BATTLE_TAG", 5)], battle.Capture().ScriptValues.Select(static value => (value.TagName, value.Value)));
        var tags = (YamlMappingNode)Value(OxceBattleSave.Write(battle.Capture(), source), "tags");
        Assert.Equal(["BATTLE_TAG"], tags.Entries.Select(static entry => entry.ScalarKey));
        // No non-zero value, no tags node.
        Assert.False(OxceBattleSave.Write(snapshot with { ScriptValues = [] }, source).TryGet("tags", out _));
    }

    [Theory]
    // DebriefingState: the custom mission's deployment, else the mission type's, else the UFO's own.
    [InlineData("UFO_ALIENS", "UFO_POWER", "UFO_SCOUT", "UFO_POWER")]
    [InlineData("UFO_ALIENS", "", "UFO_SCOUT", "UFO_ALIENS")]
    [InlineData("STR_UFO_GROUND_ASSAULT", "", "UFO_SCOUT", "UFO_SCOUT")]
    [InlineData("STR_UFO_GROUND_ASSAULT", "NO_SUCH", "UFO_SCOUT", "UFO_SCOUT")]
    [InlineData("STR_UFO_GROUND_ASSAULT", "", "NO_SUCH", "")]
    [InlineData("STR_UFO_GROUND_ASSAULT", "", "", "")]
    public void TheDeploymentIsDerivedAsTheReferencesReadersDeriveIt(string missionType, string customMission, string ufo, string expected)
    {
        var snapshot = new BattleSnapshot(1, 1, 1, missionType) { AlienCustomMission = customMission };
        Assert.Equal(expected, BattleState.Restore(snapshot, SharedContent.Value, ufo).DeploymentId);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, BattleState.MaximumMapHeight + 1)]
    public void AMapSizeOutsideTheSupportedRangeIsACorruptedSave(int width, int length, int height)
    {
        var failure = Assert.Throws<InvalidDataException>(() => BattleState.Restore(new BattleSnapshot(width, length, height, ""), SharedContent.Value));
        Assert.Contains("'width'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARestoredBattleHasAnEmptyMapOfTheSavedSizeAndTheFirstItemId()
    {
        var battle = BattleState.Restore(new BattleSnapshot(3, 2, 1, "M") { Turn = 2, SelectedUnitId = 5 }, SharedContent.Value);
        Assert.Equal((3, 2, 1, 6, 2, 5, 1), (battle.Width, battle.Length, battle.Height, battle.Map.TileCount, battle.Turn, battle.SelectedUnitId,
            battle.NextItemId));
        Assert.Empty(battle.Units);
        battle.Validate();
    }

    [Fact]
    public void BaseItemsTakeTheLastOfARepeatedItemAndRefuseAKeyThatIsNoScalar()
    {
        // ItemContainer::load assigns _qty[type] per child: a repeated item ends with its last quantity.
        Assert.Equal(7, OxceBattleSave.Read(Parse(Minimal + "baseItems: {GUN: 2, GUN: 7}\n"), SharedContent.Value).BaseItems["GUN"]);
        var span = Parse(Minimal).Span;
        var items = new YamlMappingNode(span, [new YamlMappingEntry(new YamlSequenceNode(span, [new YamlScalarNode(span, "GUN", YamlScalarStyle.Plain)]),
            new YamlScalarNode(span, "1", YamlScalarStyle.Plain))], null, null);
        var battle = new YamlMappingNode(span, [.. Parse(Minimal).Entries, new YamlMappingEntry(new YamlScalarNode(span, "baseItems", YamlScalarStyle.Plain), items)],
            null, null);
        Assert.Contains("'baseItems'", Assert.Throws<InvalidDataException>(() => OxceBattleSave.Read(battle, SharedContent.Value)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TagsTakeTheLastOfARepeatedTagAndIgnoreANodeThatIsNoMapping()
    {
        // loadBase calls setBase per child: a repeated tag ends with its last value.
        Assert.Equal([("BATTLE_TAG", 9)], OxceBattleSave.Read(Parse(Minimal + "tags: {BATTLE_TAG: 5, BATTLE_TAG: 9}\n"), SharedContent.Value)
            .ScriptValues.Select(static value => (value.TagName, value.Value)));
        // loadBase reads tags only from a mapping.
        Assert.Empty(OxceBattleSave.Read(Parse(Minimal + "tags: [BATTLE_TAG]\n"), SharedContent.Value).ScriptValues);
    }

    [Fact]
    public void RuleListTagsAreRefusedUntilTheTacticalScriptBindings()
    {
        // ModScriptGlobal::loadRuleList reads a mod name; battle saves read and write int tags only (branch 8 owns the rest).
        var expected = OxceBattleSave.UnsupportedTagType("LIST_TAG", "RuleList");
        Assert.Equal(expected, Assert.Throws<InvalidDataException>(() =>
            OxceBattleSave.Read(Parse(Minimal + "tags: {LIST_TAG: master}\n"), SharedContent.Value)).Message);
        Assert.Equal(expected, Assert.Throws<InvalidDataException>(() =>
            OxceBattleSave.Write(new BattleSnapshot(1, 1, 1, "M") { ScriptValues = [new("LIST_TAG", "RuleList", 1)] })).Message);
    }

    [Theory]
    [InlineData("StartingConditionId", "starting condition")]
    [InlineData("EnviroEffectsId", "enviro effects")]
    [InlineData("ScriptValues", "GONE_TAG")]
    public void ARestoreRefusesRulesTheContentLacks(string field, string message)
    {
        var snapshot = new BattleSnapshot(1, 1, 1, "M");
        snapshot = field switch
        {
            "StartingConditionId" => snapshot with { StartingConditionId = "SC_GONE" },
            "EnviroEffectsId" => snapshot with { EnviroEffectsId = "ENV_GONE" },
            _ => snapshot with { ScriptValues = [new("GONE_TAG", "int", 1)] },
        };
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => BattleState.Restore(snapshot, SharedContent.Value)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnAmbienceDelayWhoseTenthsOverflowTakesTheClampedMinimum()
    {
        var battle = BattleState.Restore(new BattleSnapshot(1, 1, 1, "M"), SharedContent.Value);
        battle.SetAmbience(new Oxce.Mods.Rulesets.Runtime.RuntimeTerrain("T", [], [], "", [], 0, 0, "", [])
        {
            MinimumAmbienceDelay = int.MaxValue / 5,
            MaximumAmbienceDelay = int.MaxValue,
        }, new SplitMix64RandomSource(1));
        Assert.Equal(int.MaxValue, battle.CurrentAmbienceDelay);
    }

    private static YamlNode Value(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? node! : throw new KeyNotFoundException(key);

    /// <summary>The entries of <paramref name="written"/> whose keys <paramref name="keys"/> has, in its order.</summary>
    private static YamlMappingNode Keep(YamlMappingNode written, YamlMappingNode keys) => new(written.Span,
        keys.Entries.Select(entry => written.Entries.Single(candidate => candidate.ScalarKey == entry.ScalarKey)), null, null);
}
