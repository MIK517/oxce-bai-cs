using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// Draws the globe into an indexed surface as Globe::draw and Globe::blit compose it: ocean and
/// textured land; radar circles and flight paths shaded from that unshaded land onto their own
/// layer; the day/night shadow; then the radar layer, the detail layer (borders, labels, cities)
/// and the markers on top. Projected polygons are cached until the view moves; drawing allocates
/// nothing.
/// </summary>
public sealed class GlobeRenderer
{
    private readonly GlobeView _view;
    private readonly GeoscapeAssets _assets;
    private readonly RuntimeGlobe _globe;
    private readonly IndexedPoint[][] _projected;
    private readonly bool[] _visible;
    private readonly int[] _scratch;
    private readonly int _textureFrames;
    private readonly IndexedSurface _radars;
    private readonly IndexedSpriteFont? _font;
    private long _cachedRevision = -1;

    public GlobeRenderer(GlobeView view, GeoscapeAssets assets, RuntimeGlobe globe, IndexedSpriteFont? font = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(globe);
        _view = view;
        _assets = assets;
        _globe = globe;
        Surface = new IndexedSurface(view.Width, view.Height);
        _radars = new IndexedSurface(view.Width, view.Height);
        _font = font;
        _projected = new IndexedPoint[globe.Polygons.Count][];
        var maximumPoints = 3;
        for (var index = 0; index < _projected.Length; index++)
        {
            _projected[index] = new IndexedPoint[globe.Polygons[index].Points.Count];
            maximumPoints = Math.Max(maximumPoints, _projected[index].Length);
        }
        _visible = new bool[_projected.Length];
        _scratch = new int[maximumPoints];
        // SurfaceSet::getTotalFrames counts up to the highest frame a mod added.
        foreach (var frame in assets.Textures.Keys) _textureFrames = Math.Max(_textureFrames, frame + 1);
    }

    public IndexedSurface Surface { get; }

    /// <param name="craftRange">Globe::setCraftRange: the selected craft's fuel range while choosing a destination.</param>
    public void Render(GlobeScene scene, CampaignTime time, GlobeRenderOptions options, bool blinkOn,
        GlobeCircle? craftRange = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(options);
        var colors = _assets.Colors;
        Surface.Clear(0);
        _radars.Clear(0);
        Surface.FillCircle(_view.CenterX + _view.PixelScale, _view.CenterY,
            (int)_view.Radius + 20 * _view.PixelScale, colors.Ocean);
        DrawLand();
        if (options.RadarLines) DrawRadars(scene, craftRange);
        if (options.FlightPaths) DrawFlights(scene);
        DrawShadow(time, options.Seasons);
        Surface.Blit(_radars, 0, 0);
        if (options.Detail) DrawDetail(scene);
        DrawMarkers(scene, blinkOn);
    }

    private void DrawRadars(GlobeScene scene, GlobeCircle? craftRange)
    {
        if (craftRange is { } range && range.Range > 0 && range.Range < Math.PI)
        {
            DrawCircle(range with { Segments = 64, Stride = 1 });
            DrawCircle(range with { Range = range.Range - 0.025, Segments = 64, Stride = 2 });
        }
        var radars = scene.Radars;
        for (var index = 0; index < radars.Count; index++) DrawCircle(radars[index]);
    }

    /// <summary>Globe::drawGlobeCircle.</summary>
    private void DrawCircle(GlobeCircle circle)
    {
        double x2 = 0, y2 = 0;
        var segment = Math.PI / (circle.Segments / 2.0);
        var drawn = 0;
        var stride = Math.Max(1, circle.Stride);
        for (var azimuth = 0.0; azimuth <= Math.PI * 2 + 0.01; azimuth += segment)
        {
            var latitude = Math.Asin(Math.Sin(circle.Latitude) * Math.Cos(circle.Range) +
                Math.Cos(circle.Latitude) * Math.Sin(circle.Range) * Math.Cos(azimuth));
            var longitude = circle.Longitude + Math.Atan2(Math.Sin(azimuth) * Math.Sin(circle.Range) * Math.Cos(circle.Latitude),
                Math.Cos(circle.Range) - Math.Sin(circle.Latitude) * Math.Sin(latitude));
            var (x, y) = _view.ProjectPrecise(longitude, latitude);
            if (azimuth == 0)
            {
                (x2, y2) = (x, y);
                continue;
            }
            if (!_view.IsBack(longitude, latitude) && drawn % stride == 0) ShadedLine(x, y, x2, y2, 6);
            (x2, y2) = (x, y);
            drawn++;
        }
    }

    /// <summary>Globe::drawFlights through Globe::drawPath: chord steps re-projected onto the sphere.</summary>
    private void DrawFlights(GlobeScene scene)
    {
        var flights = scene.Flights;
        for (var index = 0; index < flights.Count; index++)
        {
            var path = flights[index];
            var (ax, ay, az) = Cartesian(path.FromLongitude, path.FromLatitude);
            var (bx, by, bz) = Cartesian(path.ToLongitude, path.ToLatitude);
            if (-bx == ax && -by == ay && -bz == az) continue;
            bx -= ax;
            by -= ay;
            bz -= az;
            var length = Math.Sqrt(bx * bx + by * by + bz * bz);
            length *= length * 15;
            var count = (short)(length + 1);
            bx /= count;
            by /= count;
            bz /= count;
            var (lon1, lat1) = Polar(ax, ay, az);
            var (x1, y1) = _view.ProjectPrecise(lon1, lat1);
            for (var step = 0; step < count; step++)
            {
                ax += bx;
                ay += by;
                az += bz;
                var (lon2, lat2) = Polar(ax, ay, az);
                var (x2, y2) = _view.ProjectPrecise(lon2, lat2);
                if (!_view.IsBack(lon1, lat1) && !_view.IsBack(lon2, lat2)) ShadedLine(x1, y1, x2, y2, 8);
                (lon1, lat1, x1, y1) = (lon2, lat2, x2, y2);
            }
        }

        static (double X, double Y, double Z) Cartesian(double longitude, double latitude) =>
            (Math.Sin(longitude) * Math.Cos(latitude), Math.Sin(latitude), Math.Cos(longitude) * Math.Cos(latitude));

        static (double Longitude, double Latitude) Polar(double x, double y, double z) =>
            (Math.Atan2(x, z), Math.Asin(y / Math.Sqrt(x * x + y * y + z * z)));
    }

    /// <summary>
    /// Globe::XuLine: walks the clipped line one pixel at a time and draws each covered globe pixel
    /// shaded (ocean by <c>shade + 8</c>, land by <c>shade * 3</c>) onto the radar layer.
    /// </summary>
    private void ShadedLine(double x1, double y1, double x2, double y2, int shade)
    {
        if (!Clip(ref x1, ref y1, ref x2, ref y2, Surface.Width - 1, Surface.Height - 1)) return;
        var colors = _assets.Colors;
        double deltaX = x2 - x1, deltaY = y2 - y1;
        bool horizontal;
        double length;
        if (Math.Abs((int)y2 - (int)y1) > Math.Abs((int)x2 - (int)x1))
        {
            length = Math.Abs((int)y2 - (int)y1);
            horizontal = false;
        }
        else
        {
            length = Math.Abs((int)x2 - (int)x1);
            horizontal = true;
        }
        double stepY = y2 < y1 ? -1 : deltaY == 0 ? 0 : 1;
        double stepX = x2 < x1 ? -1 : deltaX == 0 ? 0 : 1;
        if (horizontal) stepY = deltaY / length;
        else stepX = deltaX / length;
        double x = x1, y = y1;
        while (length > 0)
        {
            int px = (int)x, py = (int)y;
            if ((uint)px < (uint)Surface.Width && (uint)py < (uint)Surface.Height)
            {
                var color = Surface.GetPixel(px, py);
                if (color != 0)
                    _radars.SetPixel(px, py, colors.OceanShading && color >= colors.Ocean && color < colors.Ocean + 32
                        ? (byte)(colors.Ocean + shade + 8)
                        : GlobeShading.LandShadow(color, shade * 3));
            }
            x += stepX;
            y += stepY;
            length -= 1.0;
        }
    }

    /// <summary>Liang-Barsky clipping to the inclusive rectangle [0, right] x [0, bottom].</summary>
    private static bool Clip(ref double x1, ref double y1, ref double x2, ref double y2, double right, double bottom)
    {
        double low = 0, high = 1, dx = x2 - x1, dy = y2 - y1;
        if (!Edge(-dx, x1) || !Edge(dx, right - x1) || !Edge(-dy, y1) || !Edge(dy, bottom - y1)) return false;
        double startX = x1, startY = y1;
        x2 = startX + high * dx;
        y2 = startY + high * dy;
        x1 = startX + low * dx;
        y1 = startY + low * dy;
        return true;

        bool Edge(double p, double q)
        {
            if (p == 0) return q >= 0;
            var r = q / p;
            if (p < 0)
            {
                if (r > high) return false;
                if (r > low) low = r;
            }
            else
            {
                if (r < low) return false;
                if (r < high) high = r;
            }
            return true;
        }
    }

    /// <summary>Globe::drawDetail: borders from zoom 1, then labels and city markers by zoom.</summary>
    private void DrawDetail(GlobeScene scene)
    {
        var line = _assets.Colors.Line;
        if (_view.Zoom >= 1)
        {
            var polylines = _globe.Polylines;
            for (var lineIndex = 0; lineIndex < polylines.Count; lineIndex++)
            {
                var points = polylines[lineIndex].Points;
                for (var index = 0; index + 1 < points.Count; index++)
                {
                    if (_view.IsBack(points[index].Longitude, points[index].Latitude) ||
                        _view.IsBack(points[index + 1].Longitude, points[index + 1].Latitude)) continue;
                    var (x1, y1) = _view.Project(points[index].Longitude, points[index].Latitude);
                    var (x2, y2) = _view.Project(points[index + 1].Longitude, points[index + 1].Latitude);
                    Surface.DrawLine(x1, y1, x2, y2, line);
                }
            }
        }
        var scale = _view.PixelScale;
        var labels = scene.Labels;
        for (var index = 0; index < labels.Count; index++)
        {
            var label = labels[index];
            if (_view.Zoom < label.MinimumZoom) continue;
            if (label.City) DrawMarker(new GlobeMarker(label.Longitude, label.Latitude, GlobeScene.CityMarker), true);
            if (_font is null || _view.IsBack(label.Longitude, label.Latitude)) continue;
            var (x, y) = _view.Project(label.Longitude, label.Latitude);
            var box = label.BoxWidth * scale;
            var left = x - box / 2 + (box - _font.Measure(label.Text).Width) / 2;
            _font.DrawTextShifted(Surface, label.Text, left, y + label.OffsetY * scale, label.Color);
        }
    }

    private void DrawLand()
    {
        if (_cachedRevision != _view.Revision) CachePolygons();
        var offset = _view.TextureOffset(_textureFrames);
        for (var index = 0; index < _projected.Length; index++)
        {
            if (!_visible[index] ||
                !_assets.Textures.TryGetValue(_globe.Polygons[index].Texture + offset, out var texture)) continue;
            Surface.FillTexturedPolygon(_projected[index], texture, 0, 0, _scratch);
        }
    }

    /// <summary>Globe::cache: skip polygons facing away, then project the rest.</summary>
    private void CachePolygons()
    {
        double sinCenter = Math.Sin(_view.CenterLatitude), cosCenter = Math.Cos(_view.CenterLatitude);
        for (var index = 0; index < _projected.Length; index++)
        {
            var points = _globe.Polygons[index].Points;
            double closest = 0, furthest = 0;
            for (var point = 0; point < points.Count; point++)
            {
                var z = cosCenter * Math.Cos(points[point].Latitude) * Math.Cos(points[point].Longitude - _view.CenterLongitude) +
                    sinCenter * Math.Sin(points[point].Latitude);
                if (z > closest) closest = z;
                else if (z < furthest) furthest = z;
            }
            _visible[index] = -furthest <= closest;
            if (!_visible[index]) continue;
            var projected = _projected[index];
            for (var point = 0; point < points.Count; point++)
            {
                var (x, y) = _view.Project(points[point].Longitude, points[point].Latitude);
                projected[point] = new(x, y);
            }
        }
        _cachedRevision = _view.Revision;
    }

    /// <summary>
    /// CreateShadow over the surface: pixels off the globe become transparent, ocean pixels take the
    /// ocean block's shade and land pixels darken within their colour group.
    /// </summary>
    private void DrawShadow(CampaignTime time, bool seasons)
    {
        var colors = _assets.Colors;
        var sun = GlobeShading.SunDirection(time, _view.CenterLongitude, _view.CenterLatitude, seasons);
        var radius = _view.Radius;
        var limit = radius * radius;
        var inverse = 1 / radius;
        var scale = _view.PixelScale;
        for (var y = 0; y < Surface.Height; y++)
        {
            var row = Surface.GetRow(y);
            var dy = y + 0.5 - _view.CenterY;
            for (var x = 0; x < row.Length; x++)
            {
                var dx = x + 0.5 - _view.CenterX;
                var distance = dx * dx + dy * dy;
                var color = row[x];
                if (color == 0 || distance >= limit)
                {
                    row[x] = 0;
                    continue;
                }
                var shadow = GlobeShading.ShadowValue(dx * inverse, dy * inverse, Math.Sqrt(limit - distance) * inverse,
                    sun, GlobeShading.NoiseAt(x / scale, y / scale));
                row[x] = colors.OceanShading && color >= colors.Ocean && color < colors.Ocean + 32
                    ? (byte)(colors.Ocean + shadow)
                    : GlobeShading.LandShadow(color, shadow);
            }
        }
    }

    /// <summary>Globe::drawTarget: centred frames, shifted one colour while the blink is off.</summary>
    private void DrawMarkers(GlobeScene scene, bool blinkOn)
    {
        var markers = scene.Markers;
        for (var index = 0; index < markers.Count; index++) DrawMarker(markers[index], blinkOn);
    }

    public void DrawMarker(GlobeMarker marker, bool blinkOn)
    {
        if (_view.IsBack(marker.Longitude, marker.Latitude) ||
            !_assets.Markers.TryGetValue(marker.Frame, out var frame)) return;
        var (x, y) = _view.Project(marker.Longitude, marker.Latitude);
        var scale = _view.PixelScale;
        // Globe::drawTarget centres at x - width / 2 in reference pixels (mods may ship larger markers).
        var left = x - frame.Width / 2 * scale;
        var top = y - frame.Height / 2 * scale;
        var shift = marker.Frame == GlobeScene.CityMarker || blinkOn ? 0 : 1;
        for (var row = 0; row < frame.Height * scale; row++)
        {
            var targetY = top + row;
            if ((uint)targetY >= (uint)Surface.Height) continue;
            var source = frame.GetRow(row / scale);
            var target = Surface.GetRow(targetY);
            for (var column = 0; column < frame.Width * scale; column++)
            {
                var targetX = left + column;
                // Markers draw onto a colour-keyed layer: a shade that wraps to 0 leaves the globe visible.
                var color = source[column / scale];
                var shaded = (byte)(color + shift);
                if (color != 0 && shaded != 0 && (uint)targetX < (uint)target.Length) target[targetX] = shaded;
            }
        }
    }
}
