using Oxce.Engine.Audio;
using Oxce.Engine.Input;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class InputAudioTests
{
    [Fact]
    public void DesktopQuitShortcutsArePlatformSpecific()
    {
        var altF4 = GameInputEvent.Key(
            GameInputEventKind.KeyPressed,
            0,
            1,
            0,
            DesktopQuitShortcut.F4Key,
            InputKeyModifiers.LeftAlt);
        var commandQ = GameInputEvent.Key(
            GameInputEventKind.KeyPressed,
            0,
            1,
            0,
            DesktopQuitShortcut.QKey,
            InputKeyModifiers.RightGui);

        Assert.True(DesktopQuitShortcut.IsMatch(altF4, DesktopPlatform.Windows));
        Assert.False(DesktopQuitShortcut.IsMatch(altF4, DesktopPlatform.MacOS));
        Assert.True(DesktopQuitShortcut.IsMatch(commandQ, DesktopPlatform.MacOS));
        Assert.False(DesktopQuitShortcut.IsMatch(commandQ, DesktopPlatform.Other));
    }

    [Fact]
    public void PointerMapperRejectsInvalidScales()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InputCoordinateMapper.Map(0, 0, 0, 1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InputCoordinateMapper.Map(0, 0, 1, double.NaN, 0, 0, 0, 0));
    }

    [Fact]
    public void NullAudioOutputValidatesPlaybackShape()
    {
        Assert.False(NullAudioOutput.Instance.IsAvailable);
        Assert.Throws<ArgumentException>(() => new PcmAudioClip(new short[] { 1, 2, 3 }, 44_100, 2));
        var clip = new PcmAudioClip(new short[] { 1, 2, 3, 4 }, 44_100, 2);
        Assert.Equal(2, clip.FrameCount);
        using var playback = NullAudioOutput.Instance.Play(clip, new AudioPlaybackOptions(AudioBus.Effects));
        Assert.False(playback.IsPlaying);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(129)]
    public void VolumeCurveRejectsOutOfRangeSettings(int setting)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioVolumeCurve.ToGain(setting));
    }
}
