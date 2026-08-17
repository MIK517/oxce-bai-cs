using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Rendering;

namespace Oxce.Platform.Sdl;

public static class SdlIndexedFramePresenter
{
    public static SdlRunDiagnostics ShowFrame(
        IndexedSurface surface,
        IndexedPalette palette,
        string title,
        int scale,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        var client = new StaticFrameClient(surface, palette);
        var host = new SdlIndexedWindowHost(
            client,
            new SdlWindowOptions(title)
            {
                Scale = scale,
                MaximumRunTime = duration,
            });
        host.Run();
        return host.LastRunDiagnostics ??
            throw new InvalidOperationException("The SDL indexed-frame run did not produce diagnostics.");
    }

    private sealed class StaticFrameClient : IIndexedLoopClient
    {
        internal StaticFrameClient(IndexedSurface frame, IndexedPalette palette)
        {
            Frame = frame;
            Palette = palette;
        }

        public IndexedSurface Frame { get; }

        public IndexedPalette Palette { get; }

        public bool ExitRequested => false;

        public void HandleInput(in GameInputEvent input)
        {
        }

        public void Tick(TimeSpan elapsed)
        {
        }
    }
}
