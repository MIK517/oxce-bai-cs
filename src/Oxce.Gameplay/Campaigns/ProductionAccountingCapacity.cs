namespace Oxce.Gameplay.Campaigns;

/// <summary>Conservative signed capacity for every possible hourly sale and continuation.</summary>
internal struct ProductionAccountingCapacity(long funds, long income, long spending)
{
    private readonly Int128 _fundCreditCapacity = (Int128)long.MaxValue - funds;
    private readonly Int128 _fundDebitCapacity = (Int128)funds - long.MinValue;
    private readonly Int128 _incomeCapacity = (Int128)long.MaxValue - income;
    private readonly Int128 _spendingCapacity = (Int128)long.MaxValue - spending;
    private Int128 _credits;
    private Int128 _debits;

    public bool Reserve(long minimum, long maximum, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (minimum > maximum) throw new ArgumentException("Accounting bounds are reversed.");
        _credits += (Int128)Math.Max(0, maximum) * count;
        _debits -= (Int128)Math.Min(0, minimum) * count;
        return _credits <= _fundCreditCapacity && _credits <= _incomeCapacity &&
            _debits <= _fundDebitCapacity && _debits <= _spendingCapacity;
    }
}
