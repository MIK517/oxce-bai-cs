namespace Oxce.Gameplay.Campaigns;

public interface ICampaignCommand;

public interface ICampaignCommandTarget
{
    CampaignCommandResult Execute(ICampaignCommand command);
}

public sealed record AdvanceCampaignTime(int FiveSecondTicks) : ICampaignCommand;

public sealed record PlaceStartingBase(int BaseIndex, string Name, double Longitude, double Latitude) : ICampaignCommand;

public interface ICampaignEvent;

public readonly record struct CampaignTimeTriggerSummary(
    int TickCount,
    int FiveSeconds,
    int TenMinutes,
    int ThirtyMinutes,
    int OneHour,
    int OneDay,
    int OneMonth)
{
    public int Count(CampaignTimeTrigger trigger) => trigger switch
    {
        CampaignTimeTrigger.FiveSeconds => FiveSeconds,
        CampaignTimeTrigger.TenMinutes => TenMinutes,
        CampaignTimeTrigger.ThirtyMinutes => ThirtyMinutes,
        CampaignTimeTrigger.OneHour => OneHour,
        CampaignTimeTrigger.OneDay => OneDay,
        CampaignTimeTrigger.OneMonth => OneMonth,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };
}

public readonly struct CampaignTimeTriggerSequence
{
    private readonly CampaignTime _previous;

    internal CampaignTimeTriggerSequence(CampaignTime previous, int count)
    {
        _previous = previous;
        Count = count;
    }

    public int Count { get; }

    public Enumerator GetEnumerator() => new(_previous, Count);

    public struct Enumerator
    {
        private CampaignTime _currentTime;
        private int _remaining;

        internal Enumerator(CampaignTime previous, int count)
        {
            _currentTime = previous;
            _remaining = count;
            Current = default;
        }

        public CampaignTimeTrigger Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining == 0) return false;
            _currentTime = _currentTime.Advance(out var trigger);
            Current = trigger;
            _remaining--;
            return true;
        }
    }
}

public sealed record CampaignTimeAdvanced(
    CampaignTime Previous,
    CampaignTime Current,
    CampaignTimeTriggerSummary Summary) : ICampaignEvent
{
    public CampaignTimeTriggerSequence Triggers => new(Previous, Summary.TickCount);
}

public sealed record StartingBasePlaced(
    int BaseIndex,
    string Name,
    double Longitude,
    double Latitude) : ICampaignEvent;

public sealed record CampaignCommandResult(IReadOnlyList<ICampaignEvent> Events);
