using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public static class StrategicGeography
{
    /// <summary>Globe::getPolygonFromLonLat, including the reference vertex horizon rejection.</summary>
    public static int? TextureAt(RuntimeGlobe globe, double longitude, double latitude)
    {
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude)) throw new ArgumentOutOfRangeException(nameof(longitude));
        var coslat = Math.Cos(latitude);
        var sinlat = Math.Sin(latitude);
        foreach (var polygon in globe.Polygons)
        {
            var discarded = false;
            foreach (var p in polygon.Points)
                if (coslat * Math.Cos(p.Latitude) * Math.Cos(p.Longitude - longitude) + sinlat * Math.Sin(p.Latitude) < 0.75)
                { discarded = true; break; }
            if (discarded || polygon.Points.Count == 0) continue;
            var first = polygon.Points[0];
            var x = Math.Cos(first.Latitude) * Math.Sin(first.Longitude - longitude);
            var y = coslat * Math.Sin(first.Latitude) - sinlat * Math.Cos(first.Latitude) * Math.Cos(first.Longitude - longitude);
            var odd = false;
            for (var j = 0; j < polygon.Points.Count; j++)
            {
                var next = polygon.Points[(j + 1) % polygon.Points.Count];
                var x2 = Math.Cos(next.Latitude) * Math.Sin(next.Longitude - longitude);
                var y2 = coslat * Math.Sin(next.Latitude) - sinlat * Math.Cos(next.Latitude) * Math.Cos(next.Longitude - longitude);
                if ((y > 0) != (y2 > 0) && 0 < (x2 - x) * -y / (y2 - y) + x) odd = !odd;
                x = x2;
                y = y2;
            }
            if (odd) return polygon.Texture;
        }
        return null;
    }
}
