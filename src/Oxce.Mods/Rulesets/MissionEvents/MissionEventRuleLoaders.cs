using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.MissionEvents;

internal sealed class UfoTrajectoryRuleLoader : TypedRuleFamilyLoader<TrajectoryBuilder, UfoTrajectoryRule>
{
    public UfoTrajectoryRuleLoader() : base(MissionEventYaml.Section("ufoTrajectories")) { }
    protected override TrajectoryBuilder Create(string id) => new();
    protected override void Apply(TrajectoryBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.GroundTimer = reader.ReadInt32("groundTimer", builder.GroundTimer);
        if (!reader.TryGet("waypoints", out var node)) return;
        if (node is not YamlSequenceNode sequence) throw new YamlFormatException("waypoints must be a sequence.", node!.Span);
        builder.Waypoints = sequence.Items.Select(item =>
        {
            var values = MissionEventYaml.Sequence(item, YamlValueReader.ReadInt32, "waypoint");
            if (values.Count != 3) throw new YamlFormatException("Trajectory waypoints require zone, altitude, and speed.", item.Span);
            return new TrajectoryWaypointRule(values[0], values[1], values[2]);
        }).ToList();
    }
    protected override UfoTrajectoryRule Freeze(TrajectoryBuilder builder) => new(builder.GroundTimer, builder.Waypoints.AsReadOnly());
}

internal sealed class AlienMissionRuleLoader : TypedRuleFamilyLoader<AlienMissionBuilder, AlienMissionRule>
{
    public AlienMissionRuleLoader() : base(MissionEventYaml.Section("alienMissions")) { }
    protected override AlienMissionBuilder Create(string id) => new();
    protected override void Apply(AlienMissionBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Integers.Keys.ToArray()) builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray()) builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        if (builder.Booleans["multiUfoRetaliationExtra"]) builder.Booleans["multiUfoRetaliation"] = true;
        foreach (var key in builder.Strings.Keys.ToArray()) builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        if (reader.TryGet("waves", out var waves)) builder.Waves = ReadWaves(waves!);
        if (reader.TryGet("missionWeights", out var missionWeights)) builder.MissionWeights = ReadIntTimeline(missionWeights!, "missionWeights");
        if (reader.TryGet("regionWeights", out var regionWeights)) builder.RegionWeights.AddRange(MissionEventYaml.Timeline(regionWeights!, "regionWeights"));
        if (reader.TryGet("raceWeights", out var raceWeights)) ApplyRaceWeights(builder, raceWeights!);
    }
    protected override AlienMissionRule Freeze(AlienMissionBuilder builder) => new(
        MissionReadOnly.Dictionary(builder.Integers), MissionReadOnly.Dictionary(builder.Booleans), MissionReadOnly.Dictionary(builder.Strings),
        builder.Waves.AsReadOnly(), MissionReadOnly.Dictionary(builder.MissionWeights),
        Array.AsReadOnly(builder.RaceWeights.Select(pair => new WeightedTimelineEntry(pair.Key, MissionReadOnly.Dictionary(pair.Value))).ToArray()),
        builder.RegionWeights.AsReadOnly());
    private static List<MissionWaveRule> ReadWaves(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence) throw new YamlFormatException("waves must be a sequence.", node.Span);
        return sequence.Items.Select(item =>
        {
            if (item is not YamlMappingNode map) throw new YamlFormatException("Mission waves must be mappings.", item.Span);
            string S(string key) => map.TryGet(key, out var child) ? YamlValueReader.ReadString(child!) : "";
            int I(string key, int value) => map.TryGet(key, out var child) ? YamlValueReader.ReadInt32(child!) : value;
            ulong U(string key) => map.TryGet(key, out var child) ? YamlValueReader.ReadUInt64(child!) : 0;
            bool B(string key) => map.TryGet(key, out var child) && YamlValueReader.ReadBoolean(child!);
            return new MissionWaveRule(S("ufo"), U("count"), S("trajectory"), U("timer"), B("objective"),
                B("objectiveOnTheLandingSite"), B("objectiveOnXcomBase"), I("hunterKillerPercentage", -1),
                I("huntMode", -1), I("huntBehavior", -1), B("escort"), I("interruptPercentage", 0));
        }).ToList();
    }
    private static Dictionary<ulong, int> ReadIntTimeline(YamlNode node, string key)
    {
        if (node is not YamlMappingNode mapping) throw new YamlFormatException($"{key} must be a mapping.", node.Span);
        return mapping.Entries.ToDictionary(entry => YamlValueReader.ReadUInt64(entry.Key), entry => YamlValueReader.ReadInt32(entry.Value));
    }
    private static void ApplyRaceWeights(AlienMissionBuilder builder, YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("raceWeights must be a mapping.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var month = YamlValueReader.ReadUInt64(entry.Key);
            if (!builder.RaceWeights.TryGetValue(month, out var weights)) builder.RaceWeights[month] = weights = new(StringComparer.Ordinal);
            if (entry.Value is not YamlMappingNode values) throw new YamlFormatException("raceWeights entries must be mappings.", entry.Value.Span);
            foreach (var pair in values.Entries)
            {
                var id = YamlValueReader.ReadString(pair.Key); var value = YamlValueReader.ReadUInt64(pair.Value);
                if (value == 0) weights.Remove(id); else weights[id] = value;
            }
            if (weights.Count == 0) builder.RaceWeights.Remove(month);
        }
    }
}

internal enum StrategicScriptKind { Arc, Event, Mission }

internal sealed class StrategicScriptRuleLoader : TypedRuleFamilyLoader<StrategicScriptBuilder, StrategicScriptRule>
{
    private readonly StrategicScriptKind _kind;
    public StrategicScriptRuleLoader(string section, StrategicScriptKind kind) : base(MissionEventYaml.Section(section)) { _kind = kind; }
    protected override StrategicScriptBuilder Create(string id)
    {
        var builder = new StrategicScriptBuilder();
        builder.Integers["firstMonth"] = 0; builder.Integers["lastMonth"] = -1; builder.Integers["executionOdds"] = 100;
        builder.Integers["minDifficulty"] = 0; builder.Integers["maxDifficulty"] = 4;
        builder.Integers["minScore"] = int.MinValue; builder.Integers["maxScore"] = int.MaxValue;
        builder.Longs["minFunds"] = long.MinValue; builder.Longs["maxFunds"] = long.MaxValue;
        builder.Strings["missionVarName"] = ""; builder.Strings["missionMarkerName"] = "";
        builder.Integers["counterMin"] = 0; builder.Integers["counterMax"] = -1;
        if (_kind == StrategicScriptKind.Arc) builder.Integers["maxArcs"] = -1;
        if (_kind == StrategicScriptKind.Event) builder.Booleans["affectsGameProgression"] = false;
        if (_kind == StrategicScriptKind.Mission)
        {
            builder.Strings["varName"] = ""; builder.Integers["label"] = 0; builder.Integers["targetBaseOdds"] = 0;
            builder.Integers["maxRuns"] = -1; builder.Integers["avoidRepeats"] = 0; builder.Integers["startDelay"] = 0;
            builder.Integers["randomDelay"] = 0; builder.Booleans["useTable"] = true;
        }
        return builder;
    }
    protected override void Apply(StrategicScriptBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Integers.Keys.ToArray()) builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Longs.Keys.ToArray()) builder.Longs[key] = MissionEventYaml.ReadLong(reader, key, builder.Longs[key]);
        foreach (var key in builder.Booleans.Keys.ToArray()) builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        foreach (var key in builder.Strings.Keys.ToArray()) builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        if (_kind == StrategicScriptKind.Event)
        {
            builder.Integers["counterMin"] = reader.ReadInt32("missionMinRuns", builder.Integers["counterMin"]);
            builder.Integers["counterMax"] = reader.ReadInt32("missionMaxRuns", builder.Integers["counterMax"]);
            builder.Integers["counterMin"] = reader.ReadInt32("counterMin", builder.Integers["counterMin"]);
            builder.Integers["counterMax"] = reader.ReadInt32("counterMax", builder.Integers["counterMax"]);
            ReadStrings(reader, "oneTimeSequentialEvents", value => builder.Sequential = value);
            ApplyWeights(reader, "oneTimeRandomEvents", builder.Random);
            ReadTimeline(reader, "eventWeights", value => builder.EventWeights.AddRange(value));
        }
        else if (_kind == StrategicScriptKind.Arc)
        {
            ReadStrings(reader, "sequentialArcs", value => builder.Sequential = value);
            ApplyWeights(reader, "randomArcs", builder.Random);
        }
        else
        {
            ReadInts(reader, "conditionals", value => builder.Conditionals = value);
            ReadStrings(reader, "adhocMissionScriptTags", value => builder.Tags = value);
            ReadTimeline(reader, "missionWeights", value => builder.MissionWeights.AddRange(value));
            ReadTimeline(reader, "raceWeights", value => builder.RaceWeights.AddRange(value));
            ReadTimeline(reader, "regionWeights", value => builder.RegionWeights.AddRange(value));
        }
        foreach (var key in StrategicScriptBuilder.TriggerKeys)
            if (reader.TryGet(key, out var node)) builder.Triggers[key] = MissionEventYaml.BoolMap(node!, key);
        if (_kind == StrategicScriptKind.Mission && builder.Strings["varName"].Length == 0 &&
            (builder.Integers["maxRuns"] > 0 || builder.Integers["avoidRepeats"] > 0))
            throw new YamlFormatException("Mission scripts with maxRuns or avoidRepeats require varName.", reader.Span);
    }
    protected override StrategicScriptRule Freeze(StrategicScriptBuilder builder) => new(
        MissionReadOnly.Dictionary(builder.Integers), MissionReadOnly.Dictionary(builder.Longs), MissionReadOnly.Dictionary(builder.Booleans),
        MissionReadOnly.Dictionary(builder.Strings), builder.Sequential.AsReadOnly(), MissionReadOnly.Dictionary(builder.Random),
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>(builder.Triggers.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyDictionary<string, bool>)MissionReadOnly.Dictionary(pair.Value), StringComparer.Ordinal)),
        builder.Conditionals.AsReadOnly(), builder.Tags.AsReadOnly(), builder.MissionWeights.AsReadOnly(), builder.RaceWeights.AsReadOnly(),
        builder.RegionWeights.AsReadOnly(), builder.EventWeights.AsReadOnly());
    private static void ReadStrings(RulePropertyReader reader, string key, Action<List<string>> set)
    { if (reader.TryGet(key, out var node)) set(MissionEventYaml.Strings(node!, key)); }
    private static void ReadInts(RulePropertyReader reader, string key, Action<List<int>> set)
    { if (reader.TryGet(key, out var node)) set(MissionEventYaml.Integers(node!, key)); }
    private static void ApplyWeights(RulePropertyReader reader, string key, Dictionary<string, ulong> target)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node is not YamlMappingNode mapping) throw new YamlFormatException($"{key} must be a mapping.", node!.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = YamlValueReader.ReadString(entry.Key); var value = YamlValueReader.ReadUInt64(entry.Value);
            if (value == 0) target.Remove(id); else target[id] = value;
        }
    }
    private static void ReadTimeline(RulePropertyReader reader, string key, Action<List<WeightedTimelineEntry>> set)
    { if (reader.TryGet(key, out var node)) set(MissionEventYaml.Timeline(node!, key)); }
}

internal sealed class EventRuleLoader : TypedRuleFamilyLoader<EventBuilder, EventRule>
{
    public EventRuleLoader() : base(MissionEventYaml.Section("events")) { }
    protected override EventBuilder Create(string id) => new();
    protected override void Apply(EventBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Strings.Keys.ToArray()) builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        foreach (var key in builder.Integers.Keys.ToArray()) builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray()) builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        ReadStrings(reader, "regionList", value => builder.Regions = value); ReadStrings(reader, "everyItemList", value => builder.EveryItems = value);
        ReadStrings(reader, "randomItemList", value => builder.RandomItems = value); ReadStrings(reader, "researchList", value => builder.Research = value);
        ReadStrings(reader, "adhocMissionScriptTags", value => builder.Tags = value);
        ReadIntMap(reader, "everyMultiItemList", value => builder.EveryMultiItems = value);
        ReadIntMap(reader, "everyMultiSoldierList", value => builder.EveryMultiSoldiers = value);
        ReadMapList(reader, "randomMultiItemList", value => builder.RandomMultiItems = value);
        ReadMapList(reader, "randomMultiSoldierList", value => builder.RandomMultiSoldiers = value);
        if (reader.TryGet("weightedItemList", out var weighted)) ApplyWeights(builder.WeightedItems, weighted!, "weightedItemList");
        if (reader.TryGet("spawnedSoldier", out var soldier))
        {
            if (soldier is not YamlMappingNode mapping) throw new YamlFormatException("spawnedSoldier must be a mapping.", soldier!.Span);
            builder.SpawnedSoldier = MissionEventYaml.Overlay(mapping, builder.SpawnedSoldier);
        }
    }
    protected override EventRule Freeze(EventBuilder builder) => new(
        MissionReadOnly.Dictionary(builder.Strings), MissionReadOnly.Dictionary(builder.Integers), MissionReadOnly.Dictionary(builder.Booleans),
        builder.Regions.AsReadOnly(), MissionReadOnly.Dictionary(builder.EveryMultiItems), builder.EveryItems.AsReadOnly(), builder.RandomItems.AsReadOnly(),
        Array.AsReadOnly(builder.RandomMultiItems.Select(value => (IReadOnlyDictionary<string, int>)MissionReadOnly.Dictionary(value)).ToArray()),
        MissionReadOnly.Dictionary(builder.WeightedItems), builder.Research.AsReadOnly(), builder.Tags.AsReadOnly(),
        MissionReadOnly.Dictionary(builder.EveryMultiSoldiers),
        Array.AsReadOnly(builder.RandomMultiSoldiers.Select(value => (IReadOnlyDictionary<string, int>)MissionReadOnly.Dictionary(value)).ToArray()), builder.SpawnedSoldier);
    private static void ReadStrings(RulePropertyReader reader, string key, Action<List<string>> set) { if (reader.TryGet(key, out var node)) set(MissionEventYaml.Strings(node!, key)); }
    private static void ReadIntMap(RulePropertyReader reader, string key, Action<Dictionary<string, int>> set) { if (reader.TryGet(key, out var node)) set(MissionEventYaml.IntMap(node!, key)); }
    private static void ReadMapList(RulePropertyReader reader, string key, Action<List<Dictionary<string, int>>> set)
    { if (!reader.TryGet(key, out var node)) return; set(MissionEventYaml.Sequence(node!, value => MissionEventYaml.IntMap(value, key), key)); }
    private static void ApplyWeights(Dictionary<string, ulong> target, YamlNode node, string key)
    {
        if (node is not YamlMappingNode mapping) throw new YamlFormatException($"{key} must be a mapping.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = YamlValueReader.ReadString(entry.Key); var value = YamlValueReader.ReadUInt64(entry.Value);
            if (value == 0) target.Remove(id); else target[id] = value;
        }
    }
}
