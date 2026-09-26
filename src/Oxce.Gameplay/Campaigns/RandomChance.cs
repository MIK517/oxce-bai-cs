using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Reference: <c>Engine/RNG.h</c> <c>RandomState::percent</c>.</summary>
internal static class RandomChance
{
    /// <summary>
    /// RNG::percent always draws <c>generate(0, 99)</c>, so a chance of 0 or 100 or more still
    /// consumes a roll. Callers that skip the draw must do so where the reference does.
    /// </summary>
    public static bool Percent(IRandomSource random, int chance) => random.NextInclusive(0, 99) < chance;
}
