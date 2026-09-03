namespace Oxce.Gameplay.Campaigns;

public interface ICampaignCommand;

public sealed record AdvanceCampaignTime(int FiveSecondTicks) : ICampaignCommand;

public sealed record PlaceStartingBase(int BaseIndex, string Name, double Longitude, double Latitude) : ICampaignCommand;

public interface ICampaignEvent;

public sealed record CampaignTimeAdvanced(
    CampaignTime Previous,
    CampaignTime Current,
    IReadOnlyList<CampaignTimeTrigger> Triggers) : ICampaignEvent;

public sealed record StartingBasePlaced(
    int BaseIndex,
    string Name,
    double Longitude,
    double Latitude) : ICampaignEvent;

public sealed record CampaignCommandResult(IReadOnlyList<ICampaignEvent> Events);
