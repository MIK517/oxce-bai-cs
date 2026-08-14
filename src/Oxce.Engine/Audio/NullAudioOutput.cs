namespace Oxce.Engine.Audio;

public sealed class NullAudioOutput : IAudioOutput
{
    public static NullAudioOutput Instance { get; } = new();

    private NullAudioOutput()
    {
    }

    public bool IsAvailable => false;

    public void SetBusGain(AudioBus bus, double gain)
    {
        ValidateBus(bus);
        if (!double.IsFinite(gain) || gain is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }
    }

    public bool IsBusPlaying(AudioBus bus)
    {
        ValidateBus(bus);
        return false;
    }

    public void StopBus(AudioBus bus)
    {
        ValidateBus(bus);
    }

    public IAudioPlayback Play(
        PcmAudioClip clip,
        AudioPlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(clip);
        options.Validate();
        return NullAudioPlayback.Instance;
    }

    public void PauseAll()
    {
    }

    public void ResumeAll()
    {
    }

    public void StopAll()
    {
    }

    public void Dispose()
    {
    }

    private static void ValidateBus(AudioBus bus)
    {
        if (!Enum.IsDefined(bus))
        {
            throw new ArgumentOutOfRangeException(nameof(bus));
        }
    }

    private sealed class NullAudioPlayback : IAudioPlayback
    {
        internal static NullAudioPlayback Instance { get; } = new();

        public bool IsPlaying => false;

        public void Halt()
        {
        }

        public void Dispose()
        {
        }
    }
}
