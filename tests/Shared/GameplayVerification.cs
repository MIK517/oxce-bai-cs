using System.Runtime.CompilerServices;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.TestSupport;

/// <summary>Turns on the gameplay self-checks that only tests run.</summary>
internal static class GameplayVerification
{
    /// <summary>
    /// Every test recomputes each shared per-tick flight projection read (ADR 0033) and compares every staged
    /// tick's shadow copy with the original (ADR 0035). With
    /// <c>OXCE_VERIFY_STAGING=1</c>, every tick is also staged on a shadow copy (ADR 0035); that run is slower and
    /// is made at slice validation rather than on every build.
    /// </summary>
    [ModuleInitializer]
    internal static void Enable()
    {
        CampaignWorld.VerifySharedFlights = true;
        CampaignState.VerifyStagedSnapshots = true;
        // Fixture-sized forecast work bounds; tests on real mods switch to the production bounds.
        CampaignWorld.FixtureWorkBoundsEverywhere = true;
        CampaignState.StageEveryTickEverywhere = Environment.GetEnvironmentVariable("OXCE_VERIFY_STAGING") == "1";
    }
}
