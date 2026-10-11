namespace Oxce.Gameplay.Campaigns;

/// <summary>Signed score headroom; uncertain opposite-sign effects cannot cancel reservations.</summary>
internal struct ResearchScoreCapacity(int score)
{
    private long _positiveCapacity = (long)int.MaxValue - score;
    private long _negativeCapacity = (long)score - int.MinValue;
    private Int128 _positive;
    private Int128 _negative;

    // Only a guaranteed, ordered transition can move the score baseline.
    // Optional production completions must continue using Reserve instead.
    public bool AdvanceGuaranteed(long minimumPrefix, long maximumPrefix, long minimumFinal, long maximumFinal)
    {
        if (minimumPrefix > 0 || maximumPrefix < 0 || minimumFinal > maximumFinal ||
            minimumFinal < minimumPrefix || maximumFinal > maximumPrefix)
            throw new ArgumentException("Guaranteed score bounds are inconsistent.");
        var prefix = this;
        if (!prefix.Reserve(minimumPrefix, maximumPrefix)) return false;
        _positiveCapacity = checked(_positiveCapacity - maximumFinal);
        _negativeCapacity = checked(_negativeCapacity + minimumFinal);
        return true;
    }

    public bool Reserve(long minimum, long maximum, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (minimum > maximum) throw new ArgumentException("Score bounds are reversed.");
        _positive += (Int128)Math.Max(0, maximum) * count;
        _negative -= (Int128)Math.Min(0, minimum) * count;
        return _positive <= _positiveCapacity && _negative <= _negativeCapacity;
    }
}
