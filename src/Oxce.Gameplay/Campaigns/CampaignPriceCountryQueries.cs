using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private readonly Dictionary<RuntimeCountryRule, string> _priceCountryIds = new(ReferenceEqualityComparer.Instance);

    private static bool PriceCountryQuerySupported(string name) => name is
        "GeoscapeGame.getCountries.init" or "GeoscapeGame.getCountries.list" or
        "Country.getRuleCountry" or "Country.getCurrentFunding" or "RuleCountry.getFundingCap" or
        "Country.getPact" or "Country.getCurrentActivityXcom" or "Country.getCurrentActivityAlien";

    private ScriptBindingResult InvokePriceCountryQuery(string name, Span<ScriptRuntimeValue> args, PriceForecastContext forecast)
    {
        var receiver = args[0].Reference;
        if (name == "Country.getCurrentActivityAlien" && receiver is CountryState && forecast.Time != Time)
            return ScriptBindingResult.Failure("Production prices require world alien-activity forecasts.");
        if (GeoscapeCountryQueries.IsList(name))
        {
            GeoscapeCountryQueries.InvokeList(name, args, _countries);
            return ScriptBindingResult.Success;
        }
        switch (name)
        {
            case "Country.getRuleCountry":
                var country = receiver as CountryState;
                var definition = country is null ? null : _content.RuntimeRules.Countries[country.Rule].Value;
                if (definition is not null) _priceCountryIds.TryAdd(definition, _content.RuntimeRules.Countries.GetExternalId(country!.Rule));
                args[1] = ScriptRuntimeValue.FromReference(definition);
                break;
            default:
                args[1] = ScriptRuntimeValue.FromScalar(receiver switch
                {
                    CountryState owner => GeoscapeCountryQueries.Value(name, owner),
                    RuntimeCountryRule rule => rule.FundingCap,
                    _ => 0,
                });
                break;
        }
        return ScriptBindingResult.Success;
    }
}
