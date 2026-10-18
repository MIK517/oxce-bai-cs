namespace Oxce.Gameplay.Campaigns;

/// <summary>One country's row in FundingState: this month's funding and its change from last month.</summary>
public sealed record CampaignCountryFunding(string CountryId, int Funding, int Change);

public interface ICampaignFundingQuery
{
    /// <summary>FundingState: every country's funding and change, in country order.</summary>
    IReadOnlyList<CampaignCountryFunding> QueryFunding();
}

public sealed partial class CampaignState : ICampaignFundingQuery
{
    IReadOnlyList<CampaignCountryFunding> ICampaignFundingQuery.QueryFunding() => Read(() => _countries
        .Select(country => new CampaignCountryFunding(_content.RuntimeRules.Countries.GetExternalId(country.Rule), country.Funding[^1],
            country.Funding.Count > 1 ? country.Funding[^1] - country.Funding[^2] : 0))
        .ToArray());
}
