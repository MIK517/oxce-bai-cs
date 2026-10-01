using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>AlienMission::ufoLifting's landed/dormant transition, without state mutation.</summary>
internal static class WorldUfoLift
{
    internal static (UfoSnapshot Ufo, int Score) Advance(UfoSnapshot ufo, int speed,
        int missionPoints, RuntimeMissionObjective objective, bool fromSite)
    {
        var radians = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(ufo.Position, ufo.Destination!.Position, radians);
        return (ufo with
        {
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.VeryLow,
            // Ufo::think has already consumed the last five seconds for normal takeoff.
            // A site-linked ignored UFO never consumes its stored ground timer.
            SecondsRemaining = fromSite ? ufo.SecondsRemaining : 0,
            Speed = speed,
            SpeedRadian = radians,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
        }, missionPoints > 0 && objective is not (RuntimeMissionObjective.Base or RuntimeMissionObjective.Infiltration)
            ? missionPoints : 0);
    }
}
