using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Bounded proof of SavedGame::locateRegion over a rectangular birth area.</summary>
internal static class WorldGenerationRegions
{
    internal static IReadOnlyList<(string Id, WorldPosition Position)> Candidates(MissionArea area,
        IReadOnlyList<(string Id, RuntimeRegionRule Rule)> regions, ref long work)
    {
        var left = Math.Min(area.LongitudeMinimum, area.LongitudeMaximum);
        var right = Math.Max(area.LongitudeMinimum, area.LongitudeMaximum);
        var bottom = Math.Min(area.LatitudeMinimum, area.LatitudeMaximum);
        var top = Math.Max(area.LatitudeMinimum, area.LatitudeMaximum);
        if (left < 0 || right >= 2 * Math.PI || bottom < -Math.PI / 2 || top > Math.PI / 2)
            throw new InvalidDataException("New base hunting requires normalized source-region bounds.");
        var longitudes = new SortedSet<double> { left, right };
        var latitudes = new SortedSet<double> { bottom, top, Math.Clamp(0, bottom, top) };
        long scans = regions.Count;
        foreach (var (_, rule) in regions)
            foreach (var boundary in rule.Areas)
            {
                if (++work > 1_000_000) throw new InvalidDataException("New base hunting region forecasts exceed the supported work limit.");
                scans++;
                Add(longitudes, boundary.LongitudeMinimum, left, right);
                Add(longitudes, boundary.LongitudeMaximum, left, right);
                Add(latitudes, boundary.LatitudeMinimum, bottom, top);
                Add(latitudes, boundary.LatitudeMaximum, bottom, top);
            }
        var xs = Samples(longitudes);
        var ys = Samples(latitudes);
        work += (long)xs.Count * ys.Count * scans;
        if (work > 1_000_000) throw new InvalidDataException("New base hunting region forecasts exceed the supported work limit.");
        var hits = new Dictionary<string, WorldPosition>(StringComparer.Ordinal);
        foreach (var x in xs)
            foreach (var y in ys)
            {
                var position = new WorldPosition(x, y);
                var found = false;
                foreach (var (id, rule) in regions)
                {
                    if (!WorldGeometry.InsideRegion(rule, position)) continue;
                    hits.TryAdd(id, position);
                    found = true;
                    break;
                }
                if (!found) throw new InvalidDataException("New base hunting cannot locate every possible source region.");
            }
        return regions.Where(region => hits.ContainsKey(region.Id)).Select(region => (region.Id, hits[region.Id])).ToArray();
    }

    private static void Add(SortedSet<double> values, double value, double minimum, double maximum)
    {
        if (value >= minimum && value <= maximum) values.Add(value);
    }

    private static List<double> Samples(SortedSet<double> boundaries)
    {
        var result = new List<double>(boundaries.Count * 2 - 1);
        double? previous = null;
        foreach (var value in boundaries)
        {
            if (previous is { } lower) result.Add(lower + (value - lower) / 2);
            result.Add(value);
            previous = value;
        }
        return result;
    }
}
