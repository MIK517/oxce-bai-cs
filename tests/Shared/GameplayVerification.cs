using System.Runtime.CompilerServices;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.TestSupport;

/// <summary>Turns on the gameplay self-checks that only tests run.</summary>
internal static class GameplayVerification
{
    /// <summary>Every test recomputes each shared per-tick flight projection read (ADR 0033).</summary>
    [ModuleInitializer]
    internal static void Enable() => CampaignWorld.VerifySharedFlights = true;
}
