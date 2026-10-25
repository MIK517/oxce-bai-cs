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
/// Phase 7 branch 2 S1 reads and writes the battle root. The map, units and items keys (<c>mapdatasets</c>, the tile
/// keys, <c>nodes</c>, <c>moduleMap</c>, the flattened names and reinforcement blocks, <c>units</c>, <c>items</c>,
/// <c>itemsSpecial</c>, <c>recoverGuaranteed</c>, <c>recoverConditional</c>) stay as the source document has them
/// until S2 to S4 read them.
/// </remarks>
public static class OxceBattleSave
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
            ScriptValues = Tags(battle, content, diagnostics),
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
            Pair("globalshade", Integer(snapshot.GlobalShade)),
            Pair("turn", Integer(snapshot.Turn)),
            Pair("bughuntMinTurn", Integer(snapshot.BughuntMinTurn)),
            Pair("animFrame", Integer(snapshot.AnimFrame)),
            Pair("bughuntMode", Boolean(snapshot.BughuntMode)),
            Pair("selectedUnit", Integer(snapshot.SelectedUnitId)),
            Pair("undoUnit", Integer(snapshot.UndoUnitId)),
            Pair("tuReserved", Integer(snapshot.TimeUnitsReserved)),
            Pair("kneelReserved", Boolean(snapshot.KneelReserved)),
            Pair("depth", Integer(snapshot.Depth)),
            Pair("ambience", Integer(snapshot.Ambience)),
            Pair("ambientVolume", Real(snapshot.AmbientVolume)),
            Pair("ambienceRandom", Sequence(snapshot.AmbienceRandom.Select(static sound => (YamlNode)Integer(sound)))),
            Pair("minAmbienceRandomDelay", Integer(snapshot.MinimumAmbienceRandomDelay)),
            Pair("maxAmbienceRandomDelay", Integer(snapshot.MaximumAmbienceRandomDelay)),
            Pair("currentAmbienceDelay", Integer(snapshot.CurrentAmbienceDelay)),
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

    /// <summary>ScriptValuesBase::loadBase for the root's <c>tags</c>: an unknown tag is skipped with an error log.</summary>
    private static ReadOnlyCollection<ScriptValueEntry> Tags(YamlMappingNode battle, RuntimeContent content, IDiagnosticSink? diagnostics)
    {
        if (!battle.TryGet("tags", out var node) || node is not YamlMappingNode tags) return Array.AsReadOnly(Array.Empty<ScriptValueEntry>());
        // The script API always declares BattleGame.Tag, so the owner type exists in any content.
        var type = content.Tags.Types.First(static candidate => candidate.Name == BattleSnapshot.ScriptOwner);
        var values = new List<ScriptValueEntry>();
        foreach (var entry in tags.Entries)
        {
            var name = entry.ScalarKey ?? throw new InvalidDataException("Battle save tag names must be scalars.");
            if (!content.Tags.TryGetTag(type.Id, "Tag." + name, out var tag))
            {
                Skipped(diagnostics, entry.Value, $"Battle save tag '{name}' is not a BattleGame tag of the mods; it is left out.");
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
}
