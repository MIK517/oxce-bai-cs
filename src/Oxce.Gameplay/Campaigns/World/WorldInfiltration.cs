namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Country::canBeInfiltrated; pending pacts do not sign until month-end.</summary>
internal static class WorldInfiltration
{
    internal static bool CanInfiltrate(bool pact, bool newPact, bool cancelPact) =>
        !pact && !newPact || pact && cancelPact;
}
