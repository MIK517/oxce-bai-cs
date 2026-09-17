using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The alien strategy table: which regions are still worth attacking, which missions remain
/// available there, how often each mission-script variable has run, and which locations were
/// used recently. Reference: <c>Savegame/AlienStrategy.cpp</c>.
/// </summary>
public sealed class AlienStrategyState
{
    private readonly SortedDictionary<string, WorldWeightedOptions> _regionMissions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _missionRuns = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, List<MissionLocation>> _missionLocations = new(StringComparer.Ordinal);
    private WorldWeightedOptions _regionChances = new();

    public readonly record struct MissionLocation(string Region, int Zone);

    public WorldWeightedOptions RegionChances => _regionChances;
    public IReadOnlyDictionary<string, WorldWeightedOptions> RegionMissions => _regionMissions;
    public IReadOnlyDictionary<string, int> MissionRuns => _missionRuns;
    public IReadOnlyDictionary<string, List<MissionLocation>> MissionLocations => _missionLocations;

    /// <summary>AlienStrategy::init.</summary>
    public void Initialize(IEnumerable<(string Region, ulong Weight, IEnumerable<KeyValuePair<string, ulong>> Missions)> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        _regionChances = new WorldWeightedOptions();
        _regionMissions.Clear();
        foreach (var (region, weight, missions) in regions)
        {
            _regionChances.Set(region, weight);
            _regionMissions[region] = new WorldWeightedOptions(missions);
        }
    }

    /// <summary>AlienStrategy::chooseRandomRegion, including the refresh when the table is exhausted.</summary>
    public string ChooseRandomRegion(
        IRandomSource random,
        IEnumerable<(string Region, ulong Weight, IEnumerable<KeyValuePair<string, ulong>> Missions)> regions)
    {
        ArgumentNullException.ThrowIfNull(random);
        var chosen = _regionChances.Choose(random);
        if (chosen.Length != 0) return chosen;
        _regionMissions.Clear();
        Initialize(regions);
        return _regionChances.Choose(random);
    }

    /// <summary>AlienStrategy::chooseRandomMission.</summary>
    public string ChooseRandomMission(string region, IRandomSource random)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentNullException.ThrowIfNull(random);
        return _regionMissions.TryGetValue(region, out var missions) ? missions.Choose(random) : string.Empty;
    }

    /// <summary>AlienStrategy::removeMission; an emptied region also loses its weight.</summary>
    public void RemoveMission(string region, string mission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(mission);
        if (!_regionMissions.TryGetValue(region, out var missions)) return;
        missions.Set(mission, 0);
        if (!missions.IsEmpty) return;
        _regionMissions.Remove(region);
        _regionChances.Set(region, 0);
    }

    /// <summary>AlienStrategy::validMissionRegion.</summary>
    public bool ValidMissionRegion(string region) => _regionMissions.ContainsKey(region);

    /// <summary>AlienStrategy::getMissionsRun.</summary>
    public int MissionsRun(string variableName) => _missionRuns.GetValueOrDefault(variableName);

    /// <summary>AlienStrategy::addMissionRun; an empty variable name is ignored.</summary>
    public void AddMissionRun(string variableName, int increment = 1)
    {
        if (string.IsNullOrEmpty(variableName)) return;
        _missionRuns[variableName] = checked(_missionRuns.GetValueOrDefault(variableName) + increment);
    }

    /// <summary>
    /// AlienStrategy::addMissionLocation. The reference trims the first entry of the whole
    /// table - not of the overflowing list - once a variable exceeds its repeat avoidance,
    /// so a busy variable can drop another variable's history. That behavior is preserved.
    /// </summary>
    public void AddMissionLocation(string variableName, string region, int zone, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        if (maximum <= 0) return;
        if (!_missionLocations.TryGetValue(variableName, out var locations))
        {
            locations = [];
            _missionLocations.Add(variableName, locations);
        }
        locations.Add(new MissionLocation(region, zone));
        if (locations.Count > maximum) _missionLocations.Remove(_missionLocations.Keys.First());
    }

    /// <summary>AlienStrategy::validMissionLocation.</summary>
    public bool ValidMissionLocation(string variableName, string region, int zone) =>
        !_missionLocations.TryGetValue(variableName, out var locations) ||
        !locations.Contains(new MissionLocation(region, zone));

    /// <summary>Restores the saved table; missing regions are dropped like <c>AlienStrategy::load</c>.</summary>
    public static AlienStrategyState Restore(
        IEnumerable<KeyValuePair<string, ulong>> regionChances,
        IEnumerable<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, ulong>>>> regionMissions,
        IEnumerable<KeyValuePair<string, int>> missionRuns,
        IEnumerable<KeyValuePair<string, IReadOnlyList<MissionLocation>>> missionLocations,
        Func<string, bool> regionExists)
    {
        ArgumentNullException.ThrowIfNull(regionChances);
        ArgumentNullException.ThrowIfNull(regionMissions);
        ArgumentNullException.ThrowIfNull(missionRuns);
        ArgumentNullException.ThrowIfNull(missionLocations);
        ArgumentNullException.ThrowIfNull(regionExists);
        var state = new AlienStrategyState();
        foreach (var entry in regionChances) state._regionChances.Set(entry.Key, entry.Value);
        foreach (var entry in regionMissions)
        {
            if (!regionExists(entry.Key)) continue;
            state._regionMissions[entry.Key] = new WorldWeightedOptions(entry.Value);
        }
        foreach (var entry in missionRuns) state._missionRuns[entry.Key] = entry.Value;
        foreach (var entry in missionLocations)
        {
            foreach (var location in entry.Value) ArgumentException.ThrowIfNullOrWhiteSpace(location.Region);
            state._missionLocations[entry.Key] = [.. entry.Value];
        }
        return state;
    }
}
