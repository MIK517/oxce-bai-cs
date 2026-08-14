namespace Oxce.Engine.Audio;

public static class AudioVolumeCurve
{
    public const int MaximumSetting = 128;
    private const double Gradient = 10;

    public static double ToGain(int setting)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(setting);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(setting, MaximumSetting);
        return (Math.Exp(Math.Log(Gradient + 1) * setting / MaximumSetting) - 1) / Gradient;
    }

    public static int ToLegacyMixerVolume(int setting) => (int)(ToGain(setting) * MaximumSetting);
}
