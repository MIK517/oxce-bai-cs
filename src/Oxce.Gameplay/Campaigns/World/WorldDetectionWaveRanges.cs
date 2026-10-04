using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Native wave helper result bounds without executing script programs.</summary>
internal static class WorldDetectionWaveRanges
{
    internal static (int Min, int Max) Apply(CoreScriptOperation operation, (int Min, int Max) value,
        (int Min, int Max) period, (int Min, int Max) size, (int Min, int Max) maximum)
    {
        if (period.Min <= 0) return WorldDetectionScalarRanges.Full;
        var circular = operation is CoreScriptOperation.WaveSine or CoreScriptOperation.WaveCosine;
        if (value.Min == value.Max && period.Min == period.Max && size.Min == size.Max &&
            (circular || maximum.Min == maximum.Max))
        {
            int result;
            if (circular)
            {
                var angle = 2.0 * Math.PI * value.Min / period.Min;
                var raw = size.Min * (operation == CoreScriptOperation.WaveSine ? Math.Sin(angle) : Math.Cos(angle));
                if (raw < int.MinValue || raw > int.MaxValue) return WorldDetectionScalarRanges.Full;
                result = (int)raw;
            }
            else
            {
                var phase = value.Min % period.Min;
                if (phase < 0) phase += period.Min;
                if (phase > size.Min) result = 0;
                else if (operation == CoreScriptOperation.WaveRectangle) result = maximum.Min;
                else
                {
                    if (operation == CoreScriptOperation.WaveTriangle && phase > size.Min / 2) phase = size.Min - phase;
                    result = Math.Min(phase, maximum.Min);
                }
            }
            return (result, result);
        }
        if (circular)
        {
            var amplitude = Math.Max(Math.Abs((long)size.Min), Math.Abs((long)size.Max));
            return amplitude > int.MaxValue ? WorldDetectionScalarRanges.Full : (-(int)amplitude, (int)amplitude);
        }
        if (size.Max < 0) return (0, 0);
        if (operation == CoreScriptOperation.WaveRectangle)
            return size.Min >= period.Max - 1 ? maximum : (Math.Min(0, maximum.Min), Math.Max(0, maximum.Max));
        var bound = Math.Min(period.Max - 1, operation == CoreScriptOperation.WaveTriangle ? size.Max / 2 : size.Max);
        return (Math.Min(0, maximum.Min), Math.Max(0, Math.Min(bound, maximum.Max)));
    }
}
