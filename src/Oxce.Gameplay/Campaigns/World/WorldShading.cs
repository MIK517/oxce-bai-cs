namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Day/night shading of the globe: Globe.cpp GlobeStaticData, CreateShadow::getShadowValue,
/// Globe::getSunDirection and the battlescape shade of Globe::getPolygonTextureAndShade. The renderer
/// adds its noise; the landing site shade uses noise 0 and is pinned by the
/// <c>strategic-landing-shade</c> oracle.
/// </summary>
public static class WorldShading
{
    private const int ValueNoiseBits = 5;
    private const int DistanceNoiseBits = 3;
    private const int MultiplierNoiseBits = 4;
    private const int GradientMaximum = 256;
    private const int StepMaximum = 1 << ValueNoiseBits;
    private static readonly short[] Gradient = new short[GradientMaximum];
    private static readonly short[] Step = new short[GradientMaximum];
    private static readonly short[] Sequence = new short[GradientMaximum];

    // Globe::getPolygonTextureAndShade: geoscape shade levels 0-31 to battlescape levels 0-15.
    private static readonly int[] WorldShades =
    [
        0, 0, 0, 0, 1, 1, 2, 2,
        3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 11,
        11, 12, 12, 13, 13, 14, 15, 15,
    ];

    static WorldShading()
    {
        int lastValue = ShadeCurve(0), last = 0;
        for (var i = 0; i < GradientMaximum; ++i)
        {
            var value = ShadeCurve(i);
            if (value != lastValue)
            {
                for (var p = last; p < i; ++p)
                {
                    Step[p] = (short)(StepMaximum / (i - last));
                    Sequence[p] = (short)(StepMaximum * (p - last) / (i - last));
                }
                lastValue = value;
                last = i;
            }
            Gradient[i] = (short)value;
        }
        for (var p = last; p < GradientMaximum; ++p)
        {
            Step[p] = (short)(StepMaximum / (GradientMaximum - last));
            Sequence[p] = (short)(StepMaximum * (p - last) / (GradientMaximum - last));
        }
    }

    private static int ShadeCurve(int i)
    {
        ReadOnlySpan<int> steps = [1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 9, 12, 16, 20, 30];
        var j = i - GradientMaximum / 2;
        var adjustment = j >= 0 ? 1 : 0;
        var direction = adjustment != 0 ? 1 : -1;
        var offset = adjustment != 0 ? j + adjustment : -j;
        var shade = 15 + adjustment;
        foreach (var step in steps)
        {
            if (offset < step) break;
            shade += direction;
            offset -= step;
        }
        return shade;
    }

    /// <summary>The terminator gradient, its step and its sequence at a gradient index (0-255).</summary>
    public static (short Gradient, short Step, short Sequence) Table(int index) =>
        (Gradient[index], Step[index], Sequence[index]);

    /// <summary>
    /// Globe::getSunDirection at a point. GameTime::getDaylight shifts the hour by 18; with
    /// <paramref name="seasons"/> (Options::globeSeasons) the sun moves with the date.
    /// </summary>
    public static (double X, double Y, double Z) SunDirection(CampaignTime time, double longitude, double latitude,
        bool seasons)
    {
        var daylight = (double)((((time.Hour + 18) % 24 * 60) + time.Minute) * 60 + time.Second) / (60 * 60 * 24);
        var rotation = daylight * 2 * Math.PI;
        var sun = 0.0;
        if (seasons)
        {
            ReadOnlySpan<int> common = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365];
            ReadOnlySpan<int> leap = [0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366];
            var dayFraction = (double)((time.Hour * 60 + time.Minute) * 60 + time.Second) / 86400;
            var isLeap = time.Year % 4 == 0 && !(time.Year % 100 == 0 && time.Year % 400 != 0);
            var month = Math.Clamp(time.Month - 1, 0, 11);
            var day = (isLeap ? (leap[month] + time.Day - 1 + dayFraction) / 366
                : (common[month] + time.Day - 1 + dayFraction) / 365) - 0.219;
            if (day < 0) day += 1;
            sun = -0.261 * Math.Sin(day * 2 * Math.PI);
        }
        var x = Math.Cos(rotation + longitude);
        var y = Math.Sin(rotation + longitude) * -Math.Sin(latitude);
        var z = Math.Sin(rotation + longitude) * Math.Cos(latitude);
        var scale = sun > 0 ? 1 - sun : 1 + sun;
        x *= scale;
        y = y * scale + Math.Cos(latitude) * sun;
        z = z * scale + Math.Sin(latitude) * sun;
        var inverse = 1 / Math.Sqrt(x * x + y * y + z * z);
        return (x * inverse, y * inverse, z * inverse);
    }

    /// <summary>CreateShadow::getShadowValue: 0 (lit) to 31 (night) for a surface normal.</summary>
    public static int ShadowValue(double earthX, double earthY, double earthZ,
        (double X, double Y, double Z) sun, short noise)
    {
        double dx = earthX - sun.X, dy = earthY - sun.Y, dz = earthZ - sun.Z;
        // The reference sums x + (z + y).
        var value = dx * dx + (dz * dz + dy * dy);
        value = (value - 2) * 125.0 + GradientMaximum / 2;
        value -= ((noise >> ValueNoiseBits) & ((1 << DistanceNoiseBits) - 1)) - DistanceNoiseBits / 2;
        value += ((noise >> (ValueNoiseBits + DistanceNoiseBits)) & ((1 << MultiplierNoiseBits) - 1)) * 4 *
            (value - GradientMaximum / 2) / GradientMaximum;
        var full = Math.Truncate(value);
        var remainder = value - full;
        var offset = (int)Math.Clamp(full, 0, GradientMaximum - 1);
        var shade = (int)Gradient[offset];
        var middle = (int)(Sequence[offset] + Step[offset] * remainder - StepMaximum / 2);
        shade += middle / StepMaximum;
        shade += (noise & ((1 << ValueNoiseBits) - 1)) < middle % StepMaximum ? 1 : 0;
        return Math.Clamp(shade, 0, 31);
    }

    /// <summary>The battlescape shade (0-15) of a landing site (Globe::getPolygonTextureAndShade).</summary>
    public static int LandingShade(CampaignTime time, double longitude, double latitude, bool seasons) =>
        WorldShades[ShadowValue(0, 0, 1, SunDirection(time, longitude, latitude, seasons), 0)];
}
