using BenchmarkDotNet.Attributes;
using Oxce.Engine.Audio;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class MixerBenchmarks : IDisposable
{
    private const int SampleRate = 48_000;
    private const int OutputFrames = 1_024;
    private ManagedAudioMixer _mixer = null!;
    private short[] _destination = null!;
    private List<IAudioPlayback> _playbacks = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mixer = new ManagedAudioMixer(SampleRate, maximumEffectVoices: 32);
        _playbacks = [];
        _destination = new short[OutputFrames * 2];
        var samples = new short[4_096 * 2];
        for (var index = 0; index < samples.Length; index += 2)
        {
            samples[index] = (short)((index * 17 % 20_000) - 10_000);
            samples[index + 1] = (short)-samples[index];
        }

        var clip = new PcmAudioClip(samples, SampleRate, channels: 2);
        for (var voice = 0; voice < 16; ++voice)
        {
            _playbacks.Add(_mixer.Play(
                clip,
                new AudioPlaybackOptions(
                    AudioBus.Effects,
                    LoopCount: -1,
                    Gain: 0.05f,
                    Pan: ((voice % 5) - 2) / 2f)));
        }
    }

    [Benchmark(OperationsPerInvoke = OutputFrames)]
    public void MixSixteenStereoVoices() => _mixer.Mix(_destination);

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        if (_playbacks is not null)
        {
            foreach (var playback in _playbacks)
            {
                playback.Dispose();
            }

            _playbacks = null!;
        }

        _mixer?.Dispose();
        _mixer = null!;
        GC.SuppressFinalize(this);
    }
}
