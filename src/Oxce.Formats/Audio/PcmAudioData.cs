namespace Oxce.Formats.Audio;

public sealed class PcmAudioData
{
    private readonly short[] _samples;

    internal PcmAudioData(short[] samples, int sampleRate, int channels)
    {
        _samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int FrameCount => _samples.Length / Channels;

    public ReadOnlyMemory<short> Samples => _samples;
}
