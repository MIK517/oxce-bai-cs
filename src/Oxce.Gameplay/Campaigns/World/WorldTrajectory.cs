using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Trajectory and wave timing arithmetic. Reference: <c>Mod/UfoTrajectory.h::applySpeedPercentage</c>
/// and the spawn countdown statements in <c>Savegame/AlienMission.cpp</c>.
/// </summary>
public static class WorldTrajectory
{
    /// <summary>UfoTrajectory::applySpeedPercentage.</summary>
    public static int Speed(int baseSpeed, int percentage) => checked((int)((long)baseSpeed * percentage / 100));

    /// <summary>The half-hour steps of a wave spawn timer; the reference divides before rolling.</summary>
    public static int SpawnTimerSteps(int spawnTimer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spawnTimer);
        return spawnTimer / 30;
    }

    /// <summary>AlienMission::start and think: <c>(spawnTimer/2 + RNG::generate(0, spawnTimer)) * 30</c>.</summary>
    public static int SpawnCountdown(int spawnTimer, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var steps = SpawnTimerSteps(spawnTimer);
        return checked((steps / 2 + random.NextInclusive(0, steps)) * 30);
    }

    /// <summary>AlienMission::ufoShotDown: the delay added to the next wave after a loss.</summary>
    public static int ShotDownDelay(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return 30 * (random.NextInclusive(0, 400) + 48);
    }
}
