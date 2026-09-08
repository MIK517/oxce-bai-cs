namespace Oxce.Gameplay.Campaigns;

public interface ICampaignQuery
{
    CampaignOverview QueryOverview();
    CampaignStores QueryStores(int baseId);
}

public sealed record CampaignStoreItem(string RuleId, string Label, int Stored, int Incoming, double Size);
public sealed record CampaignIncomingTransfer(int Id, CampaignTransferKind Kind, string Label, int Quantity, int Hours);
public sealed record CampaignStores(int BaseId, double UsedStores, int AvailableStores, int UsedQuarters,
    int AvailableQuarters, IReadOnlyList<CampaignStoreItem> Items, IReadOnlyList<CampaignIncomingTransfer> Transfers)
{
    public string? CapacityLimitation { get; init; }
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
