namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Read-only craft input for GeoscapeState::ufoHuntingAndEscorting.</summary>
internal readonly record struct WorldHuntCraft(WorldTargetReference Reference, WorldPosition Position,
    int Attraction, bool Ignored, bool Undetectable, bool Active);

internal static class WorldHunting
{
    internal static bool TryAttraction(int mode, bool allowLanding, int units, int speed, int damage, int damageMaximum,
        out int attraction)
    {
        attraction = 0;
        if (units < 0 || mode == 0 && damageMaximum <= 0) return false;
        long value = 0;
        if (mode == 0)
        {
            if (allowLanding) value += 1_000_000;
            if (units > 0) value += 500_000L + units * 1000L;
            value += 100_000L - speed;
            value += damage * 100L / damageMaximum;
        }
        else
        {
            if (!allowLanding) value += 1_000_000;
            value += 500_000L - units * 1000L;
            value += 100_000L + speed;
        }
        if (value is < int.MinValue or > int.MaxValue) return false;
        attraction = (int)value;
        return true;
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
