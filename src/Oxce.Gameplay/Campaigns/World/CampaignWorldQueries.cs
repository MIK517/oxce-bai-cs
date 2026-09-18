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
    public int SecondsRemaining { get; init; }
}

/// <summary>The alien activity the player can act on plus the scheduled work behind it.</summary>
public sealed record CampaignWorldOverview(
    IReadOnlyList<CampaignWorldTarget> Targets,
    int ActiveMissions,
    int ScheduledEvents);

public interface ICampaignWorldQuery
{
    CampaignWorldOverview QueryWorld();
}
