using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>AlienMission::start's bounded operation-base search, without creation side effects.</summary>
internal static class WorldOperationBaseSpawn
{
    internal static WorldPosition? Select(RuntimeRegionRule region, RuntimeGlobe globe, int zone,
        int fakeWaterChance, bool allowWrongTextures, IRandomSource random) =>
        Select(region, zone, fakeWaterChance, allowWrongTextures, random,
            point => WorldGeometry.InsideLand(globe, point),
            point => WorldGeometry.InsideFakeUnderwaterTexture(globe, point));

    internal static WorldPosition? Select(RuntimeRegionRule region, int zone, int fakeWaterChance,
        bool allowWrongTextures, IRandomSource random,
        Func<WorldPosition, bool> insideLand, Func<WorldPosition, bool> insideFakeWater)
    {
        _ = WorldGeometry.MissionAreas(region, zone);
        var wantsFakeWater = RandomChance.Percent(random, fakeWaterChance);
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var point = WorldGeometry.RandomPoint(region, zone, -1, random);
            // The final attempt bypasses validity tests and is classified as
            // fallback even when this point would otherwise have been valid.
            if (attempt == 100) return allowWrongTextures ? point : null;
            if (insideLand(point) && WorldGeometry.InsideRegion(region, point, ignoreTechnicalRegion: true) &&
                insideFakeWater(point) == wantsFakeWater) return point;
        }
        throw new InvalidOperationException("Operation-base search exceeded its finite bound.");
    }
}
