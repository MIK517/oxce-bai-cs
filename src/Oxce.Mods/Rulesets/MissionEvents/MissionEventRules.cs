using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.MissionEvents;

public sealed record TrajectoryWaypointRule(int Zone, int Altitude, int Speed);
public sealed record UfoTrajectoryRule(int GroundTimer, IReadOnlyList<TrajectoryWaypointRule> Waypoints);

public sealed record MissionWaveRule(string Ufo, ulong Count, string Trajectory, ulong Timer,
    bool Objective, bool ObjectiveOnTheLandingSite, bool ObjectiveOnXcomBase,
    int HunterKillerPercentage, int HuntMode, int HuntBehavior, bool Escort, int InterruptPercentage);

public sealed record WeightedTimelineEntry(ulong Month, IReadOnlyDictionary<string, ulong> Weights);

public sealed record AlienMissionRule(IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans, IReadOnlyDictionary<string, string> Strings,
    IReadOnlyList<MissionWaveRule> Waves, IReadOnlyDictionary<ulong, int> MissionWeights,
    IReadOnlyList<WeightedTimelineEntry> RaceWeights, IReadOnlyList<WeightedTimelineEntry> RegionWeights);

public sealed record StrategicScriptRule(IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, long> Longs, IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyDictionary<string, string> Strings, IReadOnlyList<string> Sequential,
    IReadOnlyDictionary<string, ulong> Random, IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> Triggers,
    IReadOnlyList<int> Conditionals, IReadOnlyList<string> Tags,
    IReadOnlyList<WeightedTimelineEntry> MissionWeights, IReadOnlyList<WeightedTimelineEntry> RaceWeights,
    IReadOnlyList<WeightedTimelineEntry> RegionWeights, IReadOnlyList<WeightedTimelineEntry> EventWeights);

public sealed record EventRule(IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers, IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyList<string> Regions, IReadOnlyDictionary<string, int> EveryMultiItems,
    IReadOnlyList<string> EveryItems, IReadOnlyList<string> RandomItems,
    IReadOnlyList<IReadOnlyDictionary<string, int>> RandomMultiItems,
    IReadOnlyDictionary<string, ulong> WeightedItems, IReadOnlyList<string> Research,
    IReadOnlyList<string> AdhocMissionScriptTags, IReadOnlyDictionary<string, int> EveryMultiSoldiers,
    IReadOnlyList<IReadOnlyDictionary<string, int>> RandomMultiSoldiers, YamlMappingNode? SpawnedSoldier);

public sealed record UfopaediaPageRule(string Title, string Text, int AmmoSlot);
public sealed record UfopaediaArticleRule(int TypeId, string Section, IReadOnlyList<string> Requires,
    IReadOnlyList<string> DisabledBy, bool HiddenCommendation, int ListOrder,
    IReadOnlyList<UfopaediaPageRule> Pages, IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers, IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyDictionary<string, YamlNode> StructuredProperties, bool CustomPalette,
    RuleOperationSource LastUpdateSource);

internal static class MissionReadOnly
{
    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IDictionary<TKey, TValue> value)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(value));
}

internal sealed class TrajectoryBuilder { public int GroundTimer = 5; public List<TrajectoryWaypointRule> Waypoints = []; }

internal sealed class AlienMissionBuilder
{
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    { ["points"] = 0, ["objective"] = 0, ["spawnZone"] = -1, ["retaliationOdds"] = -1, ["operationType"] = 0, ["operationSpawnZone"] = -1, ["targetBaseOdds"] = 0 };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    {
        ["skipScoutingPhase"] = false,
        ["endlessInfiltration"] = true,
        ["multiUfoRetaliation"] = false,
        ["multiUfoRetaliationExtra"] = false,
        ["ignoreBaseDefenses"] = false,
        ["instaHyper"] = false,
        ["despawnEvenIfTargeted"] = false,
        ["respawnUfoAfterSiteDespawn"] = false,
        ["showAlienBase"] = false
    };
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    { ["spawnUfo"] = "", ["interruptResearch"] = "", ["siteType"] = "", ["operationBaseType"] = "" };
    public List<MissionWaveRule> Waves = [];
    public Dictionary<ulong, int> MissionWeights = [];
    public SortedDictionary<ulong, Dictionary<string, ulong>> RaceWeights = [];
    public List<WeightedTimelineEntry> RegionWeights = [];
}

internal sealed class StrategicScriptBuilder
{
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Longs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);
    public List<string> Sequential = [];
    public Dictionary<string, ulong> Random = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, bool>> Triggers { get; } = TriggerKeys.ToDictionary(k => k, _ => new Dictionary<string, bool>(StringComparer.Ordinal), StringComparer.Ordinal);
    public List<int> Conditionals = [];
    public List<string> Tags = [];
    public List<WeightedTimelineEntry> MissionWeights = [], RaceWeights = [], RegionWeights = [], EventWeights = [];
    public static readonly string[] TriggerKeys = ["researchTriggers", "itemTriggers", "facilityTriggers", "soldierTypeTriggers", "xcomBaseInRegionTriggers", "xcomBaseInCountryTriggers", "pactCountryTriggers"];
}

internal sealed class EventBuilder
{
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal)
    { ["description"] = "", ["background"] = "BACK13.SCR", ["music"] = "", ["cutscene"] = "", ["spawnedCraftType"] = "", ["spawnedPersonType"] = "", ["spawnedPersonName"] = "", ["interruptResearch"] = "" };
    public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal)
    { ["points"] = 0, ["funds"] = 0, ["spawnedPersons"] = 0, ["timer"] = 30, ["timerRandom"] = 0 };
    public Dictionary<string, bool> Booleans { get; } = new(StringComparer.Ordinal)
    { ["alignBottom"] = false, ["city"] = false, ["invert"] = false };
    public List<string> Regions = [], EveryItems = [], RandomItems = [], Research = [], Tags = [];
    public Dictionary<string, int> EveryMultiItems = new(StringComparer.Ordinal), EveryMultiSoldiers = new(StringComparer.Ordinal);
    public List<Dictionary<string, int>> RandomMultiItems = [], RandomMultiSoldiers = [];
    public Dictionary<string, ulong> WeightedItems = new(StringComparer.Ordinal);
    public YamlMappingNode? SpawnedSoldier;
}
