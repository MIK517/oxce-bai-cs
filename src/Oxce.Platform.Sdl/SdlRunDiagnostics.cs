namespace Oxce.Platform.Sdl;

public sealed record SdlRunDiagnostics(
    string SdlVersion,
    string VideoDriver,
    string Renderer,
    int TickCount,
    int PresentedFrameCount,
    int SuppressedPresentationCount,
    TimeSpan TotalPresentationDuration,
    TimeSpan MaximumPresentationDuration)
{
    public TimeSpan AveragePresentationDuration => PresentedFrameCount == 0
        ? TimeSpan.Zero
        : TotalPresentationDuration / PresentedFrameCount;
}
