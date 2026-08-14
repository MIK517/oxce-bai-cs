using System.Runtime.InteropServices;

namespace Oxce.Platform.Sdl;

internal static partial class SdlNative
{
    internal const uint InitAudio = 0x00000010;
    internal const uint InitVideo = 0x00000020;
    internal const int TextureAccessStreaming = 1;
    internal const int ScaleModeNearest = 0;
    internal const int LogicalPresentationLetterbox = 2;
    internal const ulong WindowResizable = 0x0000000000000020;
    internal const uint PixelFormatRgba32LittleEndian = 0x16762004;
    internal const uint PixelFormatRgba32BigEndian = 0x16462004;
    internal const uint EventQuit = 0x100;
    internal const uint EventWindowResized = 0x206;
    internal const uint EventWindowMinimized = 0x209;
    internal const uint EventWindowRestored = 0x20b;
    internal const uint EventWindowFocusGained = 0x20e;
    internal const uint EventWindowFocusLost = 0x20f;
    internal const uint EventWindowCloseRequested = 0x210;
    internal const uint EventKeyDown = 0x300;
    internal const uint EventKeyUp = 0x301;
    internal const uint EventTextInput = 0x303;
    internal const uint EventMouseMotion = 0x400;
    internal const uint EventMouseButtonDown = 0x401;
    internal const uint EventMouseButtonUp = 0x402;
    internal const uint EventMouseWheel = 0x403;
    internal const uint MouseWheelFlipped = 1;

    private const string LibraryName = "SDL3";

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_InitSubSystem(uint initFlags);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_QuitSubSystem(uint initFlags);

    [LibraryImport(LibraryName)]
    internal static unsafe partial IntPtr SDL_OpenAudioDeviceStream(
        uint deviceId,
        SdlAudioSpec* specification,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_PutAudioStreamData(IntPtr stream, IntPtr buffer, int length);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_ResumeAudioStreamDevice(IntPtr stream);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_DestroyAudioStream(IntPtr stream);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr SDL_CreateWindow(string title, int width, int height, ulong flags);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr SDL_CreateRenderer(IntPtr window, IntPtr name);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_StartTextInput(IntPtr window);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_StopTextInput(IntPtr window);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr SDL_CreateTexture(
        IntPtr renderer,
        uint format,
        int access,
        int width,
        int height);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_SetTextureScaleMode(IntPtr texture, int scaleMode);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_SetRenderLogicalPresentation(
        IntPtr renderer,
        int width,
        int height,
        int mode);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_PollEvent(ref SdlEvent sdlEvent);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_ConvertEventToRenderCoordinates(IntPtr renderer, ref SdlEvent sdlEvent);

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
    internal static partial bool SDL_RenderClear(IntPtr renderer);

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

    internal static string GetError() =>
        Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL did not provide an error message";
}
