namespace Oxce.Engine.Audio;

public sealed class ManagedAudioMixer : IAudioOutput, IAudioSampleSource
{
    public const int DefaultMaximumEffectVoices = 11;

    private readonly object _sync = new();
    private readonly List<Voice> _voices = [];
    private readonly double[] _busGains = Enumerable.Repeat(1d, Enum.GetValues<AudioBus>().Length).ToArray();
    private readonly int _maximumEffectVoices;
    private long _nextSequence;
    private bool _paused;
    private bool _disposed;

    public ManagedAudioMixer(
        int sampleRate,
        int maximumEffectVoices = DefaultMaximumEffectVoices)
    {
        if (sampleRate is < 1 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maximumEffectVoices);
        SampleRate = sampleRate;
        _maximumEffectVoices = maximumEffectVoices;
    }

    public int SampleRate { get; }

    public int Channels => 2;

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return !_disposed;
            }
        }
    }

    public void SetBusGain(AudioBus bus, double gain)
    {
        ValidateBus(bus);
        if (!double.IsFinite(gain) || gain is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _busGains[(int)bus] = gain;
        }
    }

    public bool IsBusPlaying(AudioBus bus)
    {
        ValidateBus(bus);
        lock (_sync)
        {
            return !_disposed && _voices.Any(voice => voice.Playing && voice.Options.Bus == bus);
        }
    }

    public void StopBus(AudioBus bus)
    {
        ValidateBus(bus);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            HaltBus(bus);
            RemoveStoppedVoices();
        }
    }

    public IAudioPlayback Play(PcmAudioClip clip, AudioPlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(clip);
        options.Validate();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!PrepareBusForVoice(options.Bus))
            {
                return new ManagedAudioPlayback(this, Voice.Stopped);
            }

            var voice = new Voice(clip, options, _nextSequence++);
            _voices.Add(voice);
            return new ManagedAudioPlayback(this, voice);
        }
    }

    public void Mix(Span<short> destination)
    {
        if (destination.Length % Channels != 0)
        {
            throw new ArgumentException("Mixer destination must contain whole stereo frames.", nameof(destination));
        }

        destination.Clear();
        lock (_sync)
        {
            if (_disposed || _paused)
            {
                return;
            }

            var frameCount = destination.Length / Channels;
            for (var frame = 0; frame < frameCount; frame++)
            {
                long mixedLeft = 0;
                long mixedRight = 0;
                for (var voiceIndex = 0; voiceIndex < _voices.Count; voiceIndex++)
                {
                    var voice = _voices[voiceIndex];
                    if (!voice.Playing || !NormalizePosition(voice))
                    {
                        continue;
                    }

                    ReadFrame(voice, out var sourceLeft, out var sourceRight);
                    var gain = _busGains[(int)voice.Options.Bus] * voice.Options.Gain;
                    var leftGain = gain * (voice.Options.Pan > 0 ? 1 - voice.Options.Pan : 1);
                    var rightGain = gain * (voice.Options.Pan < 0 ? 1 + voice.Options.Pan : 1);
                    mixedLeft += (int)(sourceLeft * leftGain);
                    mixedRight += (int)(sourceRight * rightGain);

                    voice.SourcePhase += voice.Clip.SampleRate;
                    NormalizePosition(voice);
                }

                destination[frame * Channels] = (short)Math.Clamp(mixedLeft, short.MinValue, short.MaxValue);
                destination[frame * Channels + 1] = (short)Math.Clamp(mixedRight, short.MinValue, short.MaxValue);
            }

            RemoveStoppedVoices();
        }
    }

    public void PauseAll()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _paused = true;
            }
        }
    }

    public void ResumeAll()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _paused = false;
            }
        }
    }

    public void StopAll()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var voice in _voices)
            {
                voice.Playing = false;
            }

            _voices.Clear();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var voice in _voices)
            {
                voice.Playing = false;
            }

            _voices.Clear();
            _disposed = true;
        }
    }

    private bool PrepareBusForVoice(AudioBus bus)
    {
        var active = _voices.Where(voice => voice.Playing && voice.Options.Bus == bus).ToArray();
        switch (bus)
        {
            case AudioBus.Effects:
                return active.Length < _maximumEffectVoices;
            case AudioBus.UserInterface:
                if (active.Length >= 2)
                {
                    active.MinBy(voice => voice.Sequence)!.Playing = false;
                }

                break;
            case AudioBus.Ambient:
            case AudioBus.UnitResponse:
            case AudioBus.Music:
                foreach (var voice in active)
                {
                    voice.Playing = false;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(bus));
        }

        RemoveStoppedVoices();
        return true;
    }

    private bool NormalizePosition(Voice voice)
    {
        if (!voice.Playing)
        {
            return false;
        }

        var cycleLength = checked((long)voice.Clip.FrameCount * SampleRate);
        if (voice.SourcePhase < cycleLength)
        {
            return true;
        }

        var crossings = voice.SourcePhase / cycleLength;
        if (voice.RemainingLoops >= 0)
        {
            if (crossings > voice.RemainingLoops)
            {
                voice.Playing = false;
                return false;
            }

            voice.RemainingLoops -= checked((int)crossings);
        }

        voice.SourcePhase %= cycleLength;
        return true;
    }

    private void ReadFrame(Voice voice, out int left, out int right)
    {
        var sourceFrame = checked((int)(voice.SourcePhase / SampleRate));
        var samples = voice.Clip.Samples.Span;
        var channels = voice.Clip.Channels;
        var offset = sourceFrame * channels;
        if (channels == 1)
        {
            left = samples[offset];
            right = left;
        }
        else if (channels == 2)
        {
            left = samples[offset];
            right = samples[offset + 1];
        }
        else
        {
            var sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += samples[offset + channel];
            }

            left = sum / channels;
            right = left;
        }
    }

    private void HaltBus(AudioBus bus)
    {
        foreach (var voice in _voices)
        {
            if (voice.Options.Bus == bus)
            {
                voice.Playing = false;
            }
        }
    }

    private bool IsVoicePlaying(Voice voice)
    {
        lock (_sync)
        {
            return !_disposed && voice.Playing;
        }
    }

    private void HaltVoice(Voice voice)
    {
        lock (_sync)
        {
            voice.Playing = false;
            RemoveStoppedVoices();
        }
    }

    private void RemoveStoppedVoices()
    {
        for (var index = _voices.Count - 1; index >= 0; index--)
        {
            if (!_voices[index].Playing)
            {
                _voices.RemoveAt(index);
            }
        }
    }

    private static void ValidateBus(AudioBus bus)
    {
        if (!Enum.IsDefined(bus))
        {
            throw new ArgumentOutOfRangeException(nameof(bus));
        }
    }

    private sealed class Voice
    {
        internal static Voice Stopped { get; } = new();

        private Voice()
        {
            Clip = null!;
            Playing = false;
        }

        internal Voice(PcmAudioClip clip, AudioPlaybackOptions options, long sequence)
        {
            Clip = clip;
            Options = options;
            RemainingLoops = options.LoopCount;
            Sequence = sequence;
            Playing = true;
        }

        internal PcmAudioClip Clip { get; }

        internal AudioPlaybackOptions Options { get; }

        internal long Sequence { get; }

        internal long SourcePhase { get; set; }

        internal int RemainingLoops { get; set; }

        internal bool Playing { get; set; }
    }

    private sealed class ManagedAudioPlayback : IAudioPlayback
    {
        private readonly ManagedAudioMixer _owner;
        private readonly Voice _voice;
        private bool _disposed;

        internal ManagedAudioPlayback(ManagedAudioMixer owner, Voice voice)
        {
            _owner = owner;
            _voice = voice;
        }

        public bool IsPlaying => !_disposed && _owner.IsVoicePlaying(_voice);

        public void Halt()
        {
            if (!_disposed)
            {
                _owner.HaltVoice(_voice);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner.HaltVoice(_voice);
            _disposed = true;
        }
    }
}
