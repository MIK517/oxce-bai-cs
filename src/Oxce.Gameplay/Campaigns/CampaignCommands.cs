namespace Oxce.Gameplay.Campaigns;

public interface ICampaignCommand;

public interface ICampaignCommandTarget
{
    CampaignCommandResult Execute(ICampaignCommand command);
}

/// <summary>Reference geoscape selection, independent of command batch length.</summary>
public enum CampaignTimeSpeed { FiveSeconds, OneMinute, FiveMinutes, ThirtyMinutes, OneHour, OneDay }

public sealed record AdvanceCampaignTime(int FiveSecondTicks) : ICampaignCommand
{
    public CampaignTimeSpeed SelectedSpeed { get; init; } = CampaignTimeSpeed.FiveSeconds;
}

public sealed record PlaceStartingBase(int BaseIndex, string Name, double Longitude, double Latitude) : ICampaignCommand;

public interface ICampaignEvent;

/// <summary>
/// A refused command or tick. <see cref="Code"/> is a stable classification and
/// <see cref="TextKey"/> the reference language key when the reference shows this refusal.
/// </summary>
public sealed record CampaignActionBlocked(string Reason) : ICampaignEvent
{
    public string Code { get; init; } = CampaignReason.Unclassified;
    public string? TextKey { get; init; }

    public static CampaignActionBlocked From(CampaignReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new(reason.Text) { Code = reason.Code, TextKey = reason.TextKey };
    }
}

/// <summary>Why an action is unavailable: a stable code, English text and, where one exists, the reference text key.</summary>
public sealed record CampaignReason(string Code, string Text)
{
    public const string Unclassified = "unclassified";
    public string? TextKey { get; init; }
}

/// <summary>A newly detected UFO contact; the reference pauses for its alert.</summary>
public sealed record UfoContactDetected(int UniqueId, bool Hyperwave) : ICampaignEvent;

/// <summary>A detected UFO lost at arrival with followers; reported before the flight pass returns.</summary>
public sealed record UfoContactLost(int UniqueId) : ICampaignEvent;

/// <summary>A facility sight check discovered an alien base; patrol discovery is silent.</summary>
public sealed record AlienBaseDiscovered(int Id, string DeploymentId) : ICampaignEvent;

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
    public CampaignTimeSpeed SelectedSpeed { get; init; } = CampaignTimeSpeed.FiveSeconds;
}

public sealed record StartingBasePlaced(
    int BaseIndex,
    string Name,
    double Longitude,
    double Latitude) : ICampaignEvent;

public sealed record CampaignCommandResult(IReadOnlyList<ICampaignEvent> Events);
