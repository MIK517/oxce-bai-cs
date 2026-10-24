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
            .Select(static map => ReadMissionSite(map, legacy: true))
            .Concat(Maps(body, "missionSites").Select(static map => ReadMissionSite(map, legacy: false))).ToArray();
        var alienBases = Maps(body, "alienBases").Select(ReadAlienBase).ToArray();
        var markers = new WorldMarkerIndex(sites, alienBases, content, body);
        return new WorldSnapshot
        {
            Missions = Array.AsReadOnly(Maps(body, "alienMissions").Select(map => ReadAlienMission(map, markers)).ToArray()),
            Ufos = Array.AsReadOnly(Maps(body, "ufos").Select(map => ReadUfo(map, content, markers)).ToArray()),
            Waypoints = Array.AsReadOnly(Maps(body, "waypoints").Select(static map => new WaypointSnapshot(
                map.ReadInt32("id", 0), map.ReadDouble("lon", 0), map.ReadDouble("lat", 0))
            { Name = map.ReadString("name", string.Empty) }).ToArray()),
            MissionSites = Array.AsReadOnly(sites),
            AlienBases = Array.AsReadOnly(alienBases),
            AlienBaseBirthSequence = body.ReadUInt64("oxcePortAlienBaseBirthSequence", 0),
            Events = Array.AsReadOnly(Maps(body, "geoscapeEvents").Select(static (map, index) => new GeoscapeEventSnapshot(
                RequiredString(map, "name"), map.ReadInt32("spawnCountdown", 0))
            {
                Over = map.ReadBoolean("over", false),
                PreservationKey = map.ReadString("oxcePortEntityKey", $"event:legacy:{index}"),
            }).ToArray()),
            Strategy = ReadAlienStrategy(body),
            GeneratedEvents = ReadIntMap(body, "generatedEvents"),
            CraftLostDogfight = body.ReadInt32("craftLostDogfight", 0),
            DeploymentRequest = body.TryGet("oxcePortDeploymentRequest", out var request) && request is YamlMappingNode requestMap
                ? ReadDeploymentRequest(requestMap, markers) : null,
        };
    }

    private static MissionSiteSnapshot ReadMissionSite(YamlMappingNode map, bool legacy) => new(
        map.ReadInt32("id", 0),
        legacy ? LegacyTerrorMission : RequiredString(map, "type"),
        legacy ? LegacyTerrorDeployment : map.ReadString("deployment", LegacyTerrorDeployment),
        map.ReadString("race", string.Empty),
        map.ReadDouble("lon", 0),
        map.ReadDouble("lat", 0),
        map.ReadInt32("secondsRemaining", 0))
    {
        Name = map.ReadString("name", string.Empty),
        CustomDeploymentId = map.ReadString("missionCustomDeploy", string.Empty),
        Texture = map.ReadInt32("texture", -1),
        City = map.ReadString("city", string.Empty),
        Detected = map.ReadBoolean("detected", false),
        InBattlescape = map.ReadBoolean("inBattlescape", false),
        UfoUniqueId = map.ReadInt32("ufoUniqueId", -1) > 0 ? map.ReadInt32("ufoUniqueId", -1) : 0,
    };

    private static AlienBaseSnapshot ReadAlienBase(YamlMappingNode map) => new(
        map.ReadInt32("id", 0),
        map.ReadString("deployment", "STR_ALIEN_BASE_ASSAULT"),
        map.ReadString("race", string.Empty),
        map.ReadDouble("lon", 0),
        map.ReadDouble("lat", 0))
    {
        PreservationKey = map.ReadString("oxcePortEntityKey", AlienBasePreservationKey(map)),
        Name = map.ReadString("name", string.Empty),
        Discovered = map.ReadBoolean("discovered", false),
        InBattlescape = map.ReadBoolean("inBattlescape", false),
        StartMonth = map.ReadInt32("startMonth", 0),
        GenMissionCount = map.ReadInt32("genMissionCount", 0),
        MinutesSinceLastHuntMissionGeneration = map.ReadInt32("minutesSinceLastHuntMissionGeneration", 0),
        PactCountryId = map.ReadString("pactCountry", string.Empty),
    };

    private static AlienMissionSnapshot ReadAlienMission(YamlMappingNode map, WorldMarkerIndex markers) => new(
        map.ReadInt32("uniqueID", 0),
        RequiredString(map, "type"),
        map.ReadString("region", string.Empty),
        map.ReadString("race", string.Empty),
        map.ReadInt32("nextWave", 0),
        map.ReadInt32("nextUfoCounter", 0),
        map.ReadInt32("spawnCountdown", 0),
        map.ReadInt32("liveUfos", 0),
        map.ReadInt32("missionSiteZone", -1))
    {
        Interrupted = map.ReadBoolean("interrupted", false),
        MultiUfoRetaliationInProgress = map.ReadBoolean("multiUfoRetaliationInProgress", false),
        AlienBase = ReadAlienBaseReference(map, markers),
    };

    /// <summary>AlienMission::load accepts both the legacy scalar id and the current map form.</summary>
    private static WorldTargetReference? ReadAlienBaseReference(YamlMappingNode map, WorldMarkerIndex markers)
    {
        if (!map.TryGet("alienBase", out var node)) return null;
        var (id, type) = node switch
        {
            YamlMappingNode mapping => (mapping.ReadInt32("id", 0), mapping.ReadString("type", "STR_ALIEN_BASE")),
            _ => (YamlValueReader.ReadInt32(node!), "STR_ALIEN_BASE"),
        };
        var target = markers.AlienBase(type, id);
        return target ?? new WorldTargetReference(WorldTargetKind.AlienBase, type, id, 0, 0);
    }

    private static UfoSnapshot ReadUfo(YamlMappingNode map, RuntimeContent content, WorldMarkerIndex markers)
    {
        var longitude = map.ReadDouble("lon", 0);
        var latitude = map.ReadDouble("lat", 0);
        return new UfoSnapshot(
            map.ReadInt32("uniqueId", 0),
            RequiredString(map, "type"),
            map.ReadInt32("mission", 0),
            map.ReadString("trajectory", string.Empty),
            map.ReadInt32("trajectoryPoint", 0),
            longitude,
            latitude,
            (UfoStatus)map.ReadInt32("status", 0),
            map.ReadString("altitude", "STR_HIGH_UC"))
        {
            Id = map.ReadInt32("id", 0),
            LandId = map.ReadInt32("landId", 0),
            CrashId = map.ReadInt32("crashId", 0),
            MissionWaveNumber = map.ReadInt32("missionWaveNumber", -1),
            Direction = map.ReadString("direction", "STR_NORTH"),
            Damage = map.ReadInt32("damage", 0),
            Shield = map.ReadInt32("shield", -1),
            ShieldRechargeHandle = map.ReadInt32("shieldRechargeHandle", 0),
            SecondsRemaining = map.ReadInt32("secondsRemaining", 0),
            Detected = map.ReadBoolean("detected", false),
            HyperDetected = map.ReadBoolean("hyperDetected", false),
            InBattlescape = map.ReadBoolean("inBattlescape", false),
            HunterKiller = map.ReadBoolean("isHunterKiller", false),
            Escort = map.ReadBoolean("isEscort", false),
            HuntMode = map.ReadInt32("huntMode", 0),
            HuntBehavior = map.ReadInt32("huntBehavior", 0),
            Hunting = map.ReadBoolean("isHunting", false),
            Escorting = map.ReadBoolean("isEscorting", false),
            SoftlockShotCounter = map.ReadInt32("softlockShotCounter", 0),
            FireCountdown = map.ReadInt32("fireCountdown", 0),
            EscapeCountdown = map.ReadInt32("escapeCountdown", 0),
            Speed = map.ReadInt32("speed", 0),
            SpeedLongitude = map.ReadDouble("speedLon", 0),
            SpeedLatitude = map.ReadDouble("speedLat", 0),
            SpeedRadian = map.ReadDouble("speedRadian", 0),
            // Ufo::load always builds a destination waypoint, falling back to its own position.
            Destination = ReadTargetReference(map, markers, longitude, latitude) ??
                new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0,
                    longitude, latitude),
            OriginalWaypoint = map.TryGet("origWaypoint", out var origin) && origin is YamlMappingNode waypoint
                ? new WorldPosition(waypoint.ReadDouble("lon", 0), waypoint.ReadDouble("lat", 0))
                : null,
            ScriptValues = ReadScriptValues(map, content, "Ufo"),
        };
    }

    /// <summary>Reads a <c>Target::saveId</c> node and classifies it by its external type name.</summary>
    private static WorldTargetReference? ReadTargetReference(
        YamlMappingNode owner, WorldMarkerIndex markers, double fallbackLongitude, double fallbackLatitude)
    {
        if (!owner.TryGet("dest", out var node) || node is not YamlMappingNode map) return null;
        var type = map.ReadString("type", WorldTargetReference.WaypointType);
        var id = map.ReadInt32("id", 0);
        var longitude = map.ReadDouble("lon", fallbackLongitude);
        var latitude = map.ReadDouble("lat", fallbackLatitude);
        var uniqueId = map.ReadInt32("uniqueId", 0);
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
        private readonly HashSet<(string Type, int Id)> _crafts = [];

        public WorldMarkerIndex(
            IEnumerable<MissionSiteSnapshot> sites,
            IEnumerable<AlienBaseSnapshot> alienBases,
            RuntimeContent content, YamlMappingNode body)
        {
            foreach (var site in sites) _sites.Add((MarkerName(content, site.DeploymentId), site.Id));
            foreach (var alienBase in alienBases)
                _bases[(MarkerName(content, alienBase.DeploymentId), alienBase.Id)] =
                    (alienBase.Longitude, alienBase.Latitude, alienBase.DeploymentId);
            foreach (var owner in Maps(body, "bases"))
                foreach (var craft in Maps(owner, "crafts"))
                {
                    var type = craft.ReadString("type", string.Empty);
                    if (content.RuntimeRules.Crafts.TryGet(type, out _)) _crafts.Add((type, craft.ReadInt32("id", 0)));
                }
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
            if (_crafts.Contains((type, id))) return WorldTargetKind.Craft;
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

    private static YamlMappingNode?[] MatchAlienBases(IReadOnlyList<AlienBaseSnapshot> bases,
        IEnumerable<YamlMappingNode> source)
    {
        var originals = source.ToArray();
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var byIdentity = new Dictionary<(string Deployment, int Id), int>();
        for (var index = 0; index < originals.Length; index++)
        {
            var map = originals[index];
            var key = map.ReadString("oxcePortEntityKey", AlienBasePreservationKey(map));
            if (key.Length != 0 && !byKey.TryAdd(key, index))
                throw new InvalidDataException("Duplicate alien base preservation keys.");
            byIdentity.TryAdd((map.ReadString("deployment", "STR_ALIEN_BASE_ASSAULT"), map.ReadInt32("id", 0)), index);
        }
        var used = new bool[originals.Length];
        var matches = new YamlMappingNode?[bases.Count];
        for (var index = 0; index < bases.Count; index++)
        {
            var current = bases[index];
            var matched = current.PreservationKey.Length != 0
                ? byKey.TryGetValue(current.PreservationKey, out var keyed) ? keyed : -1
                : byIdentity.TryGetValue((current.DeploymentId, current.Id), out var legacy) ? legacy : -1;
            if (matched < 0 || used[matched] || YamlValueReader.ReadInt32(originals[matched], "id", 0) != current.Id) continue;
            matches[index] = originals[matched];
            used[matched] = true;
        }
        return matches;
    }

    private static string AlienBasePreservationKey(YamlMappingNode map) =>
        $"alien-base:identity:{map.ReadString("deployment", "STR_ALIEN_BASE_ASSAULT")}:{map.ReadInt32("id", 0)}";

    /// <summary>
    /// Imported and newly scheduled events have a persistent sidecar key. Unkeyed synthetic
    /// snapshots retain the legacy matching behavior: prefer complete saved state, then use the
    /// occurrence order within a rule when its countdown or completion flag has changed.
    /// Each source occurrence is consumed once, so repeated rule IDs keep distinct sidecars.
    /// </summary>
    private static YamlMappingNode?[] MatchScheduledEvents(
        IReadOnlyList<GeoscapeEventSnapshot> events, IEnumerable<YamlMappingNode> source)
    {
        var originals = source.ToArray();
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var exact = new Dictionary<(string RuleId, int Countdown, bool Over), Queue<int>>();
        var byRule = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var index = 0; index < originals.Length; index++)
        {
            var map = originals[index];
            var preservationKey = map.ReadString("oxcePortEntityKey", $"event:legacy:{index}");
            if (preservationKey.Length != 0 && !byKey.TryAdd(preservationKey, index))
                throw new InvalidDataException("Duplicate scheduled event preservation keys.");
            var ruleId = map.ReadString("name", string.Empty);
            var key = (ruleId, map.ReadInt32("spawnCountdown", 0), map.ReadBoolean("over", false));
            if (!exact.TryGetValue(key, out var exactMatches)) exact[key] = exactMatches = new Queue<int>();
            exactMatches.Enqueue(index);
            if (!byRule.TryGetValue(ruleId, out var ruleMatches)) byRule[ruleId] = ruleMatches = new Queue<int>();
            ruleMatches.Enqueue(index);
        }

        var used = new bool[originals.Length];
        var matches = new YamlMappingNode?[events.Count];
        for (var index = 0; index < events.Count; index++)
        {
            var scheduled = events[index];
            if (scheduled.PreservationKey.Length != 0)
            {
                if (byKey.TryGetValue(scheduled.PreservationKey, out var original) && !used[original] &&
                    YamlValueReader.ReadString(originals[original], "name", string.Empty) == scheduled.RuleId)
                {
                    matches[index] = originals[original];
                    used[original] = true;
                }
                continue; // A new keyed event must never inherit an old occurrence's sidecar.
            }
            var key = (scheduled.RuleId, scheduled.SpawnCountdown, scheduled.Over);
            var match = exact.TryGetValue(key, out var exactMatches) ? TakeUnused(exactMatches, used) : -1;
            if (match < 0 && byRule.TryGetValue(scheduled.RuleId, out var ruleMatches))
                match = TakeUnused(ruleMatches, used);
            if (match >= 0) matches[index] = originals[match];
        }
        return matches;

        static int TakeUnused(Queue<int> candidates, bool[] consumed)
        {
            while (candidates.Count != 0)
            {
                var candidate = candidates.Dequeue();
                if (consumed[candidate]) continue;
                consumed[candidate] = true;
                return candidate;
            }
            return -1;
        }
    }

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

    private static YamlMappingNode BuildUfo(UfoSnapshot ufo, YamlMappingNode? source, bool newBattle) => Overlay(source,
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
        Pair("mission", newBattle ? null : Integer(ufo.MissionId)),
        Pair("trajectory", newBattle ? null : Scalar(ufo.TrajectoryId)),
        Pair("trajectoryPoint", newBattle ? null : Integer(ufo.TrajectoryPoint)),
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
        Pair("oxcePortEntityKey", alienBase.PreservationKey.Length == 0 ? null : Scalar(alienBase.PreservationKey)),
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

    private static YamlMappingNode? BuildAlienStrategy(AlienStrategySnapshot strategy, YamlMappingNode? source)
    {
        if (strategy.IsEmpty && source is null) return null;
        var originalRegions = FirstByKey(Maps(source, "possibleMissions"),
            static region => region.ReadString("region", string.Empty));
        return Overlay(source,
    [
        Pair("regions", WeightMapping(strategy.RegionChances)),
        Pair("possibleMissions", strategy.RegionMissions.Count == 0 ? null : Sequence(strategy.RegionMissions.Select(
            region => Overlay(originalRegions.GetValueOrDefault(region.Key),
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
    }

    private static YamlMappingNode? WeightMapping(IReadOnlyList<KeyValuePair<string, ulong>> weights) =>
        weights.Count == 0 ? null : Mapping(weights.Select(static weight => Pair(weight.Key, (YamlNode?)ULong(weight.Value))));

    // Port-only (D4): the deployment request of a confirmed landing, with the target as a saveId node.
    private static YamlMappingNode? BuildDeploymentRequest(CampaignDeploymentRequest? request) => request is null ? null : Mapping(
    [
        Pair("missionType", Scalar(request.MissionType)),
        Pair("deployment", Scalar(request.DeploymentId)),
        Pair("alienCustomDeploy", Scalar(request.AlienCustomDeploymentId)),
        Pair("alienCustomMission", Scalar(request.AlienCustomMissionId)),
        Pair("race", Scalar(request.Race)),
        Pair("missionTexture", Integer(request.MissionTexture)),
        Pair("globeTexture", Integer(request.GlobeTexture)),
        Pair("shade", Integer(request.Shade)),
        Pair("dest", TargetReference(request.Target)),
        Pair("base", Integer(request.BaseId)),
        Pair("craft", Mapping([Pair("type", Scalar(request.CraftTypeId)), Pair("id", Integer(request.CraftId))])),
        Pair("soldiers", Sequence(request.Soldiers.Select(static id => (YamlNode)Integer(id)))),
        Pair("vehicles", Sequence(request.Vehicles.Select(static id => (YamlNode)Scalar(id)))),
        Pair("vehicleAmmo", request.VehicleAmmo.Count == 0 ? null : Sequence(request.VehicleAmmo.Select(static ammo => (YamlNode)Integer(ammo)))),
        Pair("items", IntMapping(request.Items.OrderBy(static pair => pair.Key, StringComparer.Ordinal))),
        Pair("ufoDamagePercentage", request.UfoDamagePercentage == 0 ? null : Integer(request.UfoDamagePercentage)),
    ]);

    private static CampaignDeploymentRequest ReadDeploymentRequest(YamlMappingNode map, WorldMarkerIndex markers)
    {
        var craft = map.TryGet("craft", out var node) && node is YamlMappingNode craftMap ? craftMap
            : throw new InvalidDataException("A deployment request needs its craft.");
        var vehicles = Array.AsReadOnly(Sequence(map, "vehicles").Select(static node => YamlValueReader.ReadString(node)).ToArray());
        var vehicleAmmo = Array.AsReadOnly(Sequence(map, "vehicleAmmo").Select(static node => YamlValueReader.ReadInt32(node)).ToArray());
        if (vehicleAmmo.Count != 0 && vehicleAmmo.Count != vehicles.Count)
            throw new InvalidDataException("A deployment request records ammo for a different number of vehicles than it carries.");
        var target = ReadTargetReference(map, markers, 0, 0) ?? throw new InvalidDataException("A deployment request needs its target.");
        // An alien base has no mission texture (setWorldTexture(0, globeTexture)); saves before Phase 7 recorded it as 0.
        var missionTexture = target.Kind == WorldTargetKind.AlienBase ? -1 : map.ReadInt32("missionTexture", -1);
        return new CampaignDeploymentRequest(map.ReadString("missionType", ""), map.ReadString("deployment", ""),
            map.ReadString("alienCustomDeploy", ""), map.ReadString("alienCustomMission", ""), map.ReadString("race", ""),
            missionTexture, map.ReadInt32("globeTexture", -1), map.ReadInt32("shade", 0),
            target, map.ReadInt32("base", 0), RequiredString(craft, "type"), craft.ReadInt32("id", 0))
        {
            Soldiers = Array.AsReadOnly(Sequence(map, "soldiers").Select(static node => YamlValueReader.ReadInt32(node)).ToArray()),
            Vehicles = vehicles,
            VehicleAmmo = vehicleAmmo,
            Items = ReadIntMap(map, "items"),
            UfoDamagePercentage = map.ReadInt32("ufoDamagePercentage", 0),
        };
    }

    private static YamlMappingNode? TargetReference(WorldTargetReference? reference) => reference is null ? null : Mapping(
    [
        Pair("lon", Real(reference.Longitude)), Pair("lat", Real(reference.Latitude)),
        Pair("type", Scalar(reference.TypeId)),
        Pair("id", Integer(reference.Id)),
        Pair("uniqueId", reference.UniqueId == 0 ? null : Integer(reference.UniqueId)),
    ]);
}
