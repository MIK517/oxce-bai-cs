using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// Day/night shading of the globe for drawing. The shading math is the gameplay
/// <see cref="WorldShading"/>; this adds the noise table. The reference seeds that table from the
/// clock, so it is generated from a fixed local seed and only its distribution is meaningful.
/// </summary>
public static class GlobeShading
{
    public const int NoiseSize = 60;
    private const int NoiseLimit = (1 << (4 + 3 + 5)) - 1;
    private static readonly short[] Noise = new short[NoiseSize * NoiseSize];

    static GlobeShading()
    {
        var state = 0x4F58_4345_474C_4F42UL;
        for (var i = 0; i < Noise.Length; i++)
        {
            state += 0x9E37_79B9_7F4A_7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
            z ^= z >> 31;
            Noise[i] = (short)(z % (ulong)(NoiseLimit + 1));
        }
    }

    /// <summary>The terminator gradient, its step and its sequence at a gradient index (0-255).</summary>
    public static (short Gradient, short Step, short Sequence) Table(int index) => WorldShading.Table(index);

    public static short NoiseAt(int x, int y) =>
        Noise[(y % NoiseSize + NoiseSize) % NoiseSize * NoiseSize + (x % NoiseSize + NoiseSize) % NoiseSize];

    /// <summary>Globe::getSunDirection for the view centre.</summary>
    public static (double X, double Y, double Z) SunDirection(CampaignTime time, double longitude, double latitude,
        bool seasons) => WorldShading.SunDirection(time, longitude, latitude, seasons);

    /// <summary>CreateShadow::getShadowValue: 0 (lit) to 31 (night) for a surface normal.</summary>
    public static int ShadowValue(double earthX, double earthY, double earthZ,
        (double X, double Y, double Z) sun, short noise) => WorldShading.ShadowValue(earthX, earthY, earthZ, sun, noise);

    /// <summary>CreateShadow::getLandShadow: darken within the colour's 16-shade group.</summary>
    public static byte LandShadow(byte color, int shadow)
    {
        if (shadow == 0) return color;
        var shaded = color + shadow / 3;
        var group = color & 0xF0;
        return (byte)Math.Min(shaded, group + 0x0F);
    }
}
