namespace Oxce.Engine.Audio;

public enum AudioBus
{
    Effects,
    UserInterface,
    Ambient,
    UnitResponse,
    Music,
}

public readonly record struct AudioPlaybackOptions(
    AudioBus Bus,
    int LoopCount = 0,
    float Gain = 1,
    float Pan = 0)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Bus))
        {
            throw new ArgumentOutOfRangeException(nameof(Bus));
        }

        if (LoopCount < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(LoopCount));
        }

        if (!float.IsFinite(Gain) || Gain is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Gain));
        }

        if (!float.IsFinite(Pan) || Pan is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Pan));
        }
    }
}

public sealed class PcmAudioClip
{
    private readonly short[] _samples;

    public PcmAudioClip(ReadOnlySpan<short> interleavedSamples, int sampleRate, int channels)
    {
        if (interleavedSamples.IsEmpty)
        {
            throw new ArgumentException("A PCM clip must contain at least one sample frame.", nameof(interleavedSamples));
        }

        if (sampleRate is < 1 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (interleavedSamples.Length % channels != 0)
        {
            throw new ArgumentException("Interleaved sample count must be divisible by the channel count.", nameof(interleavedSamples));
        }

        _samples = interleavedSamples.ToArray();
        SampleRate = sampleRate;
        Channels = channels;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int FrameCount => _samples.Length / Channels;

    public ReadOnlyMemory<short> Samples => _samples;
}

public interface IAudioPlayback : IDisposable
{
    bool IsPlaying { get; }

    void Halt();
}

public interface IAudioOutput : IDisposable
{
    bool IsAvailable { get; }

    void SetBusGain(AudioBus bus, double gain);

    bool IsBusPlaying(AudioBus bus);

    void StopBus(AudioBus bus);

    IAudioPlayback Play(PcmAudioClip clip, AudioPlaybackOptions options);

    void PauseAll();

    void ResumeAll();

    void StopAll();
}

public interface IAudioSampleSource
{
    int SampleRate { get; }

    int Channels { get; }

    void Mix(Span<short> destination);
}
