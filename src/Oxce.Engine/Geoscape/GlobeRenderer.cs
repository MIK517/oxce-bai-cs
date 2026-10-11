using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// Draws the globe into an indexed surface in Globe::draw order: ocean, textured land, the
/// day/night shadow over every globe pixel, then markers. Projected polygons are cached until the
/// view moves; drawing allocates nothing. Radar circles, flight paths and the detail layer
/// (borders, labels, cities) are not drawn yet.
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
    private long _cachedRevision = -1;

    public GlobeRenderer(GlobeView view, GeoscapeAssets assets, RuntimeGlobe globe)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(globe);
        _view = view;
        _assets = assets;
        _globe = globe;
        Surface = new IndexedSurface(view.Width, view.Height);
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

    public void Render(GlobeScene scene, CampaignTime time, GlobeRenderOptions options, bool blinkOn)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(options);
        var colors = _assets.Colors;
        Surface.Clear(0);
        Surface.FillCircle(_view.CenterX + _view.PixelScale, _view.CenterY,
            (int)_view.Radius + 20 * _view.PixelScale, colors.Ocean);
        DrawLand();
        DrawShadow(time, options.Seasons);
        DrawMarkers(scene, blinkOn);
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
        var left = x - frame.Width * scale / 2;
        var top = y - frame.Height * scale / 2;
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
