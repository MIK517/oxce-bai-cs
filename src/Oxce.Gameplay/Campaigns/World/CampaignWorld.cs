using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The strategic world capability: alien missions, UFOs, mission sites, alien bases, player
/// waypoints, scheduled events and the alien strategy table. This commit owns the graph, its
/// identities and its persistence; scheduling and movement arrive with their own handlers, so
/// a campaign carrying live world state still stops time with a diagnostic.
/// Reference: <c>Savegame/SavedGame.cpp</c> load/save of the world sections, <c>AlienMission.cpp</c>,
/// <c>Ufo.cpp</c>, <c>MissionSite.cpp</c>, <c>AlienBase.cpp</c>, <c>Waypoint.cpp</c>,
/// <c>GeoscapeEvent.cpp</c> and <c>AlienStrategy.cpp</c> at 4df3a5e.
/// </summary>
internal sealed class CampaignWorld(CampaignState campaign) : ICampaignCapability, ICampaignWorldQuery
{
    private readonly List<AlienMissionSnapshot> _missions = [];
    private readonly List<UfoSnapshot> _ufos = [];
    private readonly List<WaypointSnapshot> _waypoints = [];
    private readonly List<MissionSiteSnapshot> _sites = [];
    private readonly List<AlienBaseSnapshot> _alienBases = [];
    private readonly List<GeoscapeEventSnapshot> _events = [];
    private AlienStrategyState _strategy = new();

    public void Register(CampaignCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Query<ICampaignWorldQuery>(this);
        registry.Capture(snapshot => snapshot with { World = Capture() });
        registry.Restore(Restore);
        registry.Validate(Validate);
        registry.Initialize(Initialize);
        registry.Preflight(CampaignPreflightOrder.WorldSimulation, "world simulation", (_, _) => LiveWorldReason());
    }

    internal AlienStrategyState Strategy => _strategy;

    internal IReadOnlyList<AlienMissionSnapshot> Missions => _missions;

    internal IReadOnlyList<UfoSnapshot> Ufos => _ufos;

    internal IReadOnlyList<MissionSiteSnapshot> MissionSites => _sites;

    internal IReadOnlyList<AlienBaseSnapshot> AlienBases => _alienBases;

    internal IReadOnlyList<WaypointSnapshot> Waypoints => _waypoints;

    internal IReadOnlyList<GeoscapeEventSnapshot> Events => _events;

    public CampaignWorldOverview QueryWorld() => campaign.Read(() =>
    {
        var targets = new List<CampaignWorldTarget>();
        foreach (var ufo in _ufos.Where(static ufo => ufo.Status != UfoStatus.Destroyed))
            targets.Add(new CampaignWorldTarget(WorldTargetKind.Ufo, ufo.Status switch
            {
                UfoStatus.Landed => ufo.LandId,
                UfoStatus.Crashed => ufo.CrashId,
                _ => ufo.Id,
            }, ufo.RuleId, MarkerLabel(ufo), ufo.Longitude, ufo.Latitude)
            {
                Status = ufo.Status.ToString(),
                Altitude = ufo.Altitude,
                Detected = ufo.Detected,
                SecondsRemaining = ufo.SecondsRemaining,
            });
        foreach (var site in _sites)
            targets.Add(new CampaignWorldTarget(WorldTargetKind.MissionSite, site.Id, site.DeploymentId,
                site.Name.Length != 0 ? site.Name : MarkerName(site.DeploymentId), site.Longitude, site.Latitude)
            {
                Detected = site.Detected,
                SecondsRemaining = site.SecondsRemaining,
                Status = site.City,
            });
        foreach (var alienBase in _alienBases)
            targets.Add(new CampaignWorldTarget(WorldTargetKind.AlienBase, alienBase.Id, alienBase.DeploymentId,
                alienBase.Name.Length != 0 ? alienBase.Name : MarkerName(alienBase.DeploymentId),
                alienBase.Longitude, alienBase.Latitude)
            { Detected = alienBase.Discovered });
        foreach (var waypoint in _waypoints)
            targets.Add(new CampaignWorldTarget(WorldTargetKind.Waypoint, waypoint.Id,
                WorldTargetReference.WaypointType,
                waypoint.Name.Length != 0 ? waypoint.Name : WorldTargetReference.WaypointType,
                waypoint.Longitude, waypoint.Latitude)
            { Detected = true });
        return new CampaignWorldOverview(CampaignSnapshot.ReadOnly(targets), _missions.Count, _events.Count);
    });

    /// <summary>AlienStrategy::init for a newly created campaign.</summary>
    private void Initialize() => _strategy.Initialize(StrategyRegions());

    private IEnumerable<(string Region, ulong Weight, IEnumerable<KeyValuePair<string, ulong>> Missions)> StrategyRegions() =>
        campaign.Content.RuntimeRules.Regions.Rules.Select(static rule =>
            (rule.Id, rule.Value.RegionWeight, (IEnumerable<KeyValuePair<string, ulong>>)rule.Value.MissionWeights));

    private WorldSnapshot Capture() => new()
    {
        Missions = CampaignSnapshot.ReadOnly(_missions),
        Ufos = CampaignSnapshot.ReadOnly(_ufos),
        Waypoints = CampaignSnapshot.ReadOnly(_waypoints),
        MissionSites = CampaignSnapshot.ReadOnly(_sites),
        AlienBases = CampaignSnapshot.ReadOnly(_alienBases),
        Events = CampaignSnapshot.ReadOnly(_events),
        Strategy = CaptureStrategy(),
    };

    private AlienStrategySnapshot CaptureStrategy() => new()
    {
        RegionChances = CampaignSnapshot.ReadOnly(_strategy.RegionChances.Entries),
        RegionMissions = CampaignSnapshot.ReadOnly(_strategy.RegionMissions.Select(static pair =>
            new KeyValuePair<string, IReadOnlyList<KeyValuePair<string, ulong>>>(
                pair.Key, CampaignSnapshot.ReadOnly(pair.Value.Entries)))),
        MissionRuns = CampaignSnapshot.ReadOnly(_strategy.MissionRuns),
        MissionLocations = CampaignSnapshot.ReadOnly(_strategy.MissionLocations.Select(static pair =>
            new KeyValuePair<string, IReadOnlyList<AlienStrategyState.MissionLocation>>(
                pair.Key, CampaignSnapshot.ReadOnly(pair.Value)))),
    };

    private void Restore(CampaignSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.World);
        var rules = campaign.Content.RuntimeRules;
        _missions.Clear();
        _ufos.Clear();
        _waypoints.Clear();
        _sites.Clear();
        _alienBases.Clear();
        _events.Clear();

        // SavedGame::load drops entities whose rules are gone and keeps the rest.
        foreach (var alienBase in snapshot.World.AlienBases)
            if (rules.AlienDeployments.TryGet(alienBase.DeploymentId, out _)) _alienBases.Add(alienBase);
        foreach (var mission in snapshot.World.Missions)
            if (rules.AlienMissions.TryGet(mission.RuleId, out _)) _missions.Add(RestoreMission(mission, rules));
        foreach (var ufo in snapshot.World.Ufos)
            if (rules.Ufos.TryGet(ufo.RuleId, out _)) _ufos.Add(ufo);
        foreach (var scheduled in snapshot.World.Events)
            if (rules.Events.TryGet(scheduled.RuleId, out _)) _events.Add(scheduled);
        foreach (var site in snapshot.World.MissionSites)
            if (rules.AlienMissions.TryGet(site.MissionRuleId, out _) &&
                rules.AlienDeployments.TryGet(site.DeploymentId, out _)) _sites.Add(site);
        _waypoints.AddRange(snapshot.World.Waypoints);
        NormalizeDestinations();
        _strategy = AlienStrategyState.Restore(
            snapshot.World.Strategy.RegionChances,
            snapshot.World.Strategy.RegionMissions,
            snapshot.World.Strategy.MissionRuns,
            snapshot.World.Strategy.MissionLocations,
            id => rules.Regions.TryGet(id, out _));
    }

    /// <summary>
    /// AlienMission::load: a mission whose region or race no longer exists is interrupted and
    /// temporarily reassigned instead of failing the load.
    /// </summary>
    private static AlienMissionSnapshot RestoreMission(AlienMissionSnapshot mission, RuntimeRuleCatalog rules)
    {
        var result = mission;
        if (!rules.Regions.TryGet(result.RegionId, out _))
        {
            if (rules.Regions.Count == 0) throw new InvalidDataException("A world mission requires at least one region.");
            result = result with
            {
                Interrupted = true,
                RegionId = rules.Regions.Rules[0].Id,
                MissionSiteZoneArea = result.MissionSiteZoneArea > -1 ? 0 : result.MissionSiteZoneArea,
            };
        }
        if (!rules.AlienRaces.TryGet(result.Race, out _))
        {
            if (rules.AlienRaces.Count == 0) throw new InvalidDataException("A world mission requires at least one alien race.");
            result = result with { Interrupted = true, Race = rules.AlienRaces.Rules[0].Id };
        }
        return result;
    }

    /// <summary>
    /// Resolves the destinations the save recorded. The reference tolerates a destination whose
    /// target is gone: <c>Craft::load</c> simply leaves the craft without one, and <c>Ufo::load</c>
    /// keeps the dummy waypoint it built from the saved coordinates. Structural references
    /// (a UFO's mission, a mission's alien base, a site's UFO) still fail in <see cref="Validate"/>.
    /// </summary>
    private void NormalizeDestinations()
    {
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            var destination = ufo.Destination;
            // Ufo::load first creates an anonymous waypoint from dest coordinates.
            // Ufo::finishLoading replaces it only for a hunted craft or an escorted UFO.
            var resolved = destination is null ? null
                : ufo.Hunting && destination.Kind == WorldTargetKind.Craft ? Resolve(destination)
                : !ufo.Hunting && ufo.Escorting && destination.Kind == WorldTargetKind.Ufo &&
                    destination.UniqueId > 0 ? Resolve(destination)
                : null;
            _ufos[index] = ufo with
            {
                Destination = resolved ??
                    new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0,
                        destination?.Longitude ?? ufo.Longitude, destination?.Latitude ?? ufo.Latitude),
            };
        }
        foreach (var owner in campaign.BaseStates)
        {
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { Destination: { } destination } logistics) continue;
                // Craft::load returns a craft bound for "STR_BASE" to its own base, whatever the saved ID.
                var resolved = destination.Kind == WorldTargetKind.Base
                    ? destination with { Id = owner.Id, Longitude = owner.Longitude, Latitude = owner.Latitude }
                    : Resolve(destination);
                if (resolved == destination) continue;
                owner.Crafts[index] = craft with { Logistics = logistics with { Destination = resolved } };
            }
        }
    }

    private void Validate()
    {
        var rules = campaign.Content.RuntimeRules;
        EnsureUnique(_missions.Select(static mission => mission.Id), "alien mission IDs");
        EnsureUnique(_ufos.Select(static ufo => ufo.UniqueId), "UFO unique IDs");
        EnsureUnique(_waypoints.Select(static waypoint => waypoint.Id), "waypoint IDs");
        EnsureUnique(_sites.Select(static site => (site.DeploymentId, site.Id)), "mission site identities");
        EnsureUnique(_alienBases.Select(static item => (item.DeploymentId, item.Id)), "alien base identities");
        EnsureUnique(_events.Select(static scheduled => scheduled.RuleId), "scheduled event IDs");

        foreach (var mission in _missions)
        {
            if (mission.Id <= 0) throw new InvalidDataException("Alien mission IDs must be positive.");
            if (mission.NextWave < 0 || mission.NextUfoCounter < 0 || mission.SpawnCountdown < 0 || mission.LiveUfos < 0)
                throw new InvalidDataException("Alien mission counters cannot be negative.");
            var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (mission.NextWave > rule.Waves.Count)
                throw new InvalidDataException($"Alien mission '{mission.RuleId}' has no wave {mission.NextWave}.");
            // AlienMission::load throws when the referenced alien base is missing.
            if (mission.AlienBase is { } alienBase && !_alienBases.Any(
                    candidate => candidate.Id == alienBase.Id && MarkerName(candidate.DeploymentId) == alienBase.TypeId))
                throw new InvalidDataException("Corrupted save: a world mission references a missing alien base.");
        }

        foreach (var ufo in _ufos)
        {
            if (ufo.UniqueId <= 0) throw new InvalidDataException("UFO unique IDs must be positive.");
            ValidatePosition(ufo.Position, "UFO");
            if (!Enum.IsDefined(ufo.Status)) throw new InvalidDataException("UFO status is invalid.");
            if (WorldAltitudes.Index(ufo.Altitude) < 0)
                throw new InvalidDataException($"UFO altitude '{ufo.Altitude}' is not a reference altitude.");
            if (_missions.All(mission => mission.Id != ufo.MissionId))
                throw new InvalidDataException("Unknown UFO mission; the save is corrupt.");
            if (!rules.UfoTrajectories.TryGet(ufo.TrajectoryId, out var trajectory))
                throw new InvalidDataException("Unknown UFO trajectory; the save is corrupt.");
            var waypoints = rules.UfoTrajectories[trajectory].Value.Waypoints;
            if ((uint)ufo.TrajectoryPoint >= (uint)waypoints.Count)
                throw new InvalidDataException(
                    $"UFO trajectory '{ufo.TrajectoryId}' has no waypoint {ufo.TrajectoryPoint}.");
            if (ufo.SecondsRemaining < 0 || ufo.Damage < 0)
                throw new InvalidDataException("UFO counters cannot be negative.");
            ValidateReference(ufo.Destination, "UFO destination");
            if (ufo.Destination is null) throw new InvalidDataException("A UFO must have a destination.");
        }

        foreach (var site in _sites)
        {
            if (site.Id <= 0) throw new InvalidDataException("Mission site IDs must be positive.");
            ValidatePosition(site.Position, "mission site");
            if (site.SecondsRemaining < 0) throw new InvalidDataException("Mission site timers cannot be negative.");
            if (site.CustomDeploymentId.Length != 0 && !rules.AlienDeployments.TryGet(site.CustomDeploymentId, out _))
                throw new InvalidDataException($"Mission site deployment '{site.CustomDeploymentId}' is unknown.");
            if (site.UfoUniqueId > 0 && _ufos.All(ufo => ufo.UniqueId != site.UfoUniqueId))
                throw new InvalidDataException("A mission site references a missing UFO.");
        }

        foreach (var alienBase in _alienBases)
        {
            if (alienBase.Id <= 0) throw new InvalidDataException("Alien base IDs must be positive.");
            ValidatePosition(alienBase.Position, "alien base");
            if (alienBase.StartMonth < 0 || alienBase.GenMissionCount < 0 ||
                alienBase.MinutesSinceLastHuntMissionGeneration < 0)
                throw new InvalidDataException("Alien base counters cannot be negative.");
            if (alienBase.PactCountryId.Length != 0 && !rules.Countries.TryGet(alienBase.PactCountryId, out _))
                throw new InvalidDataException($"Alien base pact country '{alienBase.PactCountryId}' is unknown.");
        }

        foreach (var waypoint in _waypoints)
        {
            if (waypoint.Id <= 0) throw new InvalidDataException("Waypoint IDs must be positive.");
            ValidatePosition(waypoint.Position, "waypoint");
        }

        foreach (var scheduled in _events)
            if (scheduled.SpawnCountdown < 0)
                throw new InvalidDataException("Scheduled event countdowns cannot be negative.");

        foreach (var state in campaign.BaseStates)
            foreach (var craft in state.Crafts)
                if (craft.Logistics?.Destination is { } destination)
                    ValidateReference(destination, "craft destination");
    }

    /// <summary>
    /// Resolves the reference a save recorded. An unresolved craft destination is dropped like
    /// <c>Craft::load</c> does; an unresolved UFO destination keeps its recorded coordinates.
    /// </summary>
    internal WorldTargetReference? Resolve(WorldTargetReference? reference)
    {
        if (reference is null) return null;
        switch (reference.Kind)
        {
            case WorldTargetKind.Base:
                var owner = campaign.BaseStates.FirstOrDefault(state => state.Id == reference.Id);
                return owner is null ? null : reference with { Longitude = owner.Longitude, Latitude = owner.Latitude };
            case WorldTargetKind.Craft:
                foreach (var state in campaign.BaseStates)
                    foreach (var craft in state.Crafts)
                        if (craft.Id == reference.Id &&
                            campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule) == reference.TypeId)
                            return craft.Logistics is { } logistics
                                ? reference with { Longitude = logistics.Longitude, Latitude = logistics.Latitude }
                                : reference;
                return null;
            case WorldTargetKind.Ufo:
                var ufo = reference.UniqueId > 0
                    ? _ufos.FirstOrDefault(candidate => candidate.UniqueId == reference.UniqueId)
                    : _ufos.FirstOrDefault(candidate => candidate.Id == reference.Id);
                return ufo is null ? null : reference with { Longitude = ufo.Longitude, Latitude = ufo.Latitude };
            case WorldTargetKind.Waypoint:
                var waypoint = _waypoints.FirstOrDefault(candidate => candidate.Id == reference.Id);
                return waypoint is null ? null : reference with { Longitude = waypoint.Longitude, Latitude = waypoint.Latitude };
            case WorldTargetKind.MissionSite:
                var site = _sites.FirstOrDefault(candidate =>
                    candidate.Id == reference.Id && MarkerName(candidate.DeploymentId) == reference.TypeId);
                return site is null ? null : reference with { Longitude = site.Longitude, Latitude = site.Latitude };
            case WorldTargetKind.AlienBase:
                var alienBase = _alienBases.FirstOrDefault(candidate =>
                    candidate.Id == reference.Id && MarkerName(candidate.DeploymentId) == reference.TypeId);
                return alienBase is null
                    ? null
                    : reference with { Longitude = alienBase.Longitude, Latitude = alienBase.Latitude };
            default:
                throw new InvalidDataException("A globe target reference has an unsupported kind.");
        }
    }

    /// <summary>
    /// Checks a destination that survived <see cref="NormalizeDestinations"/>: its kind, identity and
    /// position must be usable. A reference that no longer resolves was already dropped there.
    /// </summary>
    private static void ValidateReference(WorldTargetReference? reference, string what)
    {
        if (reference is null) return;
        if (!Enum.IsDefined(reference.Kind)) throw new InvalidDataException($"A {what} has an unsupported kind.");
        if (reference.Id <= 0 && reference.Kind is not (WorldTargetKind.Base or WorldTargetKind.Waypoint))
            throw new InvalidDataException($"A {what} must reference a positive identity.");
        ValidatePosition(reference.Position, what);
    }

    /// <summary>The reason live world state blocks time until its handlers exist.</summary>
    private string? LiveWorldReason()
    {
        if (_ufos.Any(static ufo => ufo.Status != UfoStatus.Destroyed))
            return "UFO movement requires world simulation.";
        if (_missions.Count != 0) return "Alien mission scheduling requires world simulation.";
        if (_sites.Count != 0) return "Mission site expiry requires world simulation.";
        if (_alienBases.Count != 0) return "Alien base activity requires world simulation.";
        if (_events.Count != 0) return "Strategic event scheduling requires world simulation.";
        return null;
    }

    private string MarkerName(string deploymentId) =>
        campaign.Content.RuntimeRules.AlienDeployments.TryGet(deploymentId, out var handle)
            ? campaign.Content.RuntimeRules.AlienDeployments[handle].Value.MarkerName
            : deploymentId;

    private static string MarkerLabel(UfoSnapshot ufo) => ufo.Status switch
    {
        UfoStatus.Landed => "STR_LANDING_SITE_",
        UfoStatus.Crashed => "STR_CRASH_SITE_",
        _ => "STR_UFO_",
    };

    private static void ValidatePosition(WorldPosition position, string what)
    {
        if (!position.IsNormalized)
            throw new InvalidDataException($"A {what} position is outside the globe coordinate range.");
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string name) where T : notnull
    {
        var seen = new HashSet<T>();
        if (values.Any(value => !seen.Add(value))) throw new InvalidDataException($"Duplicate {name} were found.");
    }
}
