namespace Oxce.Gameplay.Campaigns.World;

/// <summary>AlienMission::ufoReachedWaypoint's legacy wave fallback at the caller's current counter.</summary>
internal static class WorldMissionWave
{
    internal static int Infer(int savedWave, int nextWave, int waveCount)
    {
        var inferred = savedWave > -1 ? savedWave : nextWave - 1;
        return inferred < 0 ? waveCount - 1 : inferred;
    }
}
