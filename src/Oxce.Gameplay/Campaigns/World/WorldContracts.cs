using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>The kinds of globe target a moving target can be sent to. Reference: <c>Target::getType</c>.</summary>
public enum WorldTargetKind
{
    Base = 0,
    Craft = 1,
    Ufo = 2,
    Waypoint = 3,
    MissionSite = 4,
    AlienBase = 5,
}

/// <summary>
/// A saved reference to a globe target: the external type name plus the identity used by
/// <c>Target::saveId</c>. The coordinates are the ones the reference also writes, so a target
/// that no longer exists still has a position to fall back on.
/// </summary>
public sealed record WorldTargetReference(
    WorldTargetKind Kind,
    string TypeId,
    int Id,
    double Longitude,
    double Latitude)
{
    public const string BaseType = "STR_BASE";
    public const string UfoType = "STR_UFO";
    public const string WaypointType = "STR_WAY_POINT";

    /// <summary>UFO references also carry <c>Ufo::_uniqueId</c>, which survives marker renumbering.</summary>
    public int UniqueId { get; init; }

    public static WorldTargetReference ForWaypoint(int id, WorldPosition position) =>
        new(WorldTargetKind.Waypoint, WaypointType, id, position.Longitude, position.Latitude);

    public WorldPosition Position => new(Longitude, Latitude);
}

/// <summary>Reference: <c>Ufo::UfoStatus</c>.</summary>
public enum UfoStatus
{
    Flying = 0,
    Landed = 1,
    Crashed = 2,
    Destroyed = 3,
    IgnoreMe = 4,
}

/// <summary>Reference: <c>Savegame/AlienMission.cpp</c> save nodes.</summary>
public sealed record AlienMissionSnapshot(
    int Id,
    string RuleId,
    string RegionId,
    string Race,
    int NextWave,
    int NextUfoCounter,
    int SpawnCountdown,
    int LiveUfos,
    int MissionSiteZoneArea)
{
    public bool Interrupted { get; init; }
    public bool MultiUfoRetaliationInProgress { get; init; }
    /// <summary>The alien base this mission operates from, by marker name and id.</summary>
    public WorldTargetReference? AlienBase { get; init; }
}

/// <summary>Reference: <c>Savegame/Ufo.cpp</c> save nodes.</summary>
public sealed record UfoSnapshot(
    int UniqueId,
    string RuleId,
    int MissionId,
    string TrajectoryId,
    int TrajectoryPoint,
    double Longitude,
    double Latitude,
    UfoStatus Status,
    string Altitude)
{
    /// <summary>The marker id (<c>Target::_id</c>); landed and crashed markers use their own ids.</summary>
    public int Id { get; init; }
    public int LandId { get; init; }
    public int CrashId { get; init; }
    public int MissionWaveNumber { get; init; } = -1;
    public string Direction { get; init; } = "STR_NORTH";
    public int Damage { get; init; }
    public int Shield { get; init; } = -1;
    public int ShieldRechargeHandle { get; init; }
    public int SecondsRemaining { get; init; }
    public bool Detected { get; init; }
    public bool HyperDetected { get; init; }
    public bool InBattlescape { get; init; }
    public bool HunterKiller { get; init; }
    public bool Escort { get; init; }
    public int HuntMode { get; init; }
    public int HuntBehavior { get; init; }
    public bool Hunting { get; init; }
    public bool Escorting { get; init; }
    public int SoftlockShotCounter { get; init; }
    public int FireCountdown { get; init; }
    public int EscapeCountdown { get; init; }
    public int Speed { get; init; }
    public double SpeedLongitude { get; init; }
    public double SpeedLatitude { get; init; }
    public double SpeedRadian { get; init; }
    /// <summary>The current destination; UFOs always have one, usually an anonymous waypoint.</summary>
    public WorldTargetReference? Destination { get; init; }
    /// <summary>The waypoint a hunting or escorting UFO returns to. Reference: <c>Ufo::_origWaypoint</c>.</summary>
    public WorldPosition? OriginalWaypoint { get; init; }
    public IReadOnlyList<ScriptValueEntry> ScriptValues { get; init; } = [];

    public WorldPosition Position => new(Longitude, Latitude);
}

/// <summary>Reference: <c>Savegame/Waypoint.cpp</c>; a bare globe marker crafts can be sent to.</summary>
public sealed record WaypointSnapshot(int Id, double Longitude, double Latitude)
{
    public string Name { get; init; } = string.Empty;

    public WorldPosition Position => new(Longitude, Latitude);
}

/// <summary>Reference: <c>Savegame/MissionSite.cpp</c>.</summary>
public sealed record MissionSiteSnapshot(
    int Id,
    string MissionRuleId,
    string DeploymentId,
    string Race,
    double Longitude,
    double Latitude,
    int SecondsRemaining)
{
    public string Name { get; init; } = string.Empty;
    public string CustomDeploymentId { get; init; } = string.Empty;
    public int Texture { get; init; } = -1;
    public string City { get; init; } = string.Empty;
    public bool Detected { get; init; }
    public bool InBattlescape { get; init; }
    /// <summary>The UFO that spawned this site and waits for it to despawn (<c>respawnUfoAfterSiteDespawn</c>).</summary>
    public int UfoUniqueId { get; init; }

    public WorldPosition Position => new(Longitude, Latitude);
}

/// <summary>Reference: <c>Savegame/AlienBase.cpp</c>.</summary>
public sealed record AlienBaseSnapshot(
    int Id,
    string DeploymentId,
    string Race,
    double Longitude,
    double Latitude)
{
    public string Name { get; init; } = string.Empty;
    public bool Discovered { get; init; }
    public bool InBattlescape { get; init; }
    public int StartMonth { get; init; }
    public int GenMissionCount { get; init; }
    public int MinutesSinceLastHuntMissionGeneration { get; init; }
    public string PactCountryId { get; init; } = string.Empty;

    public WorldPosition Position => new(Longitude, Latitude);
}

/// <summary>Reference: <c>Savegame/GeoscapeEvent.cpp</c>.</summary>
public sealed record GeoscapeEventSnapshot(string RuleId, int SpawnCountdown)
{
    /// <summary>GeoscapeEvent::_over: the countdown finished and the event is awaiting cleanup.</summary>
    public bool Over { get; init; }
}

/// <summary>Reference: <c>Savegame/AlienStrategy.cpp</c> save nodes.</summary>
public sealed record AlienStrategySnapshot
{
    public IReadOnlyList<KeyValuePair<string, ulong>> RegionChances { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, ulong>>>> RegionMissions { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, int>> MissionRuns { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, IReadOnlyList<AlienStrategyState.MissionLocation>>> MissionLocations { get; init; } = [];

    public static AlienStrategySnapshot Empty { get; } = new();

    public bool IsEmpty =>
        RegionChances.Count == 0 && RegionMissions.Count == 0 &&
        MissionRuns.Count == 0 && MissionLocations.Count == 0;
}

/// <summary>
/// The strategic world graph: alien missions and the targets they created, the waypoints the
/// player placed, and the alien strategy table. Owned by <see cref="CampaignWorld"/> and
/// captured with the rest of the campaign.
/// </summary>
public sealed record WorldSnapshot
{
    public IReadOnlyList<AlienMissionSnapshot> Missions { get; init; } = [];
    public IReadOnlyList<UfoSnapshot> Ufos { get; init; } = [];
    public IReadOnlyList<WaypointSnapshot> Waypoints { get; init; } = [];
    public IReadOnlyList<MissionSiteSnapshot> MissionSites { get; init; } = [];
    public IReadOnlyList<AlienBaseSnapshot> AlienBases { get; init; } = [];
    public IReadOnlyList<GeoscapeEventSnapshot> Events { get; init; } = [];
    public AlienStrategySnapshot Strategy { get; init; } = AlienStrategySnapshot.Empty;

    public static WorldSnapshot Empty { get; } = new();

    public bool IsEmpty =>
        Missions.Count == 0 && Ufos.Count == 0 && Waypoints.Count == 0 &&
        MissionSites.Count == 0 && AlienBases.Count == 0 && Events.Count == 0 && Strategy.IsEmpty;
}
