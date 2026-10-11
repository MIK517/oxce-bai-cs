using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The strategic world capability: alien missions, UFOs, mission sites, alien bases, player
/// waypoints, scheduled events and the alien strategy table. Timed handlers own supported
/// transitions; preflight prevents unavailable world and later-phase effects from mutating a tick.
/// Reference: <c>Savegame/SavedGame.cpp</c> load/save of the world sections, <c>AlienMission.cpp</c>,
/// <c>Ufo.cpp</c>, <c>MissionSite.cpp</c>, <c>AlienBase.cpp</c>, <c>Waypoint.cpp</c>,
/// <c>GeoscapeEvent.cpp</c> and <c>AlienStrategy.cpp</c> at 4df3a5e.
/// </summary>
internal sealed partial class CampaignWorld(CampaignState campaign) : ICampaignCapability, ICampaignWorldQuery
{
    private readonly List<AlienMissionSnapshot> _missions = [];
    private readonly List<UfoSnapshot> _ufos = [];
    private readonly List<WaypointSnapshot> _waypoints = [];
    private readonly List<MissionSiteSnapshot> _sites = [];
    private readonly List<AlienBaseSnapshot> _alienBases = [];
    private ulong _alienBaseBirthSequence;
    private readonly List<GeoscapeEventSnapshot> _events = [];
    private AlienStrategyState _strategy = new();

    public void Register(CampaignCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Query<ICampaignWorldQuery>(this);
        registry.Operation<ICampaignWorldActivityPreflight>(this);
        registry.Operation<ICampaignMissionResearchInterruption>(this);
        registry.Capture(CaptureWorldState);
        registry.Restore(Restore);
        registry.Validate(Validate);
        registry.Initialize(Initialize);
        registry.Preflight(CampaignPreflightOrder.WorldSimulation, "world simulation", (_, highest, speed) => LiveWorldReason(highest, speed));
        RegisterUfoOperations(registry);
        RegisterHunting(registry);
        RegisterMissionOperations(registry);
        RegisterUfoDetection(registry);
        RegisterSiteOperations(registry);
        RegisterAlienBaseOperations(registry);
        RegisterAlienBaseMonthly(registry);
        RegisterCraftOperations(registry);
        RegisterCraftRemoval(registry);
        RegisterEventScheduling(registry);
        RegisterEventScripts(registry);
        RegisterMissionScripts(registry);
        RegisterAdhocScripts(registry);
        RegisterArcScripts(registry);
        RegisterWorldScheduling(registry);
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
        foreach (var ufo in _ufos.Where(static ufo => ufo.Detected && ufo.Status is not (UfoStatus.Destroyed or UfoStatus.IgnoreMe)))
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
                DetectionAlertIgnored = _ignoredUfoContacts.Contains(ufo.Id),
                SecondsRemaining = ufo.SecondsRemaining,
                Reference = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, ufo.Longitude, ufo.Latitude)
                { UniqueId = ufo.UniqueId },
                UnavailableActionReason = ufo.Status == UfoStatus.Flying ? InterceptionReason : LandingReason,
                HunterKiller = ufo.HunterKiller,
                HyperDetected = ufo.HyperDetected,
                RadarRange = UfoRadarRange(ufo),
                Hunting = ufo.Hunting,
                // UFO waypoints are anonymous Waypoint objects outside the player's list.
                Destination = Resolve(ufo.Destination)?.Position ??
                    (ufo.Destination is { Kind: WorldTargetKind.Waypoint } waypoint ? waypoint.Position : null),
            });
        foreach (var site in _sites.Where(static site => site.Detected))
            targets.Add(new CampaignWorldTarget(WorldTargetKind.MissionSite, site.Id, site.DeploymentId,
                site.Name.Length != 0 ? site.Name : MarkerName(site.DeploymentId), site.Longitude, site.Latitude)
            {
                Detected = site.Detected,
                SecondsRemaining = site.SecondsRemaining,
                Status = site.City,
                Reference = new(WorldTargetKind.MissionSite, MarkerName(site.DeploymentId), site.Id, site.Longitude, site.Latitude),
                UnavailableActionReason = LandingReason,
            });
        foreach (var alienBase in _alienBases.Where(static alienBase => alienBase.Discovered))
            targets.Add(new CampaignWorldTarget(WorldTargetKind.AlienBase, alienBase.Id, alienBase.DeploymentId,
                alienBase.Name.Length != 0 ? alienBase.Name : MarkerName(alienBase.DeploymentId),
                alienBase.Longitude, alienBase.Latitude)
            {
                Detected = alienBase.Discovered,
                Reference = new(WorldTargetKind.AlienBase, MarkerName(alienBase.DeploymentId), alienBase.Id,
                    alienBase.Longitude, alienBase.Latitude),
                UnavailableActionReason = LandingReason
            });
        foreach (var waypoint in _waypoints)
            targets.Add(new CampaignWorldTarget(WorldTargetKind.Waypoint, waypoint.Id,
                WorldTargetReference.WaypointType,
                waypoint.Name.Length != 0 ? waypoint.Name : WorldTargetReference.WaypointType,
                waypoint.Longitude, waypoint.Latitude)
            { Detected = true });
        return new CampaignWorldOverview(CampaignSnapshot.ReadOnly(targets), _missions.Count, _events.Count)
        {
            Bases = CampaignSnapshot.ReadOnly(campaign.BaseStates.Where(static owner => owner.IsPlaced)
                .Select(owner => new CampaignWorldBase(owner.Id, owner.Name, owner.Longitude, owner.Latitude)
                {
                    RadarRange = BaseRadarRange(owner),
                })),
            Crafts = CampaignSnapshot.ReadOnly(campaign.BaseStates.SelectMany(owner => owner.Crafts.Select(craft =>
            {
                var state = craft.Logistics;
                var airborneReason = state is not { Status: "STR_OUT" } ? "The craft is not airborne." :
                    IsReturningHome(state) ? ReturningReason : null;
                return new CampaignWorldCraft(owner.Id, campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule),
                    craft.Id, state?.Name ?? string.Empty, state?.Status ?? string.Empty,
                    state?.Longitude ?? owner.Longitude, state?.Latitude ?? owner.Latitude,
                    state?.Fuel ?? 0, Resolve(state?.Destination), DispatchReason(owner, craft), airborneReason)
                {
                    RadarRange = CraftRadarRange(craft),
                };
            }))),
        };
    });

    private int BaseRadarRange(CampaignState.BaseState owner)
    {
        // Globe::drawRadars: the largest completed radar below MAX_DRAW_RADAR_CIRCLE_RADIUS.
        var range = 0;
        foreach (var facility in owner.Facilities)
        {
            if (facility.BuildTime != 0) continue;
            var radar = campaign.Content.RuntimeRules.Facilities[facility.Rule].Value.RadarRange;
            if (radar < 10_000 && radar > range) range = radar;
        }
        return range;
    }

    private int CraftRadarRange(CampaignState.CraftState craft)
    {
        if (craft.Logistics is not { } state) return 0;
        var rules = campaign.Content.RuntimeRules;
        CraftLogistics.EffectiveRadar(rules.Crafts[craft.Rule].Value, state.Weapons, rules, out var range, out _);
        return range;
    }

    private int UfoRadarRange(UfoSnapshot ufo)
    {
        var rules = campaign.Content.RuntimeRules;
        if (!rules.Ufos.TryGet(ufo.RuleId, out var handle)) return 0;
        var rule = rules.Ufos[handle].Value;
        var race = FindMission(ufo.MissionId)?.Race ?? string.Empty;
        return (int)Math.Clamp((long)rule.Stats.RadarRange + rule.RaceBonus(race).RadarRange, 0, int.MaxValue);
    }

    private CampaignSnapshot CaptureWorldState(CampaignSnapshot snapshot) => snapshot with
    {
        World = Capture(),
        // Target::saveId writes the target's current coordinates, including craft moved later
        // in the same pass. Resolve here without mutating live state or depending on list order.
        Bases = CampaignSnapshot.ReadOnly(snapshot.Bases.Select(owner => owner with
        {
            Crafts = CampaignSnapshot.ReadOnly(owner.Crafts.Select(craft => craft.Logistics is { Destination: { } destination } state
                && destination.Kind is WorldTargetKind.Ufo or WorldTargetKind.Craft
                ? craft with { Logistics = state with { Destination = Resolve(destination) ?? destination } } : craft)),
        })),
    };

    /// <summary>AlienStrategy::init for a newly created campaign.</summary>
    private void Initialize() => _strategy.Initialize(StrategyRegions());

    private IEnumerable<(string Region, ulong Weight, IEnumerable<KeyValuePair<string, ulong>> Missions)> StrategyRegions() =>
        campaign.Content.RuntimeRules.Regions.Rules.Select(static rule =>
            (rule.Id, rule.Value.RegionWeight, (IEnumerable<KeyValuePair<string, ulong>>)rule.Value.MissionWeights));

    private WorldSnapshot Capture() => new()
    {
        Missions = CampaignSnapshot.ReadOnly(_missions),
        Ufos = CampaignSnapshot.ReadOnly(_ufos.Select(ufo => ufo.Destination is { } destination &&
            (ufo.Hunting || ufo.Escorting)
            ? ufo with { Destination = Resolve(destination) ?? destination } : ufo)),
        Waypoints = CampaignSnapshot.ReadOnly(_waypoints),
        MissionSites = CampaignSnapshot.ReadOnly(_sites),
        AlienBases = CampaignSnapshot.ReadOnly(_alienBases),
        AlienBaseBirthSequence = _alienBaseBirthSequence,
        Events = CampaignSnapshot.ReadOnly(_events),
        GeneratedEvents = CampaignSnapshot.ReadOnlyIds(_generatedEvents),
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
        _alienBaseBirthSequence = snapshot.World.AlienBaseBirthSequence;
        _events.Clear();
        _generatedEvents.Clear();
        foreach (var pair in snapshot.World.GeneratedEvents) _generatedEvents.Add(pair.Key, pair.Value);

        // SavedGame::load drops entities whose rules are gone and keeps the rest.
        foreach (var alienBase in snapshot.World.AlienBases)
            if (rules.AlienDeployments.TryGet(alienBase.DeploymentId, out _)) _alienBases.Add(alienBase);
        foreach (var mission in snapshot.World.Missions)
            if (rules.AlienMissions.TryGet(mission.RuleId, out _)) _missions.Add(RestoreMission(mission, rules));
        foreach (var ufo in snapshot.World.Ufos)
            if (rules.Ufos.TryGet(ufo.RuleId, out _))
                _ufos.Add(ufo.InBattlescape ? ufo with
                {
                    Speed = 0,
                    SpeedRadian = 0,
                    SpeedLongitude = 0,
                    SpeedLatitude = 0,
                } : ufo);
        foreach (var scheduled in snapshot.World.Events)
            if (rules.Events.TryGet(scheduled.RuleId, out _)) _events.Add(scheduled);
        foreach (var site in snapshot.World.MissionSites)
            if (rules.AlienMissions.TryGet(site.MissionRuleId, out _) &&
                rules.AlienDeployments.TryGet(site.DeploymentId, out _))
                _sites.Add(site with
                {
                    CustomDeploymentId = rules.AlienDeployments.TryGet(site.CustomDeploymentId, out _)
                        ? site.CustomDeploymentId : string.Empty,
                    UfoUniqueId = _ufos.Any(ufo => ufo.UniqueId == site.UfoUniqueId) ? site.UfoUniqueId : 0,
                });
        foreach (var owner in campaign.BaseStates)
            if (owner.RetaliationMissionId is { } id && !_missions.Any(mission => mission.Id == id))
                owner.RetaliationMissionId = null;
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
    /// keeps the dummy waypoint it built from the saved coordinates. Ordinary-campaign
    /// UFO mission/trajectory links and alien mission/base links still fail in <see cref="Validate"/>.
    /// A missing site/UFO link is optional and was cleared during restore.
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
        foreach (var alienBase in _alienBases)
            if (alienBase.PreservationKey.StartsWith("alien-base:generated:", StringComparison.Ordinal) &&
                (!ulong.TryParse(alienBase.PreservationKey.AsSpan("alien-base:generated:".Length),
                    System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var birth) ||
                 birth == 0 || birth > _alienBaseBirthSequence))
                throw new InvalidDataException("Alien base birth identity exceeds its saved sequence.");

        var rules = campaign.Content.RuntimeRules;
        EnsureUnique(_missions.Select(static mission => mission.Id), "alien mission IDs");
        EnsureUnique(_ufos.Select(static ufo => ufo.UniqueId), "UFO unique IDs");
        EnsureUnique(_waypoints.Select(static waypoint => waypoint.Id), "waypoint IDs");
        EnsureUnique(_sites.Select(static site => (site.DeploymentId, site.Id)), "mission site identities");
        EnsureUnique(_alienBases.Select(static item => (item.DeploymentId, item.Id)), "alien base identities");
        EnsureUnique(_alienBases.Where(static item => item.PreservationKey.Length != 0)
            .Select(static item => item.PreservationKey), "alien base preservation keys");

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
            if (campaign.MonthsPassed != -1)
            {
                var mission = _missions.Find(candidate => candidate.Id == ufo.MissionId);
                if (mission is null)
                    throw new InvalidDataException("Unknown UFO mission; the save is corrupt.");
                // Ufo::load accepts any wave number, including negative values from old saves;
                // an out-of-range wave only matters when AlienMission::ufoReachedWaypoint reads
                // it, and the arrival preflight stops time there instead of rejecting the save.
                if (!rules.UfoTrajectories.TryGet(ufo.TrajectoryId, out var trajectory))
                    throw new InvalidDataException("Unknown UFO trajectory; the save is corrupt.");
                var waypoints = rules.UfoTrajectories[trajectory].Value.Waypoints;
                if ((uint)ufo.TrajectoryPoint >= (uint)waypoints.Count)
                    throw new InvalidDataException(
                        $"UFO trajectory '{ufo.TrajectoryId}' has no waypoint {ufo.TrajectoryPoint}.");
            }
            if (ufo.SecondsRemaining < 0 || ufo.Damage < 0)
                throw new InvalidDataException("UFO counters cannot be negative.");
            ValidateReference(ufo.Destination, "UFO destination");
            if (ufo.Destination is null) throw new InvalidDataException("A UFO must have a destination.");
        }

        var linkedSiteUfos = new HashSet<int>();
        foreach (var site in _sites)
        {
            if (site.Id <= 0) throw new InvalidDataException("Mission site IDs must be positive.");
            ValidatePosition(site.Position, "mission site");
            if (site.SecondsRemaining < 0) throw new InvalidDataException("Mission site timers cannot be negative.");
            if (site.UfoUniqueId != 0 && !linkedSiteUfos.Add(site.UfoUniqueId))
                throw new InvalidDataException("Multiple mission sites reference the same dormant UFO.");
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
        ValidateEventHistory();

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
        if (reference.Id <= 0 && reference.Kind is not (WorldTargetKind.Base or WorldTargetKind.Waypoint) &&
            !(reference.Kind == WorldTargetKind.Ufo && reference.Id == 0 && reference.UniqueId > 0))
            throw new InvalidDataException($"A {what} must reference a positive identity.");
        ValidatePosition(reference.Position, what);
    }

    /// <summary>The reason live world state blocks time until its handlers exist.</summary>
    private string? LiveWorldReason(CampaignTimeTrigger highest, CampaignTimeSpeed speed)
    {
        _pass.BeginWorld(highest);
        if (FlightFuelValuesReason(highest) is { } fuelValuesReason) return fuelValuesReason;
        var interruptionReason = MissionInterruptionReason(highest, out var guaranteedResearch);
        if (interruptionReason is not null) return interruptionReason;
        var destructionReason = AlienBaseDestructionReason(highest, ref guaranteedResearch);
        if (destructionReason is not null) return destructionReason;
        _pass.DailyResearchForecast = guaranteedResearch;
        if (PrepareDailyGeneratedWaveForecasts(highest, guaranteedResearch) is { } generationReason) return generationReason;
        if (PrepareMissionCounterForecasts(highest) is { } counterReason) return counterReason;
        if (SiteDiscoveryReason(highest) is { } discoveryReason) return discoveryReason;
        if (SiteFollowerWorkReason(highest) is { } followerWorkReason) return followerWorkReason;
        if (SiteUfoRepairReason(highest) is { } repairReason) return repairReason;
        if (PrepareSiteResearch(highest) is { } siteResearchReason) return siteResearchReason;
        if (HuntingReason(highest, speed) is { } huntingReason) return huntingReason;
        if (SiteResearchUfoWorkReason(highest) is { } siteFlightReason) return siteFlightReason;
        if (PrepareHuntingGeneratedWaveForecasts(highest, guaranteedResearch) is { } huntBirthReason) return huntBirthReason;
        var ufoReason = UfoMovementReason(highest);
        if (ufoReason is not null) return ufoReason;
        var missionReason = MissionSchedulingReason(highest, guaranteedResearch);
        if (missionReason is not null) return missionReason;
        if (ArrivalContactLossReason(highest) is { } arrivalLossReason) return arrivalLossReason;
        var alienBaseReason = AlienBaseActivityReason(highest, guaranteedResearch);
        if (alienBaseReason is not null) return alienBaseReason;
        var baseDetectionReason = UfoBaseDetectionReason(highest);
        if (baseDetectionReason is not null) return baseDetectionReason;
        var siteReason = SiteTimingReason(highest);
        if (siteReason is not null) return siteReason;
        if (UfoSiteConversionsReason(highest) is { } conversionReason) return conversionReason;
        if (ObjectiveBasePatrolReason(highest) is { } patrolReason) return patrolReason;
        var detectionReason = UfoHalfHourReason(highest, guaranteedResearch);
        if (detectionReason is not null) return detectionReason;
        if (FlightFuelRefundReason(highest) is { } fuelRefundReason) return fuelRefundReason;
        return EventSchedulingReason(highest);
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
