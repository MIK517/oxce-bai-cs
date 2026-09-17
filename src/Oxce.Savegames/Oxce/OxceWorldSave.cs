using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Savegames.Oxce;

/// <summary>
/// The strategic world sections of the save: alien missions, UFOs, waypoints, mission sites,
/// alien bases, scheduled events and the alien strategy table. Reference: the corresponding
/// load/save methods in <c>SavedGame.cpp</c>, <c>AlienMission.cpp</c>, <c>Ufo.cpp</c>,
/// <c>MissionSite.cpp</c>, <c>AlienBase.cpp</c>, <c>Waypoint.cpp</c>, <c>GeoscapeEvent.cpp</c>
/// and <c>AlienStrategy.cpp</c> at OXCE commit 4df3a5e.
/// </summary>
public static partial class OxceSaveAdapter
{
    /// <summary>The legacy pre-OXCE terror site type and deployment. Reference: SavedGame::load.</summary>
    private const string LegacyTerrorMission = "STR_ALIEN_TERROR";
    private const string LegacyTerrorDeployment = "STR_TERROR_MISSION";
    /// <summary>The marker name legacy craft destinations used for a terror site. Reference: Craft::load.</summary>
    private const string LegacyTerrorMarker = "STR_TERROR_SITE";

    private static WorldSnapshot ReadWorld(YamlMappingNode body, RuntimeContent content)
    {
        // SavedGame::load imports the legacy terrorSites node as ordinary mission sites, before missionSites.
        var sites = Maps(body, "terrorSites")
            .Select(static map => ReadMissionSite(map) with
            { MissionRuleId = LegacyTerrorMission, DeploymentId = LegacyTerrorDeployment })
            .Concat(Maps(body, "missionSites").Select(ReadMissionSite)).ToArray();
        var alienBases = Maps(body, "alienBases").Select(ReadAlienBase).ToArray();
        var markers = new WorldMarkerIndex(sites, alienBases, content);
        return new WorldSnapshot
        {
            Missions = Array.AsReadOnly(Maps(body, "alienMissions").Select(map => ReadAlienMission(map, markers)).ToArray()),
            Ufos = Array.AsReadOnly(Maps(body, "ufos").Select(map => ReadUfo(map, content, markers)).ToArray()),
            Waypoints = Array.AsReadOnly(Maps(body, "waypoints").Select(static map => new WaypointSnapshot(
                Integer(map, "id", 0), Double(map, "lon", 0), Double(map, "lat", 0))
            { Name = String(map, "name", string.Empty) }).ToArray()),
            MissionSites = Array.AsReadOnly(sites),
            AlienBases = Array.AsReadOnly(alienBases),
            Events = Array.AsReadOnly(Maps(body, "geoscapeEvents").Select(static map => new GeoscapeEventSnapshot(
                RequiredString(map, "name"), Integer(map, "spawnCountdown", 0))
            { Over = Boolean(map, "over", false) }).ToArray()),
            Strategy = ReadAlienStrategy(body),
        };
    }

    private static MissionSiteSnapshot ReadMissionSite(YamlMappingNode map) => new(
        Integer(map, "id", 0),
        String(map, "type", LegacyTerrorMission),
        String(map, "deployment", "STR_TERROR_MISSION"),
        String(map, "race", string.Empty),
        Double(map, "lon", 0),
        Double(map, "lat", 0),
        Integer(map, "secondsRemaining", 0))
    {
        Name = String(map, "name", string.Empty),
        CustomDeploymentId = String(map, "missionCustomDeploy", string.Empty),
        Texture = Integer(map, "texture", -1),
        City = String(map, "city", string.Empty),
        Detected = Boolean(map, "detected", false),
        InBattlescape = Boolean(map, "inBattlescape", false),
        UfoUniqueId = Integer(map, "ufoUniqueId", -1) > 0 ? Integer(map, "ufoUniqueId", -1) : 0,
    };

    private static AlienBaseSnapshot ReadAlienBase(YamlMappingNode map) => new(
        Integer(map, "id", 0),
        String(map, "deployment", "STR_ALIEN_BASE_ASSAULT"),
        String(map, "race", string.Empty),
        Double(map, "lon", 0),
        Double(map, "lat", 0))
    {
        Name = String(map, "name", string.Empty),
        Discovered = Boolean(map, "discovered", false),
        InBattlescape = Boolean(map, "inBattlescape", false),
        StartMonth = Integer(map, "startMonth", 0),
        GenMissionCount = Integer(map, "genMissionCount", 0),
        MinutesSinceLastHuntMissionGeneration = Integer(map, "minutesSinceLastHuntMissionGeneration", 0),
        PactCountryId = String(map, "pactCountry", string.Empty),
    };

    private static AlienMissionSnapshot ReadAlienMission(YamlMappingNode map, WorldMarkerIndex markers) => new(
        Integer(map, "uniqueID", 0),
        RequiredString(map, "type"),
        String(map, "region", string.Empty),
        String(map, "race", string.Empty),
        Integer(map, "nextWave", 0),
        Integer(map, "nextUfoCounter", 0),
        Integer(map, "spawnCountdown", 0),
        Integer(map, "liveUfos", 0),
        Integer(map, "missionSiteZone", -1))
    {
        Interrupted = Boolean(map, "interrupted", false),
        MultiUfoRetaliationInProgress = Boolean(map, "multiUfoRetaliationInProgress", false),
        AlienBase = ReadAlienBaseReference(map, markers),
    };

    /// <summary>AlienMission::load accepts both the legacy scalar id and the current map form.</summary>
    private static WorldTargetReference? ReadAlienBaseReference(YamlMappingNode map, WorldMarkerIndex markers)
    {
        if (!map.TryGet("alienBase", out var node)) return null;
        var (id, type) = node switch
        {
            YamlMappingNode mapping => (Integer(mapping, "id", 0), String(mapping, "type", "STR_ALIEN_BASE")),
            _ => (YamlValueReader.ReadInt32(node!), "STR_ALIEN_BASE"),
        };
        var target = markers.AlienBase(type, id);
        return target ?? new WorldTargetReference(WorldTargetKind.AlienBase, type, id, 0, 0);
    }

    private static UfoSnapshot ReadUfo(YamlMappingNode map, RuntimeContent content, WorldMarkerIndex markers)
    {
        var longitude = Double(map, "lon", 0);
        var latitude = Double(map, "lat", 0);
        return new UfoSnapshot(
            Integer(map, "uniqueId", 0),
            RequiredString(map, "type"),
            Integer(map, "mission", 0),
            String(map, "trajectory", string.Empty),
            Integer(map, "trajectoryPoint", 0),
            longitude,
            latitude,
            (UfoStatus)Integer(map, "status", 0),
            String(map, "altitude", "STR_HIGH_UC"))
        {
            Id = Integer(map, "id", 0),
            LandId = Integer(map, "landId", 0),
            CrashId = Integer(map, "crashId", 0),
            MissionWaveNumber = Integer(map, "missionWaveNumber", -1),
            Direction = String(map, "direction", "STR_NORTH"),
            Damage = Integer(map, "damage", 0),
            Shield = Integer(map, "shield", -1),
            ShieldRechargeHandle = Integer(map, "shieldRechargeHandle", 0),
            SecondsRemaining = Integer(map, "secondsRemaining", 0),
            Detected = Boolean(map, "detected", false),
            HyperDetected = Boolean(map, "hyperDetected", false),
            InBattlescape = Boolean(map, "inBattlescape", false),
            HunterKiller = Boolean(map, "isHunterKiller", false),
            Escort = Boolean(map, "isEscort", false),
            HuntMode = Integer(map, "huntMode", 0),
            HuntBehavior = Integer(map, "huntBehavior", 0),
            Hunting = Boolean(map, "isHunting", false),
            Escorting = Boolean(map, "isEscorting", false),
            SoftlockShotCounter = Integer(map, "softlockShotCounter", 0),
            FireCountdown = Integer(map, "fireCountdown", 0),
            EscapeCountdown = Integer(map, "escapeCountdown", 0),
            Speed = Integer(map, "speed", 0),
            SpeedLongitude = Double(map, "speedLon", 0),
            SpeedLatitude = Double(map, "speedLat", 0),
            SpeedRadian = Double(map, "speedRadian", 0),
            // Ufo::load always builds a destination waypoint, falling back to its own position.
            Destination = ReadTargetReference(map, markers, longitude, latitude) ??
                new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0,
                    longitude, latitude),
            OriginalWaypoint = map.TryGet("origWaypoint", out var origin) && origin is YamlMappingNode waypoint
                ? new WorldPosition(Double(waypoint, "lon", 0), Double(waypoint, "lat", 0))
                : null,
            ScriptValues = ReadScriptValues(map, content, "Ufo"),
        };
    }

    /// <summary>Reads a <c>Target::saveId</c> node and classifies it by its external type name.</summary>
    private static WorldTargetReference? ReadTargetReference(
        YamlMappingNode owner, WorldMarkerIndex markers, double fallbackLongitude, double fallbackLatitude)
    {
        if (!owner.TryGet("dest", out var node) || node is not YamlMappingNode map) return null;
        var type = String(map, "type", WorldTargetReference.WaypointType);
        var id = Integer(map, "id", 0);
        var longitude = Double(map, "lon", fallbackLongitude);
        var latitude = Double(map, "lat", fallbackLatitude);
        var uniqueId = Integer(map, "uniqueId", 0);
        var kind = type switch
        {
            WorldTargetReference.BaseType => WorldTargetKind.Base,
            WorldTargetReference.UfoType => WorldTargetKind.Ufo,
            WorldTargetReference.WaypointType => WorldTargetKind.Waypoint,
            _ => markers.Classify(ref type, id),
        };
        return new WorldTargetReference(kind, type, id, longitude, latitude) { UniqueId = uniqueId };
    }

    private static AlienStrategySnapshot ReadAlienStrategy(YamlMappingNode body)
    {
        if (!body.TryGet("alienStrategy", out var node) || node is not YamlMappingNode strategy)
            return AlienStrategySnapshot.Empty;
        var missions = new List<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, ulong>>>>();
        foreach (var entry in Maps(strategy, "possibleMissions"))
            missions.Add(new(RequiredString(entry, "region"), ReadWeights(entry, "missions")));
        return new AlienStrategySnapshot
        {
            RegionChances = ReadWeights(strategy, "regions"),
            RegionMissions = Array.AsReadOnly(missions.ToArray()),
            MissionRuns = strategy.TryGet("missionsRun", out var runs) && runs is YamlMappingNode runMap
                ? Array.AsReadOnly(runMap.Entries.Select(static entry => new KeyValuePair<string, int>(
                    entry.ScalarKey ?? string.Empty, YamlValueReader.ReadInt32(entry.Value))).ToArray())
                : [],
            MissionLocations = ReadMissionLocations(strategy),
        };
    }

    private static ReadOnlyCollection<KeyValuePair<string, ulong>> ReadWeights(YamlMappingNode owner, string key) =>
        owner.TryGet(key, out var node) && node is YamlMappingNode map
            ? Array.AsReadOnly(map.Entries.Select(static entry => new KeyValuePair<string, ulong>(
                entry.ScalarKey ?? string.Empty, YamlValueReader.ReadUInt64(entry.Value))).ToArray())
            : ReadOnlyCollection<KeyValuePair<string, ulong>>.Empty;

    private static ReadOnlyCollection<KeyValuePair<string, IReadOnlyList<AlienStrategyState.MissionLocation>>>
        ReadMissionLocations(YamlMappingNode strategy)
    {
        if (!strategy.TryGet("missionLocations", out var node) || node is not YamlMappingNode map)
            return ReadOnlyCollection<KeyValuePair<string, IReadOnlyList<AlienStrategyState.MissionLocation>>>.Empty;
        var result = new List<KeyValuePair<string, IReadOnlyList<AlienStrategyState.MissionLocation>>>();
        foreach (var entry in map.Entries)
        {
            var locations = new List<AlienStrategyState.MissionLocation>();
            if (entry.Value is YamlSequenceNode sequence)
            {
                foreach (var item in sequence.Items)
                {
                    if (item is not YamlSequenceNode pair || pair.Items.Count < 2)
                        throw new InvalidDataException("A mission location must be a [region, zone] pair.");
                    locations.Add(new AlienStrategyState.MissionLocation(
                        YamlValueReader.ReadString(pair.Items[0]), YamlValueReader.ReadInt32(pair.Items[1])));
                }
            }
            result.Add(new(entry.ScalarKey ?? string.Empty, Array.AsReadOnly(locations.ToArray())));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    /// <summary>Marker-name lookup for the target references that only record a type and an id.</summary>
    private sealed class WorldMarkerIndex
    {
        private readonly HashSet<(string Marker, int Id)> _sites = [];
        private readonly Dictionary<(string Marker, int Id), (double Longitude, double Latitude, string Type)> _bases = [];
        private readonly HashSet<string> _craftTypes;

        public WorldMarkerIndex(
            IEnumerable<MissionSiteSnapshot> sites,
            IEnumerable<AlienBaseSnapshot> alienBases,
            RuntimeContent content)
        {
            foreach (var site in sites) _sites.Add((MarkerName(content, site.DeploymentId), site.Id));
            foreach (var alienBase in alienBases)
                _bases[(MarkerName(content, alienBase.DeploymentId), alienBase.Id)] =
                    (alienBase.Longitude, alienBase.Latitude, alienBase.DeploymentId);
            _craftTypes = content.RuntimeRules.Crafts.Rules.Select(static rule => rule.Id)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// Classifies a destination that is neither a base, a UFO nor a waypoint, in the order the
        /// reference resolves them: an escorted or hunted craft (<c>Craft::finishLoading</c>,
        /// <c>Ufo::finishLoading</c>), then a mission site, then an alien base
        /// (<c>Craft::load</c>). The legacy <c>STR_ALIEN_TERROR</c> marker is renamed to
        /// <c>STR_TERROR_SITE</c> exactly like <c>Craft::load</c> does.
        /// </summary>
        public WorldTargetKind Classify(ref string type, int id)
        {
            if (_craftTypes.Contains(type)) return WorldTargetKind.Craft;
            if (type == LegacyTerrorMission) type = LegacyTerrorMarker;
            if (_sites.Contains((type, id))) return WorldTargetKind.MissionSite;
            return _bases.ContainsKey((type, id)) ? WorldTargetKind.AlienBase : WorldTargetKind.MissionSite;
        }

        public WorldTargetReference? AlienBase(string type, int id) =>
            _bases.TryGetValue((type, id), out var found)
                ? new WorldTargetReference(WorldTargetKind.AlienBase, type, id, found.Longitude, found.Latitude)
                : null;

        private static string MarkerName(RuntimeContent content, string deploymentId) =>
            content.RuntimeRules.AlienDeployments.TryGet(deploymentId, out var handle)
                ? content.RuntimeRules.AlienDeployments[handle].Value.MarkerName
                : deploymentId;
    }

    private static YamlSequenceNode? BuildWorldSection(int count, IEnumerable<YamlNode> values) =>
        count == 0 ? null : Sequence(values);

    private static YamlMappingNode BuildAlienMission(AlienMissionSnapshot mission, YamlMappingNode? source) =>
        Overlay(source,
        [
            Pair("type", Scalar(mission.RuleId)),
            Pair("region", Scalar(mission.RegionId)),
            Pair("race", Scalar(mission.Race)),
            Pair("nextWave", Integer(mission.NextWave)),
            Pair("nextUfoCounter", Integer(mission.NextUfoCounter)),
            Pair("spawnCountdown", Integer(mission.SpawnCountdown)),
            Pair("liveUfos", Integer(mission.LiveUfos)),
            Pair("interrupted", mission.Interrupted ? Boolean(true) : null),
            Pair("multiUfoRetaliationInProgress", mission.MultiUfoRetaliationInProgress ? Boolean(true) : null),
            Pair("uniqueID", Integer(mission.Id)),
            Pair("alienBase", mission.AlienBase is { } alienBase
                ? Mapping([Pair("lon", Real(alienBase.Longitude)), Pair("lat", Real(alienBase.Latitude)),
                    Pair("type", Scalar(alienBase.TypeId)), Pair("id", Integer(alienBase.Id))])
                : null),
            Pair("missionSiteZone", Integer(mission.MissionSiteZoneArea)),
        ]);

    private static YamlMappingNode BuildUfo(UfoSnapshot ufo, YamlMappingNode? source) => Overlay(source,
    [
        Pair("lon", Real(ufo.Longitude)), Pair("lat", Real(ufo.Latitude)),
        Pair("id", ufo.Id == 0 ? null : Integer(ufo.Id)),
        Pair("dest", TargetReference(ufo.Destination)),
        Pair("speedLon", Real(ufo.SpeedLongitude)), Pair("speedLat", Real(ufo.SpeedLatitude)),
        Pair("speedRadian", Real(ufo.SpeedRadian)), Pair("speed", Integer(ufo.Speed)),
        Pair("type", Scalar(ufo.RuleId)),
        Pair("uniqueId", Integer(ufo.UniqueId)),
        Pair("missionWaveNumber", Integer(ufo.MissionWaveNumber)),
        Pair("crashId", ufo.CrashId == 0 ? null : Integer(ufo.CrashId)),
        Pair("landId", ufo.CrashId == 0 && ufo.LandId != 0 ? Integer(ufo.LandId) : null),
        Pair("damage", Integer(ufo.Damage)),
        Pair("shield", Integer(ufo.Shield)),
        Pair("shieldRechargeHandle", Integer(ufo.ShieldRechargeHandle)),
        Pair("altitude", Scalar(ufo.Altitude)),
        Pair("direction", Scalar(ufo.Direction)),
        Pair("status", Integer((int)ufo.Status)),
        Pair("detected", ufo.Detected ? Boolean(true) : null),
        Pair("hyperDetected", ufo.HyperDetected ? Boolean(true) : null),
        Pair("secondsRemaining", ufo.SecondsRemaining == 0 ? null : Integer(ufo.SecondsRemaining)),
        Pair("inBattlescape", ufo.InBattlescape ? Boolean(true) : null),
        Pair("isHunterKiller", ufo.HunterKiller ? Boolean(true) : null),
        Pair("isEscort", ufo.Escort ? Boolean(true) : null),
        Pair("huntMode", Integer(ufo.HuntMode)),
        Pair("huntBehavior", Integer(ufo.HuntBehavior)),
        Pair("isHunting", ufo.Hunting ? Boolean(true) : null),
        Pair("isEscorting", ufo.Escorting ? Boolean(true) : null),
        Pair("softlockShotCounter", ufo.SoftlockShotCounter == 0 ? null : Integer(ufo.SoftlockShotCounter)),
        Pair("origWaypoint", ufo.OriginalWaypoint is { } waypoint
            ? Mapping([Pair("lon", Real(waypoint.Longitude)), Pair("lat", Real(waypoint.Latitude))]) : null),
        Pair("mission", Integer(ufo.MissionId)),
        Pair("trajectory", Scalar(ufo.TrajectoryId)),
        Pair("trajectoryPoint", Integer(ufo.TrajectoryPoint)),
        Pair("fireCountdown", Integer(ufo.FireCountdown)),
        Pair("escapeCountdown", Integer(ufo.EscapeCountdown)),
        Pair("tags", ScriptValues(ufo.ScriptValues)),
    ]);

    private static YamlMappingNode BuildMissionSite(MissionSiteSnapshot site, YamlMappingNode? source) => Overlay(source,
    [
        Pair("lon", Real(site.Longitude)), Pair("lat", Real(site.Latitude)),
        Pair("id", Integer(site.Id)),
        Pair("name", site.Name.Length == 0 ? null : Scalar(site.Name)),
        Pair("type", Scalar(site.MissionRuleId)),
        Pair("deployment", Scalar(site.DeploymentId)),
        Pair("missionCustomDeploy", site.CustomDeploymentId.Length == 0 ? null : Scalar(site.CustomDeploymentId)),
        Pair("texture", Integer(site.Texture)),
        Pair("secondsRemaining", site.SecondsRemaining == 0 ? null : Integer(site.SecondsRemaining)),
        Pair("race", Scalar(site.Race)),
        Pair("city", site.City.Length == 0 ? null : Scalar(site.City)),
        Pair("inBattlescape", site.InBattlescape ? Boolean(true) : null),
        Pair("detected", Boolean(site.Detected)),
        Pair("ufoUniqueId", site.UfoUniqueId == 0 ? null : Integer(site.UfoUniqueId)),
    ]);

    private static YamlMappingNode BuildAlienBase(AlienBaseSnapshot alienBase, YamlMappingNode? source) => Overlay(source,
    [
        Pair("lon", Real(alienBase.Longitude)), Pair("lat", Real(alienBase.Latitude)),
        Pair("id", Integer(alienBase.Id)),
        Pair("name", alienBase.Name.Length == 0 ? null : Scalar(alienBase.Name)),
        Pair("pactCountry", Scalar(alienBase.PactCountryId)),
        Pair("race", Scalar(alienBase.Race)),
        Pair("inBattlescape", alienBase.InBattlescape ? Boolean(true) : null),
        Pair("discovered", alienBase.Discovered ? Boolean(true) : null),
        Pair("deployment", Scalar(alienBase.DeploymentId)),
        Pair("startMonth", Integer(alienBase.StartMonth)),
        Pair("minutesSinceLastHuntMissionGeneration", Integer(alienBase.MinutesSinceLastHuntMissionGeneration)),
        Pair("genMissionCount", Integer(alienBase.GenMissionCount)),
    ]);

    private static YamlMappingNode? BuildAlienStrategy(AlienStrategySnapshot strategy) => strategy.IsEmpty ? null : Mapping(
    [
        Pair("regions", WeightMapping(strategy.RegionChances)),
        Pair("possibleMissions", strategy.RegionMissions.Count == 0 ? null : Sequence(strategy.RegionMissions.Select(
            static region => Mapping(
            [
                Pair("region", Scalar(region.Key)),
                // WeightedOptions::save writes null for an empty table; the port keeps the key.
                Pair("missions", region.Value.Count == 0 ? Scalar("~") : WeightMapping(region.Value)),
            ])))),
        Pair("missionsRun", strategy.MissionRuns.Count == 0 ? null : IntMapping(strategy.MissionRuns)),
        Pair("missionLocations", strategy.MissionLocations.Count == 0 ? null : Mapping(
            strategy.MissionLocations.Select(static entry => Pair(entry.Key, (YamlNode?)Sequence(
                entry.Value.Select(static location => Sequence(
                    [Scalar(location.Region), Integer(location.Zone)]))))))),
    ]);

    private static YamlMappingNode? WeightMapping(IReadOnlyList<KeyValuePair<string, ulong>> weights) =>
        weights.Count == 0 ? null : Mapping(weights.Select(static weight => Pair(weight.Key, (YamlNode?)ULong(weight.Value))));

    private static YamlMappingNode? TargetReference(WorldTargetReference? reference) => reference is null ? null : Mapping(
    [
        Pair("lon", Real(reference.Longitude)), Pair("lat", Real(reference.Latitude)),
        Pair("type", Scalar(reference.TypeId)),
        Pair("id", Integer(reference.Id)),
        Pair("uniqueId", reference.UniqueId == 0 ? null : Integer(reference.UniqueId)),
    ]);
}
