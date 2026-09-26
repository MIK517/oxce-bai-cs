using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>SavedGame::spawnEvent and GeoscapeEvent::think at reference revision 4df3a5e.</summary>
public static class WorldEventTiming
{
    public static bool IsValid(int timer, int timerRandom) =>
        timerRandom >= 0 && (long)timer + timerRandom <= int.MaxValue;

    public static int SpawnCountdown(int timer, int timerRandom, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!IsValid(timer, timerRandom)) throw new InvalidDataException("Strategic event timer exceeds the supported range.");
        return Math.Max(60, checked(timer + random.NextInclusive(0, timerRandom)) / 30 * 30);
    }

    public static GeoscapeEventSnapshot Think(GeoscapeEventSnapshot scheduled) =>
        scheduled.Over ? scheduled : scheduled.SpawnCountdown > 30
            ? scheduled with { SpawnCountdown = scheduled.SpawnCountdown - 30 }
            : scheduled with { Over = true };
}
