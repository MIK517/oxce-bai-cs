namespace Oxce.Platform.Sdl;

public sealed record SdlRunDiagnostics(
    string SdlVersion,
    string VideoDriver,
    string Renderer,
    int TickCount,
    int PresentedFrameCount);
