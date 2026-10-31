using System.Collections.ObjectModel;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.Presentation;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// The rule families only the battlescape reads (Phase 7, ADR 0038), linked as one group by <see cref="Link"/>, apart
/// from the strategic families. Terrain files (MCD, MAP, RMP) load when a battle needs them, as
/// <c>MapDataSet::loadData</c> and <c>BattlescapeGenerator::loadMAP</c> do.
/// </summary>
public sealed class RuntimeBattleRules
{
    private RuntimeBattleRules(IReadOnlyList<string> terrainOrder, IReadOnlyDictionary<string, RuntimeTerrain> terrains,
        IReadOnlyDictionary<string, RuntimeMcdPatch> mcdPatches, IReadOnlyDictionary<string, RuntimeMapScript> mapScripts)
    {
        TerrainOrder = terrainOrder;
        Terrains = terrains;
        McdPatches = mcdPatches;
        MapScripts = mapScripts;
    }

    public static RuntimeBattleRules Empty { get; } = new(Array.AsReadOnly(Array.Empty<string>()),
        new ReadOnlyDictionary<string, RuntimeTerrain>(new Dictionary<string, RuntimeTerrain>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, RuntimeMcdPatch>(new Dictionary<string, RuntimeMcdPatch>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, RuntimeMapScript>(new Dictionary<string, RuntimeMapScript>(StringComparer.Ordinal)));

    /// <summary>Mod::getTerrainList: terrain IDs in rule order (the first is the generator's last resort).</summary>
    public IReadOnlyList<string> TerrainOrder { get; }

    public IReadOnlyDictionary<string, RuntimeTerrain> Terrains { get; }

    /// <summary>Mod::getMCDPatch, by map data set name.</summary>
    public IReadOnlyDictionary<string, RuntimeMcdPatch> McdPatches { get; }

    /// <summary>Mod::getMapScript, by ID.</summary>
    public IReadOnlyDictionary<string, RuntimeMapScript> MapScripts { get; }

    /// <summary>RuleEnviroEffects::getArmorTransformation sources and targets, by enviro effects ID.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EnviroArmorTransformations { get; private init; } =
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));

    /// <summary>
    /// RuleEnviroEffects::getEnvironmetalCondition's <c>globalChance</c> for STR_FRIENDLY, STR_HOSTILE and STR_NEUTRAL (100
    /// for a faction without a condition), by enviro effects ID.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeEnviroChances> EnviroGlobalChances { get; private init; } =
        new ReadOnlyDictionary<string, RuntimeEnviroChances>(new Dictionary<string, RuntimeEnviroChances>(StringComparer.Ordinal));

    /// <summary>The rules the battle reads when it equips units.</summary>
    public RuntimeBattleEquipment Equipment { get; private init; } = RuntimeBattleEquipment.Empty;

    internal static RuntimeBattleRules Link(TerrainDeploymentRuleCatalog content, ResolvedResourceCatalog resources,
        RuntimeBattleEquipment? equipment = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resources);
        var terrains = new Dictionary<string, RuntimeTerrain>(StringComparer.Ordinal);
        foreach (var rule in content.Terrains.Rules) terrains.Add(rule.Id, Terrain(rule.Id, rule.Value, resources));
        var patches = new Dictionary<string, RuntimeMcdPatch>(StringComparer.Ordinal);
        foreach (var (id, patch) in content.McdPatches) patches.Add(id, new RuntimeMcdPatch(List(patch.Entries.Select(static entry =>
            new RuntimeMcdPatchEntry(entry.Index, Map(entry.Integers), Map(entry.Booleans), entry.Lofts is null ? null : List(entry.Lofts))))));
        var scripts = content.MapScripts.ToDictionary(static pair => pair.Key, static pair => RuntimeMapScriptProjection.Project(pair.Value),
            StringComparer.Ordinal);
        var enviro = content.EnviroEffects.Rules.ToDictionary(static rule => rule.Id,
            static rule => (IReadOnlyDictionary<string, string>)Map(rule.Value.ArmorTransformations), StringComparer.Ordinal);
        return new(List(content.Terrains.Rules.Select(static rule => rule.Id)),
            new ReadOnlyDictionary<string, RuntimeTerrain>(terrains), new ReadOnlyDictionary<string, RuntimeMcdPatch>(patches),
            new ReadOnlyDictionary<string, RuntimeMapScript>(scripts))
        {
            EnviroArmorTransformations = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(enviro),
            EnviroGlobalChances = new ReadOnlyDictionary<string, RuntimeEnviroChances>(content.EnviroEffects.Rules.ToDictionary(
                static rule => rule.Id, static rule => new RuntimeEnviroChances(Chance(rule.Value, "STR_FRIENDLY"),
                    Chance(rule.Value, "STR_HOSTILE"), Chance(rule.Value, "STR_NEUTRAL")), StringComparer.Ordinal)),
            Equipment = equipment ?? RuntimeBattleEquipment.Empty,
        };
    }

    private static int Chance(EnviroEffectsRule rule, string faction) =>
        rule.EnvironmentalConditions.TryGetValue(faction, out var condition) ? condition.GlobalChance : 100;

    /// <summary>A craft's or UFO's own terrain, projected like the terrains section's.</summary>
    internal static RuntimeTerrain? Inline(InlineTerrainRule? rule, ResolvedResourceCatalog resources) =>
        rule is null ? null : Terrain(rule.Name, rule.Rule, resources);

    private static RuntimeTerrain Terrain(string id, TerrainRule rule, ResolvedResourceCatalog resources) => new(id, List(rule.MapDataSets),
        List(rule.MapBlocks.Select(static block => new RuntimeMapBlock(block.Name, block.Width, block.Length, block.Height,
            List(block.Groups), List(block.RevealedFloors), List(block.CraftInventoryTile))
        {
            Items = RuntimeMapBlockItems.Read(block.Items, block.FuseTimers, block.RandomizedItems, block.ExtendedItems, out var invalid)
                ?? RuntimeMapBlockItems.None,
            ItemsInvalid = invalid,
        })),
        rule.EnviroEffects, List(rule.CivilianTypes), rule.MinimumDepth, rule.MaximumDepth, rule.MapScript, List(rule.MapScripts))
    {
        Music = List(rule.Music),
        Ambience = Sound(rule.Ambience, resources),
        AmbientVolume = rule.AmbientVolume,
        RandomAmbience = List(rule.RandomAmbience.Select(reference => Sound(reference, resources))),
        MinimumAmbienceDelay = rule.MinimumAmbienceDelay,
        MaximumAmbienceDelay = rule.MaximumAmbienceDelay,
    };

    /// <summary>
    /// Mod::loadSoundOffset into BATTLE.CAT (loadOffsetNode): an index at or above the set's shared sounds is offset into
    /// the declaring mod's range, whether or not the mod supplies that sound; a shared index and -1 (Mod::NO_SOUND) stay.
    /// </summary>
    private static int Sound(RuleIndexReference reference, ResolvedResourceCatalog resources) =>
        reference.Index >= resources.SharedSoundCounts.GetValueOrDefault("BATTLE.CAT")
            ? checked(reference.Index + resources.ModOffsets.GetValueOrDefault(reference.ModId)) : reference.Index;

    private static ReadOnlyCollection<T> List<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());

    private static ReadOnlyDictionary<string, T> Map<T>(IReadOnlyDictionary<string, T> source) =>
        new(source.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
}

/// <summary>RuleTerrain as the battle generator reads it (sprites stay with presentation).</summary>
public sealed record RuntimeTerrain(
    string Id,
    IReadOnlyList<string> MapDataSets,
    IReadOnlyList<RuntimeMapBlock> MapBlocks,
    string EnviroEffects,
    IReadOnlyList<string> CivilianTypes,
    int MinimumDepth,
    int MaximumDepth,
    string MapScript,
    IReadOnlyList<string> MapScripts)
{
    /// <summary>RuleTerrain <c>music</c>: the battle picks one when its deployment has none (BattlescapeGenerator::setMusic).</summary>
    public IReadOnlyList<string> Music { get; init; } = [];

    /// <summary>
    /// RuleTerrain <c>ambience</c> (a BATTLE.CAT sound, -1 for none), <c>ambientVolume</c>, <c>ambienceRandom</c> and
    /// <c>ambienceRandomDelay</c>, which generateMap copies to the battle.
    /// </summary>
    public int Ambience { get; init; } = -1;
    public double AmbientVolume { get; init; } = 0.5;
    public IReadOnlyList<int> RandomAmbience { get; init; } = [];
    public int MinimumAmbienceDelay { get; init; } = 20;
    public int MaximumAmbienceDelay { get; init; } = 60;
}

/// <summary>The environmental conditions' global chances per faction (EnvironmentalCondition::globalChance).</summary>
public readonly record struct RuntimeEnviroChances(int Friendly, int Hostile, int Neutral);

/// <summary>MapBlock: size (the MAP header's height replaces <see cref="Height"/> when loaded), groups and revealed floors.</summary>
public sealed record RuntimeMapBlock(
    string Name,
    int Width,
    int Length,
    int Height,
    IReadOnlyList<int> Groups,
    IReadOnlyList<int> RevealedFloors,
    IReadOnlyList<int> CraftInventoryTile)
{
    /// <summary>The items the block places when it is loaded.</summary>
    public RuntimeMapBlockItems Items { get; init; } = RuntimeMapBlockItems.None;

    /// <summary>Why the block's item nodes are unusable (the reference fails to load them), or null.</summary>
    public string? ItemsInvalid { get; init; }
}

/// <summary>MCDPatch: entries in rule order.</summary>
public sealed record RuntimeMcdPatch(IReadOnlyList<RuntimeMcdPatchEntry> Entries);

/// <summary>One MCDPatch data entry: the integer and boolean properties it sets and its LOFTS, if any.</summary>
public sealed record RuntimeMcdPatchEntry(
    ulong Index,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyList<int>? Lofts);
