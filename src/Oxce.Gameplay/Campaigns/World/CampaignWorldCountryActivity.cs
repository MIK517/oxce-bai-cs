namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld : ICampaignWorldActivityPreflight
{
    string? ICampaignWorldActivityPreflight.CountryAfterDailyProgress(CampaignState.CountryState country,
        IReadOnlySet<string>? research, IReadOnlySet<string>? possibleResearch,
        ref long work, out int activity, out bool needsResearch)
    {
        activity = country.ActivityAlien[^1];
        needsResearch = false;
        var rules = campaign.Content.RuntimeRules;
        foreach (var alienBase in _alienBases)
        {
            if (++work > MaximumWorldWork) return ForecastLimits.ProductionCountryActivityForecastsWork;
            var rule = AlienBaseRule(alienBase);
            if (rule.Points == 0) continue;
            CampaignState.CountryState? scored = null;
            foreach (var candidate in campaign.CountryStates)
            {
                var countryRule = rules.Countries[candidate.Rule].Value;
                work += countryRule.Areas.Count + 1L;
                if (work > MaximumWorldWork) return ForecastLimits.ProductionCountryActivityForecastsWork;
                if (!WorldGeometry.InsideCountry(countryRule, alienBase.Position)) continue;
                scored = candidate;
                break;
            }
            if (!ReferenceEquals(scored, country)) continue;
            if (rule.BaseSelfDestructCodeId is { Length: > 0 } topic)
            {
                if (research is null) { needsResearch = true; return null; }
                var destroyed = research.Contains(topic);
                if (possibleResearch is not null && destroyed != possibleResearch.Contains(topic))
                    return "Production prices require uncertain alien-base deletion forecasts.";
                if (destroyed) continue;
            }
            var next = (long)activity + rule.Points;
            if (next is < int.MinValue or > int.MaxValue)
                return "Production country alien activity exceeds the supported range.";
            activity = (int)next;
        }
        return null;
    }
}
