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
        switch (name)
        {
            case "GeoscapeGame.getCountries.init":
                args[1] = ScriptRuntimeValue.FromScalar(0);
                args[2] = ScriptRuntimeValue.FromScalar(receiver is null ? 0 : _countries.Count);
                break;
            case "GeoscapeGame.getCountries.list":
                if (receiver is null)
                {
                    args[1] = ScriptRuntimeValue.FromScalar(0);
                    args[2] = ScriptRuntimeValue.FromScalar(0);
                    args[3] = ScriptRuntimeValue.FromReference(null);
                }
                else
                {
                    var index = args[1].Scalar;
                    args[3] = ScriptRuntimeValue.FromReference((uint)index < (uint)_countries.Count ? _countries[index] : null);
                    args[1] = ScriptRuntimeValue.FromScalar(unchecked(index + 1));
                }
                break;
            case "Country.getRuleCountry":
                var country = receiver as CountryState;
                var definition = country is null ? null : _content.RuntimeRules.Countries[country.Rule].Value;
                if (definition is not null) _priceCountryIds.TryAdd(definition, _content.RuntimeRules.Countries.GetExternalId(country!.Rule));
                args[1] = ScriptRuntimeValue.FromReference(definition);
                break;
            default:
                args[1] = ScriptRuntimeValue.FromScalar(receiver switch
                {
                    CountryState owner => name switch
                    {
                        "Country.getPact" => owner.Pact ? 1 : 0,
                        "Country.getCurrentActivityXcom" => owner.ActivityXcom[^1],
                        "Country.getCurrentActivityAlien" => owner.ActivityAlien[^1],
                        _ => owner.Funding[^1],
                    },
                    RuntimeCountryRule rule => rule.FundingCap,
                    _ => 0,
                });
                break;
        }
        return ScriptBindingResult.Success;
    }
}
