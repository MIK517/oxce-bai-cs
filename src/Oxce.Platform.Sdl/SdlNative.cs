using System.Runtime.InteropServices;

namespace Oxce.Platform.Sdl;

internal static partial class SdlNative
{
    internal const uint InitVideo = 0x00000020;
    internal const int TextureAccessStreaming = 1;
    internal const uint PixelFormatRgba32LittleEndian = 0x16762004;
    internal const uint PixelFormatRgba32BigEndian = 0x16462004;

    private const string LibraryName = "SDL3";

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_Init(uint initFlags);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr SDL_CreateWindow(string title, int width, int height, ulong flags);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr SDL_CreateRenderer(IntPtr window, IntPtr name);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr SDL_CreateTexture(
        IntPtr renderer,
        uint format,
        int access,
        int width,
        int height);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_UpdateTexture(IntPtr texture, IntPtr rectangle, IntPtr pixels, int pitch);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_RenderTexture(
        IntPtr renderer,
        IntPtr texture,
        IntPtr sourceRectangle,
        IntPtr destinationRectangle);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_RenderPresent(IntPtr renderer);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_DestroyTexture(IntPtr texture);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_DestroyRenderer(IntPtr renderer);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_DestroyWindow(IntPtr window);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_Delay(uint milliseconds);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr SDL_GetError();

    [LibraryImport(LibraryName)]
    internal static partial void SDL_Quit();

    internal static string GetError() =>
        Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL did not provide an error message";
}
