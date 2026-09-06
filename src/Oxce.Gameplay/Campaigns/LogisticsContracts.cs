namespace Oxce.Gameplay.Campaigns;

public sealed record CraftArrivalServiceMessage(int BaseId, string CraftType, int CraftId, string Message) : ICampaignEvent;

public enum LogisticsOperation { Purchase, Sell, Transfer }

public sealed record PrepareLogisticsQuote(int BaseId, LogisticsOperation Operation, int? DestinationBaseId = null) : ICampaignCommand;
public sealed record LogisticsSelection(int RowId, int Quantity);
public sealed record SubmitLogisticsOrder(long QuoteId, IReadOnlyList<LogisticsSelection> Lines) : ICampaignCommand;
public sealed record LogisticsRow(int Id, CampaignTransferKind Kind, string RuleId, int EntityId,
    string Label, int Owned, int UnitCost, int MaximumQuantity, int Hours, string? UnavailableReason);
public sealed record LogisticsQuote(long Id, int BaseId, LogisticsOperation Operation, int? DestinationBaseId,
    long Funds, double UsedStores, int AvailableStores, int UsedQuarters, int AvailableQuarters,
    IReadOnlyList<LogisticsRow> Rows)
{
    public int TransferCostMultiplier { get; init; } = 1;
    public int TransferCostDivisor { get; init; } = 1;
}
public sealed record LogisticsQuoted(LogisticsQuote Quote) : ICampaignEvent;
public sealed record LogisticsOrderCompleted(int BaseId, LogisticsOperation Operation, long Cost) : ICampaignEvent;
public sealed record SuppliesArrived(int BaseId, IReadOnlyList<int> TransferIds) : ICampaignEvent;
