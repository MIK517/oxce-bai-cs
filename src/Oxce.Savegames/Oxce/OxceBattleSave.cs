using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Mods.Rulesets.Content;
using Oxce.Scripting.Globals;
using static Oxce.Savegames.Oxce.OxceSaveAdapter;

namespace Oxce.Savegames.Oxce;

/// <summary>
/// The battle save module (ADR 0038, D6): reads the save's <c>battleGame</c> node into a <see cref="BattleSnapshot"/>
/// and writes a snapshot back, overlaying the loaded node so keys no reader reads survive (ADR 0018). Reference:
/// <c>SavedBattleGame::load</c> and <c>save</c>, <c>ItemContainer::load</c> and <c>save</c> (<c>baseItems</c>) and
/// <c>ScriptValuesBase::loadBase</c> and <c>saveBase</c> (<c>tags</c>) at OXCE commit 4df3a5e.
/// </summary>
/// <remarks>
/// Phase 7 branch 2 S1 reads and writes the battle root and S2 the map (<c>mapdatasets</c>, the binary tiles with their
/// serialization key or the old text <c>tiles</c>, <c>nodes</c>, <c>moduleMap</c>, the flattened names and reinforcement
/// blocks; <c>Tile::load</c>, <c>loadBinary</c> and <c>saveBinary</c>, <c>Node::load</c> and <c>save</c>), S3 the units
/// and S4 the items (<c>items</c>, <c>itemsSpecial</c>, <c>recoverGuaranteed</c>, <c>recoverConditional</c>).
/// </remarks>
public static partial class OxceBattleSave
{
    /// <summary>The code of the warnings for what the reference's load skips (it logs an error and goes on).</summary>
    public const string SkippedCode = "OXCE-BATTLE-SAVE-SKIPPED";

    /// <summary>
    /// SavedBattleGame::load for the battle root. A value the reference cannot read (a present value of the wrong type,
    /// or a missing <c>selectedUnit</c>) is an <see cref="InvalidDataException"/> naming the key. What the reference drops
    /// (an unknown starting condition or enviro effects, silently; an unknown base item or tag, with an error log) is
    /// left out, and the port reports each as a warning to <paramref name="diagnostics"/>.
    /// </summary>
    public static BattleSnapshot Read(YamlMappingNode battle, RuntimeContent content, IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(content);
        var rules = content.RuntimeRules;
        // readVal(_mapsize_x): an absent size keeps the constructor's 0; an unreadable one throws (tryReadVal).
        var snapshot = new BattleSnapshot(Int(battle, "width", 0), Int(battle, "length", 0), Int(battle, "height", 0),
            String(battle, "missionType", ""));
        // getStartingCondition and getEnviroEffects return null for an unknown type, which is then none.
        var startingCondition = KnownOrNone(battle, "startingConditionType", id => rules.StartingConditions.TryGet(id, out _),
            "starting condition", diagnostics);
        var enviroEffects = KnownOrNone(battle, "enviroEffectsType", rules.Battle.EnviroGlobalChances.ContainsKey, "enviro effects",
            diagnostics);
        return snapshot with
        {
            TargetName = String(battle, "strTarget", ""),
            CraftOrBaseName = String(battle, "strCraftOrBase", ""),
            StartingConditionId = startingCondition,
            EnviroEffectsId = enviroEffects,
            NameDisplay = Bool(battle, "nameDisplay", false),
            EnvironmentalConditionFriendly = Bool(battle, "ecEnabledFriendly", false),
            EnvironmentalConditionHostile = Bool(battle, "ecEnabledHostile", false),
            EnvironmentalConditionNeutral = Bool(battle, "ecEnabledNeutral", false),
            AlienCustomDeploy = String(battle, "alienCustomDeploy", ""),
            AlienCustomMission = String(battle, "alienCustomMission", ""),
            AlienItemLevel = Int(battle, "alienItemLevel", 0),
            MapScriptId = String(battle, "lastUsedMapScript", ""),
            ReinforcementsDeployment = String(battle, "reinforcementsDeployment", ""),
            ReinforcementsRace = String(battle, "reinforcementsRace", ""),
            ReinforcementsItemLevel = Int(battle, "reinforcementsItemLevel", 0),
            ReinforcementsMemory = Counts(battle, "reinforcementsMemory"),
            GlobalShade = Int(battle, "globalshade", 0),
            Turn = Int(battle, "turn", 0),
            BughuntMinTurn = Int(battle, "bughuntMinTurn", 20),
            BughuntMode = Bool(battle, "bughuntMode", false),
            Depth = Int(battle, "depth", 0),
            AnimFrame = Int(battle, "animFrame", 0),
            // readVal<int>() without a default: the reference throws on a missing or unreadable selected unit.
            SelectedUnitId = battle.TryGet("selectedUnit", out var selected) && YamlValueReader.TryReadInt32(selected!, out var selectedId)
                ? selectedId : throw new InvalidDataException("Battle save 'selectedUnit' is missing or not an integer."),
            UndoUnitId = Int(battle, "undoUnit", -1),
            VipEscapeType = Int(battle, "vipEscapeType", 0),
            VipSurvivalPercentage = Int(battle, "vipSurvivalPercentage", 0),
            VipsSaved = Int(battle, "vipsSaved", 0),
            VipsLost = Int(battle, "vipsLost", 0),
            VipsWaitingOutside = Int(battle, "vipsWaitingOutside", 0),
            VipsSavedScore = Int(battle, "vipsSavedScore", 0),
            VipsLostScore = Int(battle, "vipsLostScore", 0),
            VipsWaitingOutsideScore = Int(battle, "vipsWaitingOutsideScore", 0),
            ObjectiveType = Int(battle, "objectiveType", -1),
            ObjectivesDestroyed = Int(battle, "objectivesDestroyed", 0),
            ObjectivesNeeded = Int(battle, "objectivesNeeded", 0),
            TimeUnitsReserved = Int(battle, "tuReserved", 0),
            KneelReserved = Bool(battle, "kneelReserved", false),
            Ambience = Int(battle, "ambience", -1),
            AmbientVolume = battle.TryGet("ambientVolume", out var volume) ? Value(volume!, "ambientVolume", YamlValueReader.ReadDouble) : 0.5,
            AmbienceRandom = battle.TryGet("ambienceRandom", out var random)
                ? Array.AsReadOnly(Value(random!, "ambienceRandom", node => YamlValueReader.ReadSequence(node, YamlValueReader.ReadInt32)))
                : [],
            MinimumAmbienceRandomDelay = Int(battle, "minAmbienceRandomDelay", 20),
            MaximumAmbienceRandomDelay = Int(battle, "maxAmbienceRandomDelay", 60),
            CurrentAmbienceDelay = Int(battle, "currentAmbienceDelay", 0),
            Music = String(battle, "music", ""),
            BaseItems = BaseItems(battle, content, diagnostics),
            TurnLimit = Int(battle, "turnLimit", 0),
            ChronoTrigger = Int(battle, "chronoTrigger", 0),
            CheatTurn = Int(battle, "cheatTurn", 20),
            TogglePersonalLight = Bool(battle, "togglePersonalLight", true),
            ToggleNightVision = Bool(battle, "toggleNightVision", false),
            ToggleBrightness = Int(battle, "toggleBrightness", 0),
            ScriptValues = Tags(battle, content, diagnostics, BattleSnapshot.ScriptOwner, "Battle save tag"),
            ReinforcementsBlocks = Grid(battle, "reinforcementsBlocks", YamlValueReader.ReadInt32),
            FlattenedMapTerrainNames = Grid(battle, "flattenedMapTerrainNames", YamlValueReader.ReadString),
            FlattenedMapBlockNames = Grid(battle, "flattenedMapBlockNames", YamlValueReader.ReadString),
            MapDataSets = Array.AsReadOnly(Children(battle, "mapdatasets").Select(node => Value(node, "mapdatasets", YamlValueReader.ReadString))
                .ToArray()),
            // !reader["tileTotalBytesPer"]: the old text tiles; otherwise the binary tiles with their serialization key.
            Tiles = battle.TryGet("tileTotalBytesPer", out var totalBytes)
                ? BinaryTiles(battle, totalBytes!, snapshot.Width, snapshot.Length, snapshot.Height, diagnostics)
                : TextTiles(battle, snapshot.Width, snapshot.Length, snapshot.Height),
            Nodes = Array.AsReadOnly(Children(battle, "nodes").Select(ReadNode).ToArray()),
            // A base defense reads its module map (tryRead), or counts it without objects (calculateModuleMap).
            ModuleMap = snapshot.MissionType != BattleSnapshot.BaseDefenseMission ? []
                : battle.TryGet("moduleMap", out var modules)
                    ? Array.AsReadOnly(Value(modules!, "moduleMap", ReadModuleMap))
                    : BattleSnapshot.UncountedModuleMap(snapshot.Width, snapshot.Length),
            Units = Units(battle, content, diagnostics),
            Items = ReadItems(battle, "items", content, diagnostics),
            RecoverConditional = ReadItems(battle, "recoverConditional", content, diagnostics),
            RecoverGuaranteed = ReadItems(battle, "recoverGuaranteed", content, diagnostics),
            ItemsSpecial = ReadItems(battle, "itemsSpecial", content, diagnostics),
        };
    }

    /// <summary>
    /// SavedBattleGame::save for the battle root, over <paramref name="source"/> (the loaded <c>battleGame</c>, or null
    /// for a battle the port created). Keys are written as the reference writes them: the VIP block only with a VIP
    /// survival percentage, the objective block only with objectives needed, the starting condition and enviro effects
    /// only when set; a block or key the battle no longer has is removed from the source.
    /// </summary>
    public static YamlMappingNode Write(BattleSnapshot snapshot, YamlMappingNode? source = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var vip = snapshot.VipSurvivalPercentage > 0;
        // writer.write("items", _items, ...) and ("itemsSpecial", _items, ...): both keys whenever the battle has items, each
        // holding its share (possibly none); the recovery lists only when not empty.
        var anyItems = snapshot.Items.Count + snapshot.ItemsSpecial.Count != 0;
        var itemSources = ItemSources(source);
        var objectives = snapshot.ObjectivesNeeded != 0;
        return Overlay(source,
        [
            Pair("vipEscapeType", vip ? Integer(snapshot.VipEscapeType) : null),
            Pair("vipSurvivalPercentage", vip ? Integer(snapshot.VipSurvivalPercentage) : null),
            Pair("vipsSaved", vip ? Integer(snapshot.VipsSaved) : null),
            Pair("vipsLost", vip ? Integer(snapshot.VipsLost) : null),
            Pair("vipsWaitingOutside", vip ? Integer(snapshot.VipsWaitingOutside) : null),
            Pair("vipsSavedScore", vip ? Integer(snapshot.VipsSavedScore) : null),
            Pair("vipsLostScore", vip ? Integer(snapshot.VipsLostScore) : null),
            Pair("vipsWaitingOutsideScore", vip ? Integer(snapshot.VipsWaitingOutsideScore) : null),
            Pair("objectivesDestroyed", objectives ? Integer(snapshot.ObjectivesDestroyed) : null),
            Pair("objectivesNeeded", objectives ? Integer(snapshot.ObjectivesNeeded) : null),
            Pair("objectiveType", objectives ? Integer(snapshot.ObjectiveType) : null),
            Pair("width", Integer(snapshot.Width)),
            Pair("length", Integer(snapshot.Length)),
            Pair("height", Integer(snapshot.Height)),
            Pair("missionType", Scalar(snapshot.MissionType)),
            Pair("strTarget", Scalar(snapshot.TargetName)),
            // setAsQuotedAndEscaped: the craft line carries a control character ("CRAFT> \x01SKYRANGER-1").
            Pair("strCraftOrBase", new YamlScalarNode(GeneratedSpan, snapshot.CraftOrBaseName, YamlScalarStyle.DoubleQuoted)),
            Pair("startingConditionType", snapshot.StartingConditionId.Length != 0 ? Scalar(snapshot.StartingConditionId) : null),
            Pair("enviroEffectsType", snapshot.EnviroEffectsId.Length != 0 ? Scalar(snapshot.EnviroEffectsId) : null),
            Pair("nameDisplay", Boolean(snapshot.NameDisplay)),
            Pair("ecEnabledFriendly", Boolean(snapshot.EnvironmentalConditionFriendly)),
            Pair("ecEnabledHostile", Boolean(snapshot.EnvironmentalConditionHostile)),
            Pair("ecEnabledNeutral", Boolean(snapshot.EnvironmentalConditionNeutral)),
            Pair("alienCustomDeploy", Scalar(snapshot.AlienCustomDeploy)),
            Pair("alienCustomMission", Scalar(snapshot.AlienCustomMission)),
            Pair("alienItemLevel", Integer(snapshot.AlienItemLevel)),
            Pair("lastUsedMapScript", Scalar(snapshot.MapScriptId)),
            Pair("reinforcementsDeployment", Scalar(snapshot.ReinforcementsDeployment)),
            Pair("reinforcementsRace", Scalar(snapshot.ReinforcementsRace)),
            Pair("reinforcementsItemLevel", Integer(snapshot.ReinforcementsItemLevel)),
            Pair("reinforcementsMemory", IntMapping(snapshot.ReinforcementsMemory)),
            Pair("reinforcementsBlocks", Grid(snapshot.ReinforcementsBlocks, Integer)),
            Pair("flattenedMapTerrainNames", Grid(snapshot.FlattenedMapTerrainNames, Scalar)),
            Pair("flattenedMapBlockNames", Grid(snapshot.FlattenedMapBlockNames, Scalar)),
            Pair("globalshade", Integer(snapshot.GlobalShade)),
            Pair("turn", Integer(snapshot.Turn)),
            Pair("bughuntMinTurn", Integer(snapshot.BughuntMinTurn)),
            Pair("animFrame", Integer(snapshot.AnimFrame)),
            Pair("bughuntMode", Boolean(snapshot.BughuntMode)),
            Pair("selectedUnit", Integer(snapshot.SelectedUnitId)),
            Pair("undoUnit", Integer(snapshot.UndoUnitId)),
            Pair("mapdatasets", Sequence(snapshot.MapDataSets.Select(static name => (YamlNode)Scalar(name)))),
            // The field sizes of Tile::serializationKey, each a one-character string (static_cast<char>, quoted and escaped).
            Pair("tileIndexSize", Size(TileKey.Index)),
            Pair("tileTotalBytesPer", Integer(TileKey.TotalBytes)),
            Pair("tileFireSize", Size(TileKey.Fire)),
            Pair("tileSmokeSize", Size(TileKey.Smoke)),
            Pair("tileIDSize", Size(TileKey.MapDataId)),
            Pair("tileSetIDSize", Size(TileKey.MapDataSetId)),
            Pair("tileBoolFieldsSize", Size(TileKey.BoolFields)),
            Pair("lastExploredByPlayer", Size(TileKey.LastExploredByPlayer)),
            Pair("lastExploredByNeutral", Size(TileKey.LastExploredByNeutral)),
            Pair("lastExploredByHostile", Size(TileKey.LastExploredByHostile)),
            Pair("totalTiles", Integer(snapshot.Tiles.Count)),
            Pair("binTiles", Scalar(Convert.ToBase64String(BinaryTiles(snapshot.Tiles)))),
            // The old text tiles are read, never written (the reference's text writer is compiled out).
            Pair("tiles", null),
            Pair("nodes", Sequence(snapshot.Nodes.Select(static node => (YamlNode)WriteNode(node)))),
            Pair("moduleMap", snapshot.MissionType == BattleSnapshot.BaseDefenseMission
                ? Grid(snapshot.ModuleMap, static module => Sequence([Integer(module.Total), Integer(module.Remaining)])) : null),
            // writer.write("units", _units, ...): no key without units.
            Pair("units", WriteUnits(snapshot.Units, source)),
            Pair("items", anyItems ? WriteItems(snapshot.Items, itemSources) : null),
            Pair("itemsSpecial", anyItems ? WriteItems(snapshot.ItemsSpecial, itemSources) : null),
            Pair("tuReserved", Integer(snapshot.TimeUnitsReserved)),
            Pair("kneelReserved", Boolean(snapshot.KneelReserved)),
            Pair("depth", Integer(snapshot.Depth)),
            Pair("ambience", Integer(snapshot.Ambience)),
            Pair("ambientVolume", Real(snapshot.AmbientVolume)),
            Pair("ambienceRandom", Sequence(snapshot.AmbienceRandom.Select(static sound => (YamlNode)Integer(sound)))),
            Pair("minAmbienceRandomDelay", Integer(snapshot.MinimumAmbienceRandomDelay)),
            Pair("maxAmbienceRandomDelay", Integer(snapshot.MaximumAmbienceRandomDelay)),
            Pair("currentAmbienceDelay", Integer(snapshot.CurrentAmbienceDelay)),
            Pair("recoverGuaranteed", snapshot.RecoverGuaranteed.Count != 0 ? WriteItems(snapshot.RecoverGuaranteed, itemSources) : null),
            Pair("recoverConditional", snapshot.RecoverConditional.Count != 0 ? WriteItems(snapshot.RecoverConditional, itemSources) : null),
            Pair("music", Scalar(snapshot.Music)),
            // ItemContainer::save: sorted by type, an empty container as an empty map.
            Pair("baseItems", IntMapping(snapshot.BaseItems)),
            Pair("turnLimit", Integer(snapshot.TurnLimit)),
            Pair("chronoTrigger", Integer(snapshot.ChronoTrigger)),
            Pair("cheatTurn", Integer(snapshot.CheatTurn)),
            Pair("togglePersonalLight", Boolean(snapshot.TogglePersonalLight)),
            Pair("toggleNightVision", Boolean(snapshot.ToggleNightVision)),
            Pair("toggleBrightness", Integer(snapshot.ToggleBrightness)),
            // saveBase: only non-zero values, and no tags node without one.
            Pair("tags", ScriptValues(Tags(snapshot.ScriptValues))),
            Pair(PortKey, WritePort(snapshot.Units, source)),
        ]);
    }

    // tryRead and readVal(default): an absent key keeps the default; a present one of the wrong type throws in the
    // reference (YamlNodeReader::tryReadVal calls throwTypeError).
    private static int Int(YamlMappingNode map, string key, int fallback) =>
        map.TryGet(key, out var node) ? Value(node!, key, YamlValueReader.ReadInt32) : fallback;

    private static bool Bool(YamlMappingNode map, string key, bool fallback) =>
        map.TryGet(key, out var node) ? Value(node!, key, YamlValueReader.ReadBoolean) : fallback;

    private static string String(YamlMappingNode map, string key, string fallback) =>
        map.TryGet(key, out var node) ? Value(node!, key, YamlValueReader.ReadString) : fallback;

    private static T Value<T>(YamlNode node, string key, Func<YamlNode, T> read)
    {
        try
        {
            return read(node);
        }
        catch (Exception failure) when (failure is YamlFormatException or FormatException or OverflowException or InvalidDataException)
        {
            throw new InvalidDataException($"Battle save '{key}' cannot be read: {failure.Message}", failure);
        }
    }

    private static string KnownOrNone(YamlMappingNode map, string key, Func<string, bool> known, string kind, IDiagnosticSink? diagnostics)
    {
        if (!map.TryGet(key, out var node)) return "";
        var id = Value(node!, key, YamlValueReader.ReadString);
        if (known(id)) return id;
        Skipped(diagnostics, node!, $"Battle save names unknown {kind} '{id}', which the battle goes without.");
        return "";
    }

    private static ReadOnlyDictionary<string, int> Counts(YamlMappingNode map, string key) =>
        map.TryGet(key, out var node)
            ? new(Value(node!, key, value => YamlValueReader.ReadMap(value, YamlValueReader.ReadString, YamlValueReader.ReadInt32, StringComparer.Ordinal)))
            : new(new SortedDictionary<string, int>(StringComparer.Ordinal));

    /// <summary>ItemContainer::load: nothing unless a mapping; an unknown item is skipped with an error log.</summary>
    private static ReadOnlyDictionary<string, int> BaseItems(YamlMappingNode battle, RuntimeContent content, IDiagnosticSink? diagnostics)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (battle.TryGet("baseItems", out var node) && node is YamlMappingNode items)
            foreach (var entry in items.Entries)
            {
                var type = entry.ScalarKey ?? throw new InvalidDataException("Battle save 'baseItems' has a key that is not a scalar.");
                if (!content.RuntimeRules.Items.TryGet(type, out _))
                {
                    Skipped(diagnostics, entry.Value, $"Battle save base item '{type}' is not an item of the mods; it is left out.");
                    continue;
                }
                // std::map operator[]: a repeated item takes the last quantity.
                result[type] = Value(entry.Value, "baseItems", YamlValueReader.ReadInt32);
            }
        return new(result);
    }

    /// <summary>
    /// The value type battle saves read and write; the reference's other tag value type, <c>RuleList</c> (a mod name,
    /// <c>ModScriptGlobal::loadRuleList</c>), waits for the tactical script bindings (Phase 7 branch 8).
    /// </summary>
    private const string IntegerTag = "int";

    /// <summary>The named refusal for a battle tag of a value type battle saves do not read or write yet.</summary>
    public static string UnsupportedTagType(string name, string valueType) =>
        $"Battle tags of the {valueType} value type ('{name}') are read and written with the tactical script bindings (Phase 7 branch 8).";

    /// <summary>
    /// ScriptValuesBase::loadBase for an owner's <c>tags</c> (the root's, owner <c>BattleGame</c>, or a unit's, owner
    /// <c>BattleUnit</c>): an unknown tag is skipped with an error log.
    /// </summary>
    private static ReadOnlyCollection<ScriptValueEntry> Tags(YamlMappingNode battle, RuntimeContent content, IDiagnosticSink? diagnostics,
        string owner, string what)
    {
        if (!battle.TryGet("tags", out var node) || node is not YamlMappingNode tags) return Array.AsReadOnly(Array.Empty<ScriptValueEntry>());
        // The script API always declares BattleGame.Tag and BattleUnit.Tag, so the owner type exists in any content.
        var type = content.Tags.Types.First(candidate => candidate.Name == owner);
        var values = new List<ScriptValueEntry>();
        foreach (var entry in tags.Entries)
        {
            var name = entry.ScalarKey ?? throw new InvalidDataException($"{what} names must be scalars.");
            if (!content.Tags.TryGetTag(type.Id, "Tag." + name, out var tag))
            {
                Skipped(diagnostics, entry.Value, $"{what} '{name}' is not a {owner} tag of the mods; it is left out.");
                continue;
            }
            if (tag!.ValueType != IntegerTag) throw new InvalidDataException(UnsupportedTagType(name, tag.ValueType));
            var value = Value(entry.Value, "tags", YamlValueReader.ReadInt32);
            // setBase per child: a repeated tag takes its last value.
            values.RemoveAll(existing => existing.TagName == name);
            values.Add(new ScriptValueEntry(name, tag.ValueType, value));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static ScriptValueEntry[] Tags(IReadOnlyList<ScriptValueEntry> values)
    {
        var written = values.Where(static value => value.Value != 0).ToArray();
        foreach (var value in written)
            if (value.ValueType != IntegerTag) throw new InvalidDataException(UnsupportedTagType(value.TagName, value.ValueType));
        return written;
    }

    private static void Skipped(IDiagnosticSink? diagnostics, YamlNode node, string message) =>
        diagnostics?.Report(new DiagnosticEvent(SkippedCode, DiagnosticSeverity.Warning, message, node.Span));

    /// <summary>YamlNodeReader::children: a sequence's items or a mapping's values; nothing for a scalar or a missing key.</summary>
    private static IReadOnlyList<YamlNode> Children(YamlNode? node) => node switch
    {
        YamlSequenceNode sequence => sequence.Items,
        YamlMappingNode mapping => [.. mapping.Entries.Select(static entry => entry.Value)],
        _ => [],
    };

    private static IReadOnlyList<YamlNode> Children(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? Children(node) : [];

    /// <summary>
    /// tryRead into std::vector&lt;std::vector&lt;T&gt;&gt;: a vector takes a node's children (none for a scalar), and an element
    /// the type cannot hold fails.
    /// </summary>
    private static ReadOnlyCollection<IReadOnlyList<T>> Grid<T>(YamlMappingNode map, string key, Func<YamlNode, T> read) =>
        Array.AsReadOnly(Children(map, key).Select(row => (IReadOnlyList<T>)Array.AsReadOnly(Children(row)
            .Select(item => Value(item, key, read)).ToArray())).ToArray());

    private static YamlSequenceNode Grid<T>(IReadOnlyList<IReadOnlyList<T>> grid, Func<T, YamlNode> write) =>
        Sequence(grid.Select(row => (YamlNode)Sequence(row.Select(write))));

    /// <summary>moduleMap: a grid of std::pair&lt;int, int&gt;, each a sequence of exactly two integers (first in all, second left).</summary>
    private static IReadOnlyList<BattleModuleSnapshot>[] ReadModuleMap(YamlNode node) =>
        [.. Children(node).Select(static row => (IReadOnlyList<BattleModuleSnapshot>)Array.AsReadOnly(Children(row).Select(static pair =>
        {
            var (total, remaining) = YamlValueReader.ReadPair(pair, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32);
            return new BattleModuleSnapshot(total, remaining);
        }).ToArray()))];

    /// <summary>read(Position): three Sint16 children, each required.</summary>
    private static BattlePosition Position(YamlNode node, string key)
    {
        var children = Children(node);
        if (children.Count < 3) throw new InvalidDataException($"Battle save '{key}' must hold three coordinates.");
        return new BattlePosition(Value(children[0], key, YamlValueReader.ReadInt16), Value(children[1], key, YamlValueReader.ReadInt16),
            Value(children[2], key, YamlValueReader.ReadInt16));
    }

    /// <summary>Node::load: each key read when present (tryRead), the Node() constructor's values otherwise.</summary>
    private static BattleNodeSnapshot ReadNode(YamlNode node)
    {
        if (node is not YamlMappingNode map) return new BattleNodeSnapshot(0, default, 0, 0, 0, 0, 0, false, [], false);
        return new BattleNodeSnapshot(Int(map, "id", 0),
            map.TryGet("position", out var position) ? Position(position!, "position") : default,
            Int(map, "type", 0), Int(map, "rank", 0), Int(map, "flags", 0), Int(map, "reserved", 0), Int(map, "priority", 0),
            Bool(map, "allocated", false),
            Array.AsReadOnly(Children(map, "links").Select(static link => Value(link, "links", YamlValueReader.ReadInt32)).ToArray()),
            Bool(map, "dummy", false));
    }

    /// <summary>Node::save: <c>allocated</c> and <c>dummy</c> only when set.</summary>
    private static YamlMappingNode WriteNode(BattleNodeSnapshot node) => Overlay(null,
    [
        Pair("id", Integer(node.Id)),
        Pair("position", Sequence([Integer(node.Position.X), Integer(node.Position.Y), Integer(node.Position.Z)])),
        Pair("type", Integer(node.Type)),
        Pair("rank", Integer(node.Rank)),
        Pair("flags", Integer(node.Flags)),
        Pair("reserved", Integer(node.Reserved)),
        Pair("priority", Integer(node.Priority)),
        Pair("allocated", node.Allocated ? Boolean(true) : null),
        Pair("links", Sequence(node.Links.Select(static link => (YamlNode)Integer(link)))),
        Pair("dummy", node.Dummy ? Boolean(true) : null),
    ]);

    /// <summary>Tile::serializationKey, the field sizes in bytes the port writes (the reference's current key).</summary>
    private static class TileKey
    {
        public const int Index = 4;
        public const int MapDataSetId = 2;
        public const int MapDataId = 2;
        public const int Smoke = 1;
        public const int Fire = 1;
        public const int BoolFields = 1;
        public const int LastExploredByHostile = 2;
        public const int LastExploredByNeutral = 2;
        public const int LastExploredByPlayer = 2;
        public const int TotalBytes = Index + MapDataId * 4 + MapDataSetId * 4 + Smoke + Fire + BoolFields + LastExploredByHostile +
            LastExploredByNeutral + LastExploredByPlayer;
    }

    private static YamlScalarNode Size(int size) => new(GeneratedSpan, ((char)size).ToString(), YamlScalarStyle.DoubleQuoted);

    /// <summary>readVal&lt;char&gt;(default): a one-character value is its character code; anything else fails.</summary>
    private static int ReadSize(YamlMappingNode battle, string key, int fallback)
    {
        if (!battle.TryGet(key, out var node)) return fallback;
        return node is YamlScalarNode { Value: { Length: 1 } value } && value[0] <= byte.MaxValue ? value[0]
            : throw new InvalidDataException($"Battle save '{key}' cannot be read: a tile field size is one character.");
    }

    /// <summary>
    /// SavedBattleGame::load's binary tiles: the serialization key (each size missing is 0, except <c>tileBoolFieldsSize</c>,
    /// which older saves did not write, 1), then <c>totalTiles</c> records of <c>tileTotalBytesPer</c> bytes, each an index
    /// and the fields Tile::loadBinary reads. A size other than 1, 2 or 4 reads as 0 and skips its bytes (unserializeInt's
    /// release path, which logs a warning; the port warns once per key); a <c>lastExploredBy</c> size of 0 leaves the
    /// field alone. A read past the decoded buffer, a record shorter than its index or an index outside the map fails.
    /// </summary>
    private static ReadOnlyCollection<BattleTileSnapshot> BinaryTiles(YamlMappingNode battle, YamlNode totalBytesNode, int width, int length,
        int height, IDiagnosticSink? diagnostics)
    {
        var totalBytes = Value(totalBytesNode, "tileTotalBytesPer", YamlValueReader.ReadUInt32);
        var totalTiles = battle.TryGet("totalTiles", out var count) ? Value(count!, "totalTiles", YamlValueReader.ReadUInt64)
            : throw new InvalidDataException("Battle save 'totalTiles' is missing.");
        var index = ReadSize(battle, "tileIndexSize", 0);
        var fire = ReadSize(battle, "tileFireSize", 0);
        var smoke = ReadSize(battle, "tileSmokeSize", 0);
        var id = ReadSize(battle, "tileIDSize", 0);
        var set = ReadSize(battle, "tileSetIDSize", 0);
        var flags = ReadSize(battle, "tileBoolFieldsSize", 1);
        var player = ReadSize(battle, "lastExploredByPlayer", 0);
        var hostile = ReadSize(battle, "lastExploredByHostile", 0);
        var neutral = ReadSize(battle, "lastExploredByNeutral", 0);
        foreach (var (key, size, optional) in new[]
        {
            ("tileIndexSize", index, false), ("tileFireSize", fire, false), ("tileSmokeSize", smoke, false), ("tileIDSize", id, false),
            ("tileSetIDSize", set, false), ("tileBoolFieldsSize", flags, false), ("lastExploredByPlayer", player, true),
            ("lastExploredByHostile", hostile, true), ("lastExploredByNeutral", neutral, true),
        })
            if (size is not (1 or 2 or 4) && !(optional && size == 0))
                Skipped(diagnostics, battle.TryGet(key, out var sizeNode) ? sizeNode! : battle,
                    $"Battle save '{key}' gives tile fields of {size} bytes, which read as 0 (the reference logs a warning).");
        if (!battle.TryGet("binTiles", out var data)) throw new InvalidDataException("Battle save 'binTiles' is missing.");
        var buffer = data is YamlScalarNode { Value.Length: 0 } ? [] : Value(data!, "binTiles", static node => YamlValueReader.ReadBase64(node));
        ulong end;
        try
        {
            end = checked(totalTiles * totalBytes);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException($"Battle save 'totalTiles' {totalTiles} of {totalBytes} bytes overflows the tile buffer.");
        }
        var tileCount = (long)width * length * height;
        var tiles = new SortedDictionary<int, BattleTileSnapshot>();
        for (ulong start = 0; start < end; start += totalBytes)
        {
            if (start > (ulong)buffer.Length || totalBytes < (uint)index)
                throw new InvalidDataException(
                    $"Battle save 'binTiles' holds {buffer.Length} bytes, short of {totalTiles} tiles of {totalBytes} bytes with a {index}-byte index.");
            var cursor = (long)start;
            var tileIndex = Field(buffer, ref cursor, index);
            // assert(index >= 0 && index < _mapsize_x * _mapsize_z * _mapsize_y)
            if (tileIndex < 0 || tileIndex >= tileCount)
                throw new InvalidDataException($"Battle save 'binTiles' names tile {tileIndex}, outside the {width}x{length}x{height} map.");
            var tile = tiles.TryGetValue(tileIndex, out var existing) ? existing : new BattleTileSnapshot(tileIndex);
            for (var part = 0; part < 4; part++) tile = tile.WithPart(part, Field(buffer, ref cursor, id), tile.Part(part).Set);
            for (var part = 0; part < 4; part++) tile = tile.WithPart(part, tile.Part(part).Id, Field(buffer, ref cursor, set));
            // _smoke, _fire and the bool fields are Uint8.
            tile = tile with { Smoke = (byte)Field(buffer, ref cursor, smoke), Fire = (byte)Field(buffer, ref cursor, fire) };
            var bits = (byte)Field(buffer, ref cursor, flags);
            tile = tile with
            {
                WestWallDiscovered = (bits & 1) != 0,
                NorthWallDiscovered = (bits & 2) != 0,
                FloorDiscovered = (bits & 4) != 0,
                WestDoorOpen = (bits & 8) != 0,
                NorthDoorOpen = (bits & 0x10) != 0,
            };
            if (hostile != 0) tile = tile with { LastExploredByHostile = Field(buffer, ref cursor, hostile) };
            if (neutral != 0) tile = tile with { LastExploredByNeutral = Field(buffer, ref cursor, neutral) };
            if (player != 0) tile = tile with { LastExploredByPlayer = Field(buffer, ref cursor, player) };
            tiles[tileIndex] = tile;
        }
        return Array.AsReadOnly(tiles.Values.ToArray());
    }

    /// <summary>unserializeInt: a Uint8, a Sint16 or a Uint32 cast to int, little-endian; any other size is 0 and skips its bytes.</summary>
    private static int Field(byte[] buffer, ref long cursor, int size)
    {
        if (size is (1 or 2 or 4) && cursor + size > buffer.Length)
            throw new InvalidDataException($"Battle save 'binTiles' ends inside a tile at byte {cursor}.");
        var at = (int)cursor;
        cursor += size;
        return size switch
        {
            1 => buffer[at],
            2 => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(at)),
            4 => unchecked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(at))),
            _ => 0,
        };
    }

    /// <summary>SavedBattleGame::save's binary tiles with the port's key (Tile::saveBinary after each index).</summary>
    private static byte[] BinaryTiles(IReadOnlyList<BattleTileSnapshot> tiles)
    {
        var buffer = new byte[checked(tiles.Count * TileKey.TotalBytes)];
        var span = buffer.AsSpan();
        foreach (var tile in tiles)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span, unchecked((uint)tile.Index));
            var at = TileKey.Index;
            for (var part = 0; part < 4; part++, at += 2)
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[at..], unchecked((short)tile.Part(part).Id));
            for (var part = 0; part < 4; part++, at += 2)
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[at..], unchecked((short)tile.Part(part).Set));
            span[at++] = unchecked((byte)tile.Smoke);
            span[at++] = unchecked((byte)tile.Fire);
            span[at++] = (byte)((tile.WestWallDiscovered ? 1 : 0) | (tile.NorthWallDiscovered ? 2 : 0) | (tile.FloorDiscovered ? 4 : 0) |
                (tile.WestDoorOpen ? 8 : 0) | (tile.NorthDoorOpen ? 0x10 : 0));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[at..], unchecked((short)tile.LastExploredByHostile));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[(at + 2)..], unchecked((short)tile.LastExploredByNeutral));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[(at + 4)..], unchecked((short)tile.LastExploredByPlayer));
            span = span[TileKey.TotalBytes..];
        }
        return buffer;
    }

    /// <summary>
    /// SavedBattleGame::load's old text tiles and Tile::load: each tile at its required position (inside the map, or
    /// getTile gives null), its keys read over what the tile already has. <c>discovered</c> needs three booleans: the
    /// first went to an undefined part in the reference and is ignored, the second and third both to the floor, the third
    /// last (ADR 0039). <c>openDoorWest</c> and <c>openDoorNorth</c> set the wall's door frame whatever their value.
    /// </summary>
    private static ReadOnlyCollection<BattleTileSnapshot> TextTiles(YamlMappingNode battle, int width, int length, int height)
    {
        var tiles = new SortedDictionary<int, BattleTileSnapshot>();
        foreach (var node in Children(battle, "tiles"))
        {
            var map = node as YamlMappingNode;
            if (map is null || !map.TryGet("position", out var positionNode))
                throw new InvalidDataException("Battle save 'tiles' has a tile without a 'position'.");
            var position = Position(positionNode!, "position");
            if ((uint)position.X >= (uint)width || (uint)position.Y >= (uint)length || (uint)position.Z >= (uint)height)
                throw new InvalidDataException($"Battle save 'tiles' has a tile at {position}, outside the {width}x{length}x{height} map.");
            var index = position.X + position.Y * width + position.Z * width * length;
            var tile = tiles.TryGetValue(index, out var existing) ? existing : new BattleTileSnapshot(index);
            var ids = map.TryGet("mapDataID", out var idNode) ? Children(idNode) : [];
            var sets = map.TryGet("mapDataSetID", out var setNode) ? Children(setNode) : [];
            for (var part = 0; part < 4; part++)
            {
                var (id, set) = tile.Part(part);
                if (part < ids.Count) id = Value(ids[part], "mapDataID", YamlValueReader.ReadInt32);
                if (part < sets.Count) set = Value(sets[part], "mapDataSetID", YamlValueReader.ReadInt32);
                tile = tile.WithPart(part, id, set);
            }
            if (map.TryGet("fire", out var fire)) tile = tile with { Fire = Value(fire!, "fire", YamlValueReader.ReadUInt8) };
            if (map.TryGet("smoke", out var smoke)) tile = tile with { Smoke = Value(smoke!, "smoke", YamlValueReader.ReadUInt8) };
            if (map.TryGet("discovered", out var discoveredNode))
            {
                var discovered = Children(discoveredNode);
                if (discovered.Count < 3) throw new InvalidDataException("Battle save tile 'discovered' must hold three booleans.");
                _ = Value(discovered[0], "discovered", YamlValueReader.ReadBoolean);
                _ = Value(discovered[1], "discovered", YamlValueReader.ReadBoolean);
                tile = tile with { FloorDiscovered = Value(discovered[2], "discovered", YamlValueReader.ReadBoolean) };
            }
            if (map.TryGet("openDoorWest", out _)) tile = tile with { WestDoorOpen = true };
            if (map.TryGet("openDoorNorth", out _)) tile = tile with { NorthDoorOpen = true };
            tiles[index] = tile;
        }
        return Array.AsReadOnly(tiles.Values.ToArray());
    }
}
