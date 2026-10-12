using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class ScriptedLoopClientTests
{
    [Fact]
    public void InputsReplayInTimeOrderBeforeTheirTickAndTheRunEndsAfterItsDuration()
    {
        var inner = new Recorder();
        var scripted = new ScriptedLoopClient(inner,
        [
            new(TimeSpan.FromMilliseconds(30), Key('b')),
            new(TimeSpan.FromMilliseconds(10), Key('a')),
        ], TimeSpan.FromMilliseconds(50));
        var step = TimeSpan.FromMilliseconds(20);

        scripted.Tick(step);
        Assert.Equal(["a", "tick"], inner.Log);
        scripted.Tick(step);
        Assert.Equal(["a", "tick", "b", "tick"], inner.Log);
        Assert.False(scripted.ExitRequested);
        scripted.Tick(step);
        Assert.True(scripted.ExitRequested);
        Assert.Equal(2, scripted.Replayed);
        Assert.Same(inner.Frame, scripted.Frame);
        Assert.Equal(3, scripted.TickDurations.Count);
        Assert.Equal(scripted.TickDurations.Max(), scripted.TickDurationPercentile(100));
        Assert.Equal(scripted.TickDurations.Min(), scripted.TickDurationPercentile(0));
        Assert.Equal(TimeSpan.Zero, new ScriptedLoopClient(inner, [], TimeSpan.FromSeconds(1)).TickDurationPercentile(95));
        Assert.Throws<ArgumentOutOfRangeException>(() => scripted.TickDurationPercentile(101));

        inner.Exit = true;
        Assert.True(new ScriptedLoopClient(inner, [], TimeSpan.FromSeconds(1)).ExitRequested);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedLoopClient(inner, [], TimeSpan.Zero));
    }

    private static GameInputEvent Key(char key) =>
        GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None);

    private sealed class Recorder : IIndexedLoopClient
    {
        public List<string> Log { get; } = [];
        public bool Exit { get; set; }
        public IndexedSurface Frame { get; } = new(4, 4);
        public IndexedPalette Palette { get; } = IndexedPalette.CreateGrayscale();
        public long PresentationRevision => Log.Count;
        public bool ExitRequested => Exit;
        public void HandleInput(in GameInputEvent input) => Log.Add(((char)input.KeyCode).ToString());
        public void Tick(TimeSpan fixedInterval) => Log.Add("tick");
    }
}
