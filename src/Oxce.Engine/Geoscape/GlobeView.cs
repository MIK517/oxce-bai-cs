namespace Oxce.Engine.Geoscape;

/// <summary>
/// The globe's orthographic view: centre, zoom and the projection between globe and screen.
/// Reference: <c>Geoscape/Globe.cpp</c> (polarToCart, cartToPolar, pointBack, setupRadii,
/// setZoom, rotate, center, targetNear). Coordinates are frame pixels; <see cref="PixelScale"/>
/// scales the reference's 320x200 distances such as <c>NEAR_RADIUS</c>.
/// </summary>
public sealed class GlobeView
{
    /// <summary>Globe::ROTATE_LONGITUDE and ROTATE_LATITUDE, in radians per rotation tick.</summary>
    public const double RotateLongitude = 0.10;
    public const double RotateLatitude = 0.06;
    /// <summary>Globe's rotation timer interval.</summary>
    public static readonly TimeSpan RotationInterval = TimeSpan.FromMilliseconds(10);
    /// <summary>Globe::NEAR_RADIUS: squared reference pixels around a target that select it.</summary>
    public const int NearRadius = 25;
    /// <summary>Options::geoScrollSpeed default.</summary>
    public const int DefaultScrollSpeed = 20;
    private const double LoopLimit = 1024 * Math.PI;
    private static readonly double[] RadiusFactors = [0.45, 0.60, 0.90, 1.40, 2.25, 3.60];

    public GlobeView(int width, int height, int pixelScale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelScale);
        Width = width;
        Height = height;
        PixelScale = pixelScale;
        // Globe::resize centres the globe in its surface; Globe::setupRadii scales by height.
        CenterX = width / 2;
        CenterY = height / 2;
    }

    public int Width { get; }
    public int Height { get; }
    public int PixelScale { get; }
    public int CenterX { get; }
    public int CenterY { get; }
    public double CenterLongitude { get; private set; }
    public double CenterLatitude { get; private set; }
    public int Zoom { get; private set; }
    public static int ZoomLevels => RadiusFactors.Length;
    public double Radius => RadiusFactors[Zoom] * Height;

    /// <summary>Changes on every centre or zoom change, so cached projections can be reused.</summary>
    public long Revision { get; private set; }

    public void Center(double longitude, double latitude)
    {
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude))
            throw new ArgumentOutOfRangeException(nameof(longitude), "Globe centre must be finite.");
        CenterLongitude = longitude;
        CenterLatitude = latitude;
        Revision++;
    }

    public void SetZoom(int zoom)
    {
        Zoom = Math.Clamp(zoom, 0, RadiusFactors.Length - 1);
        Revision++;
    }

    public bool ZoomIn()
    {
        if (Zoom >= RadiusFactors.Length - 1) return false;
        SetZoom(Zoom + 1);
        return true;
    }

    public bool ZoomOut()
    {
        if (Zoom == 0) return false;
        SetZoom(Zoom - 1);
        return true;
    }

    /// <summary>Globe::rotate: one rotation-timer step in the given directions (-1, 0 or 1).</summary>
    public void Rotate(int longitudeDirection, int latitudeDirection, int scrollSpeed = DefaultScrollSpeed)
    {
        var factor = (110 - scrollSpeed) / 100.0 / (Zoom + 1);
        Center(CenterLongitude + Math.Sign(longitudeDirection) * RotateLongitude * factor,
            CenterLatitude + Math.Sign(latitudeDirection) * RotateLatitude * factor);
    }

    /// <summary>
    /// Globe::setZoom's texture detail: the frame offset into a texture set of three detail levels.
    /// </summary>
    public int TextureOffset(int totalFrames) => (2 - Zoom / 2) * (totalFrames / 3);

    /// <summary>Globe::polarToCart (Sint16 overload): floored orthographic projection.</summary>
    public (int X, int Y) Project(double longitude, double latitude)
    {
        var (dx, dy) = Offset(longitude, latitude);
        return (CenterX + (int)Math.Floor(dx), CenterY + (int)Math.Floor(dy));
    }

    /// <summary>Globe::polarToCart (double overload).</summary>
    public (double X, double Y) ProjectPrecise(double longitude, double latitude)
    {
        var (dx, dy) = Offset(longitude, latitude);
        return (CenterX + dx, CenterY + dy);
    }

    private (double X, double Y) Offset(double longitude, double latitude)
    {
        var radius = Radius;
        var delta = longitude - CenterLongitude;
        return (radius * Math.Cos(latitude) * Math.Sin(delta),
            radius * (Math.Cos(CenterLatitude) * Math.Sin(latitude) -
                Math.Sin(CenterLatitude) * Math.Cos(latitude) * Math.Cos(delta)));
    }

    /// <summary>
    /// Globe::cartToPolar. Returns false for a point off the globe, where the reference produces NaN
    /// and its mouse handlers ignore the click. Longitude is kept in [0, 2π).
    /// </summary>
    public bool TryUnproject(int x, int y, out double longitude, out double latitude)
    {
        var dx = (double)(short)(x - CenterX);
        var dy = (double)(short)(y - CenterY);
        var rho = Math.Sqrt(dx * dx + dy * dy);
        var c = Math.Asin(rho / Radius);
        if (rho == 0)
        {
            latitude = CenterLatitude;
            longitude = CenterLongitude;
        }
        else
        {
            latitude = Math.Asin(dy * Math.Sin(c) * Math.Cos(CenterLatitude) / rho + Math.Cos(c) * Math.Sin(CenterLatitude));
            longitude = Math.Atan2(dx * Math.Sin(c),
                rho * Math.Cos(CenterLatitude) * Math.Cos(c) - dy * Math.Sin(CenterLatitude) * Math.Sin(c)) + CenterLongitude;
        }
        if (double.IsNaN(longitude) || double.IsNaN(latitude)) return false;
        longitude = NormalizeLongitude(longitude);
        return true;
    }

    /// <summary>Globe::pointBack: the point is on the hidden hemisphere.</summary>
    public bool IsBack(double longitude, double latitude) =>
        Math.Cos(CenterLatitude) * Math.Cos(latitude) * Math.Cos(longitude - CenterLongitude) +
        Math.Sin(CenterLatitude) * Math.Sin(latitude) < 0.0;

    /// <summary>Globe::targetNear, with the radius scaled to frame pixels.</summary>
    public bool IsNear(double longitude, double latitude, int x, int y)
    {
        if (IsBack(longitude, latitude)) return false;
        var (tx, ty) = Project(longitude, latitude);
        long dx = x - tx;
        long dy = y - ty;
        return dx * dx + dy * dy <= (long)NearRadius * PixelScale * PixelScale;
    }

    /// <summary>The reference's "while (lon &lt; 0) lon += 2π; while (lon &gt;= 2π) lon -= 2π".</summary>
    public static double NormalizeLongitude(double longitude)
    {
        if (!double.IsFinite(longitude)) return longitude;
        // Ordinary values keep the reference's repeated additions bit for bit. A huge value, such as
        // an untrusted saved centre, is reduced first so the loops terminate.
        if (Math.Abs(longitude) > LoopLimit) longitude %= 2 * Math.PI;
        while (longitude < 0) longitude += 2 * Math.PI;
        while (longitude >= 2 * Math.PI) longitude -= 2 * Math.PI;
        return longitude;
    }
}
