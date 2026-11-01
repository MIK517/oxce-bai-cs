using Oxce.Engine.Input;
using Oxce.Rendering;

namespace Oxce.Engine.Geoscape;

/// <summary>What a geoscape-owned screen above the globe is for.</summary>
internal enum GeoscapeMode
{
    /// <summary>SelectDestinationState: a craft's destination is chosen on the globe.</summary>
    Selecting,

    /// <summary>BuildNewBaseState: a base site is chosen on the globe.</summary>
    Placing,

    /// <summary>The shown popup window (notification, craft, intercept, confirmation).</summary>
    Popup,

    /// <summary>BaseNameState: a name is typed.</summary>
    Naming,
}

/// <summary>
/// The root of the geoscape's <see cref="ScreenNavigator"/>: the globe, its sidebar and timers. It is not an entry, so
/// <see cref="ScreenNavigator.Covers"/> of it says that something sits above the globe, which is what the host clock
/// pauses on. It draws into the client's frame.
/// </summary>
internal sealed class GeoscapeGlobeScreen(GeoscapeClient owner) : IIndexedLoopClient
{
    public IndexedSurface Frame => owner.GlobeFrame;
    public IndexedPalette Palette => owner.GlobePalette;
    public long PresentationRevision => 0;
    public bool ExitRequested => false;
    public void HandleInput(in GameInputEvent input) => owner.HandleGlobeInput(input);
    public void Tick(TimeSpan fixedInterval) => owner.TickGlobe(fixedInterval);
}

/// <summary>
/// A translucent screen the geoscape pushes above the globe while a popup, a name box or a destination or site choice
/// is open. It is on the stack so the stack, not the client's own flags, is what covers the clock. As in the reference
/// (<c>Game::run</c> thinks only the top state) a popup or name box stops the globe's timers, while the destination and
/// site choices keep the globe thinking (<c>SelectDestinationState::think</c>, <c>BuildNewBaseState::think</c>). The window
/// itself is drawn into the client's frame, which is why every mode shares it.
/// </summary>
internal sealed class GeoscapeModeScreen(GeoscapeClient owner, GeoscapeMode mode) : IIndexedLoopClient
{
    public GeoscapeMode Mode { get; } = mode;
    public IndexedSurface Frame => owner.GlobeFrame;
    public IndexedPalette Palette => owner.GlobePalette;
    public long PresentationRevision => 0;
    public bool ExitRequested => false;
    public void HandleInput(in GameInputEvent input) => owner.HandleModeInput(Mode, input);
    public void Tick(TimeSpan fixedInterval)
    {
        if (Mode is GeoscapeMode.Popup or GeoscapeMode.Naming) owner.TickCovered(fixedInterval);
        else owner.TickGlobe(fixedInterval);
    }
}
