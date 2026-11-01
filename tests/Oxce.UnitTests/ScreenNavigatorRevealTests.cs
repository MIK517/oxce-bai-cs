using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests;

/// <summary>The navigator tells the screen a pop reveals (the reference's <c>State::init</c>), and a replace does not.</summary>
public sealed class ScreenNavigatorRevealTests
{
    private sealed class Screen : IIndexedLoopClient, IRevealedScreen
    {
        public IndexedSurface Frame { get; } = new(8, 8);
        public IndexedPalette Palette { get; } = new(new Oxce.Core.Graphics.Rgba32[256]);
        public long PresentationRevision => 0;
        public bool ExitRequested { get; set; }
        public int Revealed { get; private set; }
        public void HandleInput(in GameInputEvent input) => ExitRequested = true;
        public void Tick(TimeSpan fixedInterval) { }
        void IRevealedScreen.Revealed() => Revealed++;
    }

    [Fact]
    public void APopRevealsTheScreenBelowAndAReplaceDoesNot()
    {
        var navigator = new ScreenNavigator();
        Screen bottom = new(), middle = new(), top = new();
        navigator.Push(bottom);
        navigator.Push(middle);
        navigator.Replace(top);
        Assert.Equal(0, bottom.Revealed);
        Assert.Equal(0, middle.Revealed);
        Assert.True(navigator.Pop());
        Assert.Equal(1, bottom.Revealed);
        // A screen that exits on input is popped by the navigator and reveals the one below.
        navigator.Push(top);
        navigator.HandleInput(GameInputEvent.Simple(GameInputEventKind.FocusGained, 0));
        Assert.Same(bottom, navigator.Top);
        Assert.Equal(2, bottom.Revealed);
    }
}
