using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.TerrainDeployment;

internal sealed class TerrainRuleLoader : TypedRuleFamilyLoader<TerrainBuilder, TerrainRule>
{
    public TerrainRuleLoader() : base(TerrainDeploymentYaml.Section("terrains")) { }
    protected override TerrainBuilder Create(string id) => new(id);
    protected override void Apply(TerrainBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        var addOnly = reader.ReadBoolean("addOnly", false);
        if (reader.TryGet("mapDataSets", out var sets)) builder.MapDataSets = TerrainDeploymentYaml.Strings(sets!, "mapDataSets");
        if (reader.TryGet("mapBlocks", out var blocks)) ApplyMapBlocks(builder, blocks!, addOnly);
        builder.EnviroEffects = reader.ReadString("enviroEffects", builder.EnviroEffects);
        TerrainDeploymentYaml.EditableNames(reader, "civilianTypes", builder.CivilianTypes);
        TerrainDeploymentYaml.EditableNames(reader, "music", builder.Music);
        if (reader.TryGet("depth", out var depth))
        {
            var pair = TerrainDeploymentYaml.FixedPair(depth!, [builder.MinimumDepth, builder.MaximumDepth], "depth");
            builder.MinimumDepth = pair[0]; builder.MaximumDepth = pair[1];
        }
        if (reader.TryGet("ambience", out var ambience))
            builder.Ambience = PresentationYaml.ReadIndexReference(ambience!, reader.Source.ModId);
        builder.AmbientVolume = reader.ReadSingle("ambientVolume", builder.AmbientVolume);
        if (reader.TryGet("ambienceRandom", out var random))
            builder.RandomAmbience = TerrainDeploymentYaml.Values(random!,
                value => PresentationYaml.ReadIndexReference(value, reader.Source.ModId), "ambienceRandom");
        if (reader.TryGet("ambienceRandomDelay", out var delay))
        {
            var pair = TerrainDeploymentYaml.FixedPair(delay!, [builder.MinimumAmbienceDelay, builder.MaximumAmbienceDelay], "ambienceRandomDelay");
            builder.MinimumAmbienceDelay = pair[0]; builder.MaximumAmbienceDelay = pair[1];
        }
        builder.MapScript = reader.ReadString("script", builder.MapScript);
        if (reader.TryGet("mapScripts", out var scripts)) builder.MapScripts = TerrainDeploymentYaml.Strings(scripts!, "mapScripts");
    }
    protected override TerrainRule Freeze(TerrainBuilder builder) => new(
        builder.MapDataSets.AsReadOnly(), builder.MapBlocks.AsReadOnly(), builder.EnviroEffects,
        builder.CivilianTypes.AsReadOnly(), builder.Music.AsReadOnly(), builder.MinimumDepth,
        builder.MaximumDepth, builder.Ambience, builder.AmbientVolume, builder.RandomAmbience.AsReadOnly(),
        builder.MinimumAmbienceDelay, builder.MaximumAmbienceDelay, builder.MapScript, builder.MapScripts.AsReadOnly());

    private static void ApplyMapBlocks(TerrainBuilder builder, YamlNode node, bool addOnly)
    {
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException("mapBlocks must be a sequence.", node.Span);
        if (!addOnly) builder.MapBlocks.Clear();
        foreach (var item in sequence.Items)
        {
            if (item is not YamlMappingNode map)
                throw new YamlFormatException("mapBlocks entries must be mappings.", item.Span);
            var name = ReadString(map, "name", "");
            var width = ReadInt(map, "width", 10); var length = ReadInt(map, "length", 10);
            if (width % 10 != 0 || length % 10 != 0)
                throw new YamlFormatException($"MapBlock '{name}' size must be divisible by ten.", item.Span);
            builder.MapBlocks.Add(new MapBlockRule(name, width, length, ReadInt(map, "height", 4),
                ReadInts(map, "groups", [0]), ReadInts(map, "revealedFloors", []),
                Get(map, "items"), Get(map, "fuseTimers"), Get(map, "randomizedItems"), Get(map, "extendedItems"),
                ReadInts(map, "craftInventoryTile", [])));
        }
    }
    private static YamlNode? Get(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? node : null;
    private static string ReadString(YamlMappingNode map, string key, string value) => map.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : value;
    private static int ReadInt(YamlMappingNode map, string key, int value) => map.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : value;
    private static IReadOnlyList<int> ReadInts(YamlMappingNode map, string key, IReadOnlyList<int> value) =>
        map.TryGet(key, out var node) ? TerrainDeploymentYaml.Integers(node!, key).AsReadOnly() : value;
}

internal sealed class AlienRaceRuleLoader : TypedRuleFamilyLoader<AlienRaceBuilder, AlienRaceRule>
{
    public AlienRaceRuleLoader() : base(TerrainDeploymentYaml.Section("alienRaces")) { }
    protected override AlienRaceBuilder Create(string id) => throw new NotSupportedException();
    protected override AlienRaceBuilder Create(UnresolvedRule rule) => new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(AlienRaceBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.BaseCustomDeploy = reader.ReadString("baseCustomDeploy", builder.BaseCustomDeploy);
        builder.BaseCustomMission = reader.ReadString("baseCustomMission", builder.BaseCustomMission);
        if (reader.TryGet("members", out var members)) builder.Members = TerrainDeploymentYaml.Strings(members!, "members");
        if (reader.TryGet("membersRandom", out var random))
        {
            if (random is not YamlSequenceNode rows) throw new YamlFormatException("membersRandom must be a sequence.", random!.Span);
            builder.RandomMembers = rows.Items.Select(row => TerrainDeploymentYaml.Strings(row, "membersRandom")).ToList();
        }
        builder.RetaliationAggression = reader.ReadInt32("retaliationAggression", builder.RetaliationAggression);
        if (reader.TryGet("retaliationMissionWeights", out var weights))
            builder.RetaliationWeights.AddRange(TerrainDeploymentYaml.ReadWeightTimeline(weights!, "retaliationMissionWeights"));
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
    }
    protected override AlienRaceRule Freeze(AlienRaceBuilder builder) => new(
        builder.BaseCustomDeploy, builder.BaseCustomMission, builder.Members.AsReadOnly(),
        Array.AsReadOnly(builder.RandomMembers.Select(row => (IReadOnlyList<string>)row.AsReadOnly()).ToArray()),
        builder.RetaliationAggression, builder.RetaliationWeights.AsReadOnly(), builder.ListOrder);
}

internal sealed class EnviroEffectsRuleLoader : TypedRuleFamilyLoader<EnviroEffectsBuilder, EnviroEffectsRule>
{
    public EnviroEffectsRuleLoader() : base(TerrainDeploymentYaml.Section("enviroEffects")) { }
    protected override EnviroEffectsBuilder Create(string id) => new();
    protected override void Apply(EnviroEffectsBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        if (reader.TryGet("environmentalConditions", out var conditions)) builder.Conditions = ReadConditions(conditions!);
        ApplyStringMap(reader, "paletteTransformations", builder.PaletteTransformations);
        ApplyStringMap(reader, "armorTransformations", builder.ArmorTransformations);
        builder.MapBackgroundColor = reader.ReadInt32("mapBackgroundColor", builder.MapBackgroundColor);
        builder.IgnoreNightVision = reader.ReadBoolean("ignoreAutoNightVisionUserSetting", builder.IgnoreNightVision);
        builder.InventoryShockIndicator = reader.ReadString("inventoryShockIndicator", builder.InventoryShockIndicator);
        builder.MapShockIndicator = reader.ReadString("mapShockIndicator", builder.MapShockIndicator);
    }
    protected override EnviroEffectsRule Freeze(EnviroEffectsBuilder builder) => new(
        TerrainReadOnly.Dictionary(builder.Conditions), TerrainReadOnly.Dictionary(builder.PaletteTransformations),
        TerrainReadOnly.Dictionary(builder.ArmorTransformations), builder.MapBackgroundColor, builder.IgnoreNightVision,
        builder.InventoryShockIndicator, builder.MapShockIndicator);
    private static Dictionary<string, EnvironmentalConditionRule> ReadConditions(YamlNode node)
    {
        if (node is not YamlMappingNode mapping) throw new YamlFormatException("environmentalConditions must be a mapping.", node.Span);
        return mapping.Entries.ToDictionary(entry => entry.ScalarKey ?? throw new YamlFormatException("Condition keys must be scalars.", entry.Key.Span),
            entry => ReadCondition(entry.Value), StringComparer.Ordinal);
    }
    private static EnvironmentalConditionRule ReadCondition(YamlNode node)
    {
        if (node is not YamlMappingNode map) throw new YamlFormatException("Environmental condition must be a mapping.", node.Span);
        int I(string key, int value) => map.TryGet(key, out var child) ? YamlValueReader.ReadInt32(child!) : value;
        string S(string key) => map.TryGet(key, out var child) ? YamlValueReader.ReadString(child!) : "";
        return new(I("globalChance", 100), I("chancePerTurn", 0), I("firstTurn", 1), I("lastTurn", 1000),
            S("message"), I("color", 29), S("weaponOrAmmo"), I("side", -1), I("bodyPart", -1));
    }
    private static void ApplyStringMap(RulePropertyReader reader, string key, Dictionary<string, string> target)
    {
        if (reader.TryGet(key, out var node)) PersonnelTacticalYaml.ApplyEditableMap(target, node!,
            YamlValueReader.ReadString, YamlValueReader.ReadString, key);
    }
}

internal sealed class StartingConditionRuleLoader : TypedRuleFamilyLoader<StartingConditionBuilder, StartingConditionRule>
{
    public StartingConditionRuleLoader() : base(TerrainDeploymentYaml.Section("startingConditions")) { }
    protected override StartingConditionBuilder Create(string id) => new();
    protected override void Apply(StartingConditionBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        if (reader.TryGet("defaultArmor", out var armor)) PersonnelTacticalYaml.ApplyEditableMap(builder.DefaultArmor, armor!,
            YamlValueReader.ReadString, ReadStringIntMap, "defaultArmor");
        foreach (var pair in builder.Collections) TerrainDeploymentYaml.EditableNames(reader, pair.Key, pair.Value);
        if (reader.TryGet("requiredItems", out var required)) PersonnelTacticalYaml.ApplyEditableMap(builder.RequiredItems, required!,
            YamlValueReader.ReadString, YamlValueReader.ReadInt32, "requiredItems");
        if (reader.TryGet("craftTransformations", out var crafts)) PersonnelTacticalYaml.ApplyEditableMap(builder.CraftTransformations, crafts!,
            YamlValueReader.ReadString, YamlValueReader.ReadString, "craftTransformations");
        builder.DestroyRequiredItems = reader.ReadBoolean("destroyRequiredItems", builder.DestroyRequiredItems);
        builder.RequireCommanderOnboard = reader.ReadBoolean("requireCommanderOnboard", builder.RequireCommanderOnboard);
        foreach (var obsolete in new[] { "environmentalConditions", "paletteTransformations", "armorTransformations", "mapBackgroundColor", "inventoryShockIndicator", "mapShockIndicator" })
            reader.Defer(obsolete, "obsolete starting-condition attribute retained for compatibility diagnostics");
    }
    protected override StartingConditionRule Freeze(StartingConditionBuilder builder) => new(
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(builder.DefaultArmor.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyDictionary<string, int>)TerrainReadOnly.Dictionary(pair.Value), StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(builder.Collections.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(), StringComparer.Ordinal)),
        TerrainReadOnly.Dictionary(builder.RequiredItems), TerrainReadOnly.Dictionary(builder.CraftTransformations),
        builder.DestroyRequiredItems, builder.RequireCommanderOnboard);
    private static Dictionary<string, int> ReadStringIntMap(YamlNode node) =>
        YamlValueReader.ReadMap(node, YamlValueReader.ReadString, YamlValueReader.ReadInt32).ToDictionary();
}

internal sealed class AlienDeploymentRuleLoader : TypedRuleFamilyLoader<AlienDeploymentBuilder, AlienDeploymentRule>
{
    public AlienDeploymentRuleLoader() : base(TerrainDeploymentYaml.Section("alienDeployments")) { }
    protected override AlienDeploymentBuilder Create(string id) => new();
    protected override void Apply(AlienDeploymentBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Strings.Keys.ToArray()) builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        foreach (var key in builder.Integers.Keys.ToArray()) builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray()) builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        ReadStrings(reader, "terrains", value => builder.Terrains = value);
        ReadStrings(reader, "randomRace", value => builder.RandomRaces = value);
        ReadStrings(reader, "mapScripts", value => builder.MapScripts = value);
        ReadStrings(reader, "music", value => builder.Music = value);
        if (reader.TryGet("civiliansByType", out var civilians)) PersonnelTacticalYaml.ApplyEditableMap(builder.CiviliansByType,
            civilians!, YamlValueReader.ReadString, YamlValueReader.ReadInt32, "civiliansByType");
        if (reader.TryGet("depth", out var depth)) builder.Depth = TerrainDeploymentYaml.FixedPair(depth!, builder.Depth, "depth");
        if (reader.TryGet("duration", out var duration)) builder.Duration = TerrainDeploymentYaml.FixedPair(duration!, builder.Duration, "duration");
        ReadNodes(reader, "data", value => builder.Data = value);
        ReadNodes(reader, "reinforcements", value => builder.Reinforcements = value);
        ReadNode(reader, "briefing", value => builder.Briefing = value);
        ReadNode(reader, "successEvents", value => builder.SuccessEvents = value);
        ReadNode(reader, "despawnEvents", value => builder.DespawnEvents = value);
        ReadNode(reader, "failureEvents", value => builder.FailureEvents = value);
        ReadNode(reader, "genMission", value => builder.GenMission = value);
        if (reader.TryGet("huntMissionWeights", out var hunts)) builder.HuntMissionWeights.AddRange(TerrainDeploymentYaml.ReadWeightTimeline(hunts!, "huntMissionWeights"));
        if (reader.TryGet("alienBaseUpgrades", out var upgrades)) builder.AlienBaseUpgrades.AddRange(TerrainDeploymentYaml.ReadWeightTimeline(upgrades!, "alienBaseUpgrades"));
        ReadNodes(reader, "alienRaceEvolution", value => builder.AlienRaceEvolution = value.OrderByDescending(ReadEvolutionMonth).ToList());
        foreach (var key in new[] { "alertSound", "objectiveComplete", "objectiveFailed" })
            reader.Defer(key, "resource offsets or paired mission text are retained for the closure pass");
        reader.DeferRemaining("deployment extensions and dynamic script values require their owning later slice");
    }
    protected override AlienDeploymentRule Freeze(AlienDeploymentBuilder builder) => new(
        TerrainReadOnly.Dictionary(builder.Strings), TerrainReadOnly.Dictionary(builder.Integers),
        TerrainReadOnly.Dictionary(builder.Booleans), builder.Terrains.AsReadOnly(), builder.RandomRaces.AsReadOnly(),
        builder.MapScripts.AsReadOnly(), builder.Music.AsReadOnly(), TerrainReadOnly.Dictionary(builder.CiviliansByType),
        builder.Depth.AsReadOnly(), builder.Duration.AsReadOnly(), builder.Data.AsReadOnly(), builder.Reinforcements.AsReadOnly(),
        builder.Briefing, builder.SuccessEvents, builder.DespawnEvents, builder.FailureEvents, builder.GenMission,
        builder.HuntMissionWeights.AsReadOnly(), builder.AlienBaseUpgrades.AsReadOnly(), builder.AlienRaceEvolution.AsReadOnly());
    private static void ReadStrings(RulePropertyReader reader, string key, Action<List<string>> set)
    { if (reader.TryGet(key, out var node)) set(TerrainDeploymentYaml.Strings(node!, key)); }
    private static void ReadNodes(RulePropertyReader reader, string key, Action<List<YamlNode>> set)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node is not YamlSequenceNode sequence) throw new YamlFormatException($"{key} must be a sequence.", node!.Span);
        set(sequence.Items.ToList());
    }
    private static void ReadNode(RulePropertyReader reader, string key, Action<YamlNode> set)
    { if (reader.TryGet(key, out var node)) set(node!); }
    private static ulong ReadEvolutionMonth(YamlNode node) => node is YamlSequenceNode sequence && sequence.Items.Count > 0
        ? YamlValueReader.ReadUInt64(sequence.Items[0]) : 0;
}
