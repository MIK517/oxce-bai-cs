namespace Oxce.Gameplay.Campaigns.World;

/// <summary>A globe target as the player sees it. Detection state decides visibility.</summary>
/// <summary>
/// A target the player can see. <see cref="Label"/> is a custom name or the marker's language key,
/// which Target::getDefaultName formats with <see cref="Id"/> (the landing or crash id for those markers).
/// </summary>
public sealed record CampaignWorldTarget(
    WorldTargetKind Kind,
    int Id,
    string TypeId,
    string Label,
    double Longitude,
    double Latitude)
{
    public string Status { get; init; } = string.Empty;
    public string Altitude { get; init; } = string.Empty;
    public bool Detected { get; init; }
    /// <summary>Future UFO detection alerts are ignored until the campaign is reloaded.</summary>
    public bool DetectionAlertIgnored { get; init; }
    public int SecondsRemaining { get; init; }
    public WorldTargetReference Reference { get; init; } = new(Kind, TypeId, Id, Longitude, Latitude);
    /// <summary>Reason interception or landing cannot yet execute; pursuit may still be available.</summary>
    public string? UnavailableActionReason { get; init; }
    /// <summary>UFO radar circle inputs (Globe::drawRadars): hunter-killer, hyper-wave detection and radar range in nautical miles.</summary>
    public bool HunterKiller { get; init; }
    public bool HyperDetected { get; init; }
    public int RadarRange { get; init; }
    /// <summary>A hunting UFO's current destination, for its flight path (Globe::drawFlights).</summary>
    public bool Hunting { get; init; }
    public WorldPosition? Destination { get; init; }
    /// <summary>The classified <see cref="UnavailableActionReason"/>.</summary>
    public CampaignReason? ActionUnavailable { get; init; }
    /// <summary>UfoDetectedState's lists for a UFO; absent for other targets.</summary>
    public CampaignWorldUfoDetails? Ufo { get; init; }
    /// <summary>
    /// AlienBaseState's location for an alien base: the first country and region containing it
    /// in saved order ("" when none), and its deployment's discovery message ("" for the default).
    /// </summary>
    public string CountryId { get; init; } = string.Empty;
    public string RegionId { get; init; } = string.Empty;
    public string DiscoveredMessage { get; init; } = string.Empty;
}

/// <summary>A craft weapon slot for the craft screen: weapon rule and rounds.</summary>
public sealed record CampaignWorldCraftWeapon(string RuleId, int Ammo, int AmmoMaximum);

public sealed record CampaignWorldBase(int Id, string Name, double Longitude, double Latitude)
{
    /// <summary>The largest completed facility radar below 10,000 nautical miles (Globe::drawRadars).</summary>
    public int RadarRange { get; init; }
}

public sealed record CampaignWorldCraft(int BaseId, string TypeId, int Id, string Name, string Status,
    double Longitude, double Latitude, int Fuel, WorldTargetReference? Destination,
    string? DispatchUnavailableReason, string? PatrolRecallUnavailableReason)
{
    /// <summary>Craft::getCraftStats().radarRange in nautical miles, including weapon bonuses.</summary>
    public int RadarRange { get; init; }
    public CampaignReason? DispatchUnavailable { get; init; }
    public CampaignReason? PatrolRecallUnavailable { get; init; }
    /// <summary>GeoscapeCraftState details: condition, performance, armament and crew.</summary>
    public int FuelMaximum { get; init; }
    public int Damage { get; init; }
    public int DamageMaximum { get; init; }
    public int Shield { get; init; }
    public int ShieldMaximum { get; init; }
    public int Speed { get; init; }
    public int SpeedMaximum { get; init; }
    public IReadOnlyList<CampaignWorldCraftWeapon?> Weapons { get; init; } = [];
    public int Soldiers { get; init; }
    public int Vehicles { get; init; }
    public bool LowFuel { get; init; }
    public bool MissionComplete { get; init; }
    /// <summary>RuleCraft::canAutoPatrol: Patrol becomes auto-patrol.</summary>
    public bool CanAutoPatrol { get; init; }
    /// <summary>
    /// Craft::getBaseRange in radians: how far the craft can fly out and still return, drawn while a
    /// grounded craft picks a destination. Infinite when the craft burns no fuel at maximum speed.
    /// </summary>
    public double BaseRange { get; init; } = double.PositiveInfinity;
}

/// <summary>
/// UfoDetectedState: size, heading (STR_NONE_UC unless flying) and speed, then the hyper-wave
/// decoded craft type, race, mission rule and mission region.
/// </summary>
public sealed record CampaignWorldUfoDetails(string Size, string Heading, int Speed, string Race, string Mission, string Region);

/// <summary>The alien activity the player can act on plus the scheduled work behind it.</summary>
public sealed record CampaignWorldOverview(
    IReadOnlyList<CampaignWorldTarget> Targets,
    int ActiveMissions,
    int ScheduledEvents)
{
    public IReadOnlyList<CampaignWorldBase> Bases { get; init; } = [];
    public IReadOnlyList<CampaignWorldCraft> Crafts { get; init; } = [];
    /// <summary>The campaign's country and region rules in saved order, as Globe::drawDetail visits them.</summary>
    public IReadOnlyList<string> CountryIds { get; init; } = [];
    public IReadOnlyList<string> RegionIds { get; init; } = [];
}

public interface ICampaignWorldQuery
{
    CampaignWorldOverview QueryWorld();
}
