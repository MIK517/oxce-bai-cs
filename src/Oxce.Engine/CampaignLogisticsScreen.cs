using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine;

/// <summary>Paused logistics interaction. Only opening an order executes price hooks.</summary>
public sealed class CampaignLogisticsScreen
{
    private readonly ICampaignQuery _queries;
    private readonly ICampaignCommandTarget _commands;
    private readonly Dictionary<int, int> _quantities = [];

    public CampaignLogisticsScreen(ICampaignQuery queries, ICampaignCommandTarget commands, int baseId)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        BaseId = baseId;
        Stores = queries.QueryStores(baseId);
    }

    public int BaseId { get; }
    public CampaignStores Stores { get; private set; }
    public LogisticsQuote? Quote { get; private set; }
    public bool ConfirmationPending { get; private set; }
    public string Message { get; private set; } = "";
    public long Total
    {
        get
        {
            if (Quote is null || _quantities.Count == 0) return 0;
            var subtotal = _quantities.Sum(p => (long)Quote.Rows[p.Key].UnitCost * p.Value);
            return Quote.Operation == LogisticsOperation.Transfer
                ? StrategicLogisticsMath.TransferTotal(checked((int)subtotal), Quote.TransferCostMultiplier, Quote.TransferCostDivisor)
                : subtotal;
        }
    }
    public int Quantity(int rowId) => _quantities.GetValueOrDefault(rowId);

    public bool OpenOrder(LogisticsOperation operation, int? destinationId = null)
    {
        Quote = null;
        ConfirmationPending = false;
        _quantities.Clear();
        var result = _commands.Execute(new PrepareLogisticsQuote(BaseId, operation, destinationId));
        Quote = result.Events.OfType<LogisticsQuoted>().SingleOrDefault()?.Quote;
        Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Reason ?? "";
        return Quote is not null;
    }

    public void SetQuantity(int rowId, int quantity)
    {
        if (Quote is null || (uint)rowId >= (uint)Quote.Rows.Count) throw new ArgumentOutOfRangeException(nameof(rowId));
        ConfirmationPending = false;
        var row = Quote.Rows[rowId];
        quantity = Math.Clamp(quantity, 0, row.MaximumQuantity);
        if (quantity == 0) _quantities.Remove(rowId);
        else _quantities[rowId] = quantity;
        Message = row.UnavailableReason ?? "";
    }

    public bool RequestConfirmation()
    {
        ConfirmationPending = Quote is not null && _quantities.Count > 0;
        if (!ConfirmationPending) Message = "Select a quantity first.";
        return ConfirmationPending;
    }

    public bool Confirm()
    {
        if (!ConfirmationPending || Quote is null) return false;
        ConfirmationPending = false;
        var result = _commands.Execute(new SubmitLogisticsOrder(Quote.Id,
            _quantities.OrderBy(p => p.Key).Select(p => new LogisticsSelection(p.Key, p.Value)).ToArray()));
        var completed = result.Events.OfType<LogisticsOrderCompleted>().SingleOrDefault();
        if (completed is null)
        {
            Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Reason ?? "Order failed.";
            return false;
        }
        Quote = null;
        _quantities.Clear();
        Stores = _queries.QueryStores(BaseId);
        Message = "Order completed.";
        return true;
    }

    public void Cancel()
    {
        if (ConfirmationPending) { ConfirmationPending = false; return; }
        Quote = null;
        _quantities.Clear();
        Message = "";
        Stores = _queries.QueryStores(BaseId);
    }
}
