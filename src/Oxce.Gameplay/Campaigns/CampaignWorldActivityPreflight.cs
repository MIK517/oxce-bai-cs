namespace Oxce.Gameplay.Campaigns;

/// <summary>World-owned country activity at the hourly production price caller.</summary>
internal interface ICampaignWorldActivityPreflight
{
    string? CountryAfterDailyProgress(CampaignState.CountryState country,
        IReadOnlySet<string>? research, IReadOnlySet<string>? possibleResearch,
        ref long work, out int activity, out bool needsResearch);
}

public sealed partial class CampaignState
{
    internal ICampaignWorldActivityPreflight WorldActivityPreflight =>
        _handlers.Operation<ICampaignWorldActivityPreflight>();
}
