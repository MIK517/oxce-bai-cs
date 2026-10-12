namespace Oxce.Platform.Sdl;

public sealed record SdlRunDiagnostics(
    string SdlVersion,
    string VideoDriver,
    string Renderer,
    int TickCount,
    int PresentedFrameCount,
    int SuppressedPresentationCount,
    TimeSpan TotalPresentationDuration,
    TimeSpan MaximumPresentationDuration,
    TimeSpan TotalUploadDuration = default,
    TimeSpan MaximumUploadDuration = default)
{
    /// <summary>Average indexed-to-RGBA conversion and texture upload per presented frame, without the
    /// render and present calls that may wait for the display.</summary>
    public TimeSpan AverageUploadDuration => PresentedFrameCount == 0
        ? TimeSpan.Zero
        : TotalUploadDuration / PresentedFrameCount;

    public TimeSpan AveragePresentationDuration => PresentedFrameCount == 0
        ? TimeSpan.Zero
        : TotalPresentationDuration / PresentedFrameCount;
}
