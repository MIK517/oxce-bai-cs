using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class ScreenNavigatorTests
{
    private sealed class Screen(string name, List<string> log) : IIndexedLoopClient
    {
        public IndexedSurface Frame { get; } = new(4, 4);
        public IndexedPalette Palette { get; } = IndexedPalette.CreateGrayscale();
        public long PresentationRevision { get; set; }
        public bool ExitRequested { get; set; }
        public Action? OnInput { get; set; }

        public void HandleInput(in GameInputEvent input)
        {
            log.Add(name + ":input");
            OnInput?.Invoke();
        }

        public void Tick(TimeSpan fixedInterval) => log.Add(name + ":tick");
    }

    private static GameInputEvent Input() => GameInputEvent.Simple(GameInputEventKind.WindowRestored, 0);

    [Fact]
    public void InputAndTicksReachOnlyTheTopScreen()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        Assert.False(navigator.HandleInput(Input()));
        Assert.False(navigator.Tick(TimeSpan.Zero));
        var bottom = new Screen("bottom", log);
        var top = new Screen("top", log);
        navigator.Push(bottom);
        navigator.Push(top);
        navigator.HandleInput(Input());
        navigator.Tick(TimeSpan.Zero);
        Assert.Equal(["top:input", "top:tick"], log);
        Assert.Same(top, navigator.Top);
    }

    [Fact]
    public void ACoveredScreenIsOneWithSomethingAboveIt()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        var bottom = new Screen("bottom", log);
        var top = new Screen("top", log);
        navigator.Push(bottom);
        Assert.False(navigator.Covers(bottom));
        navigator.Push(top);
        Assert.True(navigator.Covers(bottom));
        Assert.False(navigator.Covers(top));
        Assert.False(navigator.Covers(new Screen("absent", log)));
        navigator.Pop();
        Assert.False(navigator.Covers(bottom));
    }

    [Fact]
    public void AnExitRequestPopsTheScreenAndRunsItsCallbackOnce()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        var bottom = new Screen("bottom", log);
        var top = new Screen("top", log);
        var closed = 0;
        navigator.Push(bottom);
        navigator.Push(top, closed: () => closed++);
        top.OnInput = () => top.ExitRequested = true;
        navigator.HandleInput(Input());
        Assert.Equal((1, 1), (closed, navigator.Count));
        Assert.Same(bottom, navigator.Top);
        navigator.Tick(TimeSpan.Zero);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void AScreenThatPushesAnotherIsNotPoppedUnderIt()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        var first = new Screen("first", log);
        var second = new Screen("second", log);
        navigator.Push(first);
        first.OnInput = () =>
        {
            first.ExitRequested = true;
            navigator.Push(second);
        };
        navigator.HandleInput(Input());
        Assert.Equal(2, navigator.Count);
        Assert.Same(second, navigator.Top);
    }

    [Fact]
    public void ReplaceSwapsTheTopAndAScreenCannotBePushedTwice()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        var bottom = new Screen("bottom", log);
        var old = new Screen("old", log);
        var replacement = new Screen("new", log);
        var oldClosed = false;
        navigator.Push(bottom);
        navigator.Push(old, closed: () => oldClosed = true);
        navigator.Replace(replacement);
        Assert.True(oldClosed);
        Assert.Equal(2, navigator.Count);
        Assert.Same(replacement, navigator.Top);
        Assert.Throws<InvalidOperationException>(() => navigator.Push(bottom));
        navigator.Pop();
        navigator.Pop();
        Assert.False(navigator.Pop());
    }

    [Fact]
    public void OnlyTheTopmostOpaqueScreenAndTheTranslucentOnesAboveItAreVisible()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        var map = new Screen("map", log);
        var inventory = new Screen("inventory", log);
        var popup = new Screen("popup", log);
        navigator.Push(map);
        navigator.Push(inventory);
        navigator.Push(popup, opaque: false);
        Assert.Equal([inventory, popup], navigator.Visible);
    }

    [Fact]
    public void PresentationRevisionRisesOnEveryStructuralChangeAndNeverFalls()
    {
        var log = new List<string>();
        var navigator = new ScreenNavigator();
        var screen = new Screen("a", log) { PresentationRevision = 5 };
        var start = navigator.PresentationRevision;
        navigator.Push(screen);
        var pushed = navigator.PresentationRevision;
        Assert.True(pushed > start);
        navigator.Pop();
        Assert.True(navigator.PresentationRevision > pushed);
    }
}
