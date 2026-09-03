namespace Oxce.Gameplay.Campaigns;

public interface ICampaignQuery
{
    CampaignOverview QueryOverview();
}

public sealed record CampaignOverview(
    CampaignId Id,
    string Name,
    CampaignTime Time,
    int DaysPassed,
    long Funds,
    int CountryCount,
    int RegionCount,
    IReadOnlyList<CampaignBaseOverview> Bases);

public sealed record CampaignBaseOverview(
    int Id,
    string Name,
    double Longitude,
    double Latitude,
    IReadOnlyList<CampaignFacilityOverview> Facilities,
    int CraftCount,
    int SoldierCount,
    int ItemTypeCount,
    int Scientists,
    int Engineers)
{
    public bool IsPlaced => Name.Length != 0;
}

public sealed record CampaignFacilityOverview(
    int X,
    int Y,
    int SizeX,
    int SizeY,
    int BuildTime);
