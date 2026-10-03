using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Read-only craft input for GeoscapeState::ufoHuntingAndEscorting.</summary>
internal readonly record struct WorldHuntCraft(WorldTargetReference Reference, WorldPosition Position,
    int Attraction, bool Ignored, bool Undetectable, bool Active);

internal static class WorldHunting
{
    internal static (bool HunterKiller, int Mode, int Behavior) Initialize(int percentage, int mode,
        int behavior, IRandomSource random)
    {
        if (percentage <= 0 || random.NextInclusive(0, 99) >= percentage) return (false, 0, 0);
        return (true, mode > 1 ? random.NextInclusive(0, 1) : mode,
            behavior > 1 ? random.NextInclusive(0, 1) : behavior);
    }

    internal static bool IsIgnored(bool lowFuel, bool missionComplete) => lowFuel || missionComplete;

    internal static bool RetargetDue(CampaignTimeTrigger highest, CampaignTimeSpeed speed, bool fast) =>
        highest >= CampaignTimeTrigger.TenMinutes || fast &&
        speed is CampaignTimeSpeed.FiveSeconds or CampaignTimeSpeed.OneMinute;

    internal static bool TryHunt(UfoSnapshot ufo, WorldTargetReference target, int speedMaximum, int percentage,
        out UfoSnapshot hunted)
    {
        hunted = ufo;
        var speed = ufo.Speed;
        if (percentage > 0)
        {
            var product = (long)speedMaximum * percentage;
            if (product is < 0 or > int.MaxValue) return false;
            speed = (int)(product / 100);
        }
        if (!ufo.Hunting && !ufo.Escorting && ufo.Destination is null) return false;
        hunted = ufo with
        {
            OriginalWaypoint = ufo.Hunting || ufo.Escorting ? ufo.OriginalWaypoint : ufo.Destination!.Position,
            Destination = target,
            Hunting = true,
            Escorting = false,
            Speed = speed,
            SpeedRadian = WorldGeometry.RadianSpeed(speed),
            SpeedLongitude = 0,
            SpeedLatitude = 0,
        };
        return true;
    }

    internal static UfoSnapshot Escort(UfoSnapshot ufo, WorldTargetReference target, int speedMaximum) => ufo with
    {
        OriginalWaypoint = ufo.Hunting || ufo.Escorting ? ufo.OriginalWaypoint : ufo.Destination!.Position,
        Destination = target,
        Hunting = false,
        Escorting = true,
        Speed = speedMaximum,
        SpeedRadian = WorldGeometry.RadianSpeed(speedMaximum),
        SpeedLongitude = 0,
        SpeedLatitude = 0,
    };

    internal static UfoSnapshot? EscortLeader(int missionId, IReadOnlyList<UfoSnapshot> ufos)
    {
        foreach (var target in ufos)
            if (target.MissionId == missionId && !target.HunterKiller) return target;
        return null;
    }

    internal static UfoSnapshot InitializeEscort(UfoSnapshot ufo, bool requested, int speedMaximum,
        IReadOnlyList<UfoSnapshot> ufos)
    {
        if (!ufo.HunterKiller || !requested) return ufo;
        ufo = ufo with { Escort = true };
        if (EscortLeader(ufo.MissionId, ufos) is not { } target) return ufo;
        ufo = ufo with { Longitude = target.Longitude, Latitude = target.Latitude };
        return Escort(ufo, new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id,
            target.Longitude, target.Latitude) { UniqueId = target.UniqueId }, speedMaximum);
    }

    internal static UfoSnapshot Restore(UfoSnapshot ufo) => ufo.OriginalWaypoint is { } original ? ufo with
    {
        Destination = WorldTargetReference.ForWaypoint(0, original),
        OriginalWaypoint = null,
        Hunting = false,
        Escorting = false,
    } : ufo;

    internal static bool TryAttraction(int mode, bool allowLanding, int units, int speed, int damage, int damageMaximum,
        out int attraction)
    {
        attraction = 0;
        if (units < 0 || mode == 0 && damageMaximum <= 0) return false;
        var unitTerm = units * 1000L;
        if (!Fits(unitTerm)) return false;
        long value = allowLanding == (mode == 0) ? 1_000_000 : 0;
        if (mode == 0)
        {
            if (units > 0)
            {
                var capacityTerm = 500_000L + unitTerm;
                if (!Fits(capacityTerm) || !Fits(value += capacityTerm)) return false;
            }
            var speedTerm = 100_000L - speed;
            var damageTerm = damage * 100L;
            if (!Fits(speedTerm) || !Fits(value += speedTerm) || !Fits(damageTerm)) return false;
            value += damageTerm / damageMaximum;
        }
        else
        {
            var capacityTerm = 500_000L - unitTerm;
            var speedTerm = 100_000L + speed;
            if (!Fits(capacityTerm) || !Fits(value += capacityTerm) || !Fits(speedTerm)) return false;
            value += speedTerm;
        }
        if (!Fits(value)) return false;
        attraction = (int)value;
        return true;

        static bool Fits(long candidate) => candidate is >= int.MinValue and <= int.MaxValue;
    }

    internal static WorldTargetReference? Select(UfoSnapshot ufo, int radarRange, IReadOnlyList<WorldHuntCraft> crafts)
    {
        var attraction = int.MaxValue;
        WorldTargetReference? selected = null;
        if (ufo.Hunting && ufo.Destination is { Kind: WorldTargetKind.Craft } original)
            foreach (var craft in crafts)
            {
                if (craft.Reference.TypeId != original.TypeId || craft.Reference.Id != original.Id) continue;
                if (!craft.Ignored && InRange(craft))
                {
                    selected = craft.Reference;
                    attraction = craft.Attraction;
                }
                break;
            }
        foreach (var craft in crafts)
        {
            if (!craft.Active || craft.Ignored || craft.Undetectable || craft.Attraction >= attraction || !InRange(craft)) continue;
            selected = craft.Reference;
            attraction = craft.Attraction;
        }
        return selected;

        bool InRange(WorldHuntCraft craft) => radarRange != 0 &&
            WorldGeometry.Distance(ufo.Position, craft.Position) <= WorldGeometry.Nautical(radarRange);
    }
}
