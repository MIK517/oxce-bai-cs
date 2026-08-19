using Oxce.Engine.Input;
using Oxce.Rendering;

namespace Oxce.Engine;

/// <summary>Platform-neutral indexed-frame client driven by a window host.</summary>
public interface IIndexedLoopClient
{
    IndexedSurface Frame { get; }

    IndexedPalette Palette { get; }

    bool ExitRequested { get; }

    void HandleInput(in GameInputEvent input);

    /// <summary>Advances simulation by one fixed host-selected step.</summary>
    void Tick(TimeSpan fixedInterval);
}
