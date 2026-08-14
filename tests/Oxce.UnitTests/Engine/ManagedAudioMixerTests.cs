using Oxce.Engine.Audio;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class ManagedAudioMixerTests
{
    [Fact]
    public void MixAppliesBusVoiceAndLinearPanGains()
    {
        using var mixer = new ManagedAudioMixer(8_000);
        mixer.SetBusGain(AudioBus.Effects, 0.5);
        var loud = new PcmAudioClip(new short[] { 30_000 }, 8_000, 1);
        using var first = mixer.Play(loud, new AudioPlaybackOptions(AudioBus.Effects, Gain: 1, Pan: -0.5f));
        using var second = mixer.Play(loud, new AudioPlaybackOptions(AudioBus.Effects, Gain: 1, Pan: -0.5f));
        var output = new short[2];

        mixer.Mix(output);

        Assert.Equal(new short[] { 30_000, 15_000 }, output);
        Assert.False(first.IsPlaying);
        Assert.False(second.IsPlaying);
    }

    [Fact]
    public void MixSaturatesAccumulatedVoices()
    {
        using var mixer = new ManagedAudioMixer(sampleRate: 8_000);
        var clip = new PcmAudioClip(new short[] { 30_000 }, 8_000, 1);

        using var first = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Effects));
        using var second = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Effects));

        Span<short> output = stackalloc short[2];
        mixer.Mix(output);

        Assert.Equal([short.MaxValue, short.MaxValue], output.ToArray());
    }

    [Fact]
    public void MixUsesFixedPointRateConversionAndFiniteLoopCounts()
    {
        using var mixer = new ManagedAudioMixer(4);
        var clip = new PcmAudioClip(new short[] { 1_000, 2_000 }, 2, 1);
        using var playback = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Effects, LoopCount: 1));
        var output = new short[18];

        mixer.Mix(output);

        Assert.Equal(
            new short[]
            {
                1000, 1000, 1000, 1000, 2000, 2000, 2000, 2000,
                1000, 1000, 1000, 1000, 2000, 2000, 2000, 2000,
                0, 0,
            },
            output);
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void PauseResumeStopAndPlaybackHandlesPreserveState()
    {
        using var mixer = new ManagedAudioMixer(8_000);
        var clip = new PcmAudioClip(new short[] { 100, 200 }, 8_000, 1);
        using var playback = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Ambient, LoopCount: -1));
        mixer.PauseAll();
        var paused = new short[4];

        mixer.Mix(paused);

        Assert.Equal(new short[4], paused);
        Assert.True(playback.IsPlaying);
        Assert.True(mixer.IsBusPlaying(AudioBus.Ambient));

        mixer.ResumeAll();
        mixer.Mix(paused);
        Assert.Equal(new short[] { 100, 100, 200, 200 }, paused);

        mixer.StopBus(AudioBus.Ambient);
        Assert.False(playback.IsPlaying);
        Assert.False(mixer.IsBusPlaying(AudioBus.Ambient));
    }

    [Fact]
    public void BusPoliciesReplaceReservedVoicesAndRejectExcessEffects()
    {
        using var mixer = new ManagedAudioMixer(8_000, maximumEffectVoices: 1);
        var clip = new PcmAudioClip(new short[] { 100 }, 8_000, 1);
        using var effect1 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Effects, LoopCount: -1));
        using var effect2 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Effects));
        using var ambient1 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Ambient, LoopCount: -1));
        using var ambient2 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Ambient, LoopCount: -1));
        using var ui1 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.UserInterface, LoopCount: -1));
        using var ui2 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.UserInterface, LoopCount: -1));
        using var ui3 = mixer.Play(clip, new AudioPlaybackOptions(AudioBus.UserInterface, LoopCount: -1));

        Assert.True(effect1.IsPlaying);
        Assert.False(effect2.IsPlaying);
        Assert.False(ambient1.IsPlaying);
        Assert.True(ambient2.IsPlaying);
        Assert.False(ui1.IsPlaying);
        Assert.True(ui2.IsPlaying);
        Assert.True(ui3.IsPlaying);
    }

    [Fact]
    public void MixValidatesDestinationAndDisposedState()
    {
        var mixer = new ManagedAudioMixer(8_000);
        var clip = new PcmAudioClip(new short[] { 100 }, 8_000, 1);

        Assert.Throws<ArgumentException>(() => mixer.Mix(new short[1]));
        mixer.Dispose();
        Assert.False(mixer.IsAvailable);
        Assert.Throws<ObjectDisposedException>(
            () => mixer.Play(clip, new AudioPlaybackOptions(AudioBus.Effects)));
        var output = new short[] { 1, 1 };
        mixer.Mix(output);
        Assert.Equal(new short[] { 0, 0 }, output);
    }
}
