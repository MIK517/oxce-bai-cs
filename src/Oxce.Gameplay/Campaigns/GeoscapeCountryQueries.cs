using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>
/// Read-only country queries shared by the geoscape script callers (price and detection scripts).
/// Callers keep their own support lists and forecast restrictions; this owns only the native
/// semantics. Reference: <c>SavedGame::getCountries</c> script bindings and <c>Country</c> getters.
/// </summary>
internal static class GeoscapeCountryQueries
{
    public static bool IsList(string name) =>
        name is "GeoscapeGame.getCountries.init" or "GeoscapeGame.getCountries.list";

    /// <summary>
    /// The <c>getCountries</c> loop protocol: <c>init</c> yields (index 0, count) and <c>list</c>
    /// yields the country at the index and advances it. A null game yields an empty loop.
    /// </summary>
    public static void InvokeList(string name, Span<ScriptRuntimeValue> args, IReadOnlyList<CampaignState.CountryState> countries)
    {
        var receiver = args[0].Reference;
        if (name == "GeoscapeGame.getCountries.init")
        {
            args[1] = ScriptRuntimeValue.FromScalar(0);
            args[2] = ScriptRuntimeValue.FromScalar(receiver is null ? 0 : countries.Count);
            return;
        }
        if (receiver is null)
        {
            args[1] = ScriptRuntimeValue.FromScalar(0);
            args[2] = ScriptRuntimeValue.FromScalar(0);
            args[3] = ScriptRuntimeValue.FromReference(null);
            return;
        }
        var index = args[1].Scalar;
        args[3] = ScriptRuntimeValue.FromReference((uint)index < (uint)countries.Count ? countries[index] : null);
        args[1] = ScriptRuntimeValue.FromScalar(unchecked(index + 1));
    }

    /// <summary>The current-month scalar getters of a live country.</summary>
    public static int Value(string name, CampaignState.CountryState country) => name switch
    {
        "Country.getPact" => country.Pact ? 1 : 0,
        "Country.getCurrentFunding" => country.Funding[^1],
        "Country.getCurrentActivityXcom" => country.ActivityXcom[^1],
        "Country.getCurrentActivityAlien" => country.ActivityAlien[^1],
        _ => throw new InvalidOperationException($"Country provider '{name}' was not preflighted."),
    };
}
