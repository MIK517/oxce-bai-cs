namespace Oxce.Gameplay.Campaigns;

/// <summary>Signed score headroom; uncertain opposite-sign effects cannot cancel reservations.</summary>
internal struct ResearchScoreCapacity(int score)
{
    private readonly long _positiveCapacity = (long)int.MaxValue - score;
    private readonly long _negativeCapacity = (long)score - int.MinValue;
    private Int128 _positive;
    private Int128 _negative;

    public bool Reserve(long minimum, long maximum, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (minimum > maximum) throw new ArgumentException("Score bounds are reversed.");
        _positive += (Int128)Math.Max(0, maximum) * count;
        _negative -= (Int128)Math.Min(0, minimum) * count;
        return _positive <= _positiveCapacity && _negative <= _negativeCapacity;
    }
}
