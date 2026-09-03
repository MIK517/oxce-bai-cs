namespace Oxce.Extensions.Abstractions;

public interface ICampaignExtension
{
    void Attach(IExtensionCampaignAccess campaign, CancellationToken cancellationToken);
    void OnEvent(ExtensionCampaignEvent campaignEvent);
    void Detach(CancellationToken cancellationToken);
}

public interface IExtensionCampaignAccess
{
    ExtensionCampaignOverview QueryOverview();
    ExtensionCampaignCommandResult Execute(ExtensionCampaignCommand command);
}

public sealed record ExtensionCampaignOverview(
    Guid Id,
    string Name,
    ExtensionCampaignTime Time,
    int DaysPassed,
    long Funds,
    int CountryCount,
    int RegionCount,
    IReadOnlyList<ExtensionCampaignBaseOverview> Bases);

public readonly record struct ExtensionCampaignTime(
    int Year,
    int Month,
    int Day,
    int Hour,
    int Minute,
    int Second);

public sealed record ExtensionCampaignBaseOverview(
    int Id,
    string Name,
    double Longitude,
    double Latitude,
    IReadOnlyList<ExtensionCampaignFacilityOverview> Facilities,
    int CraftCount,
    int SoldierCount,
    int ItemTypeCount,
    int Scientists,
    int Engineers)
{
    public bool IsPlaced => Name.Length != 0;
}

public sealed record ExtensionCampaignFacilityOverview(
    int X,
    int Y,
    int SizeX,
    int SizeY,
    int BuildTime);

public abstract record ExtensionCampaignCommand;

public sealed record ExtensionAdvanceCampaignTime(int FiveSecondTicks) : ExtensionCampaignCommand;

public sealed record ExtensionPlaceStartingBase(
    int BaseIndex,
    string Name,
    double Longitude,
    double Latitude) : ExtensionCampaignCommand;

public abstract record ExtensionCampaignEvent;

public sealed record ExtensionCampaignTimeAdvanced(
    ExtensionCampaignTime Previous,
    ExtensionCampaignTime Current,
    ExtensionCampaignTimeTriggerSummary Summary) : ExtensionCampaignEvent;

public readonly record struct ExtensionCampaignTimeTriggerSummary(
    int TickCount,
    int FiveSeconds,
    int TenMinutes,
    int ThirtyMinutes,
    int OneHour,
    int OneDay,
    int OneMonth);

public sealed record ExtensionStartingBasePlaced(
    int BaseIndex,
    string Name,
    double Longitude,
    double Latitude) : ExtensionCampaignEvent;

public sealed record ExtensionCampaignCommandResult(IReadOnlyList<ExtensionCampaignEvent> Events);
