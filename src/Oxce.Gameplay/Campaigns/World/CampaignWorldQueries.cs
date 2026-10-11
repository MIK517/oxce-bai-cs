namespace Oxce.Gameplay.Campaigns.World;

/// <summary>A globe target as the player sees it. Detection state decides visibility.</summary>
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
}

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
}

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
