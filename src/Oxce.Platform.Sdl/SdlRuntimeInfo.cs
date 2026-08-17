using System.Runtime.InteropServices;

namespace Oxce.Platform.Sdl;

public static class SdlRuntimeInfo
{
    public static string Version => FormatVersion(SdlNative.SDL_GetVersion());

    public static string? CurrentVideoDriver => ReadUtf8(SdlNative.SDL_GetCurrentVideoDriver());

    public static string? CurrentAudioDriver => ReadUtf8(SdlNative.SDL_GetCurrentAudioDriver());

    internal static string FormatVersion(int version) =>
        $"{version / 1_000_000}.{version / 1_000 % 1_000}.{version % 1_000}";

    internal static string? GetRendererName(IntPtr renderer) =>
        ReadUtf8(SdlNative.SDL_GetRendererName(renderer));

    private static string? ReadUtf8(IntPtr value) =>
        value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);
}
