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
        if (!double.IsFinite(gain) || gain is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }
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
