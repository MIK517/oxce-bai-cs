using System.Runtime.InteropServices;
using Oxce.Rendering;

namespace Oxce.Platform.Sdl;

public static class SdlIndexedFramePresenter
{
    public static void ShowFrame(
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
        if (duration < TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var rgba = new byte[checked(surface.Pixels.Length * IndexedFrameConverter.RgbaBytesPerPixel)];
        IndexedFrameConverter.ConvertToRgba32(surface, palette, rgba);
        if (!SdlNative.SDL_Init(SdlNative.InitVideo))
        {
            throw Error("SDL_Init");
        }

        try
        {
            using var window = CreateWindow(
                title,
                checked(surface.Width * scale),
                checked(surface.Height * scale));
            using var renderer = CreateRenderer(window);
            using var texture = CreateTexture(renderer, surface.Width, surface.Height);
            var pinnedPixels = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try
            {
                RequireSuccess(
                    SdlNative.SDL_UpdateTexture(
                        texture.DangerousGetHandle(),
                        IntPtr.Zero,
                        pinnedPixels.AddrOfPinnedObject(),
                        checked(surface.Width * IndexedFrameConverter.RgbaBytesPerPixel)),
                    "SDL_UpdateTexture");
            }
            finally
            {
                pinnedPixels.Free();
            }

            RequireSuccess(
                SdlNative.SDL_RenderTexture(
                    renderer.DangerousGetHandle(),
                    texture.DangerousGetHandle(),
                    IntPtr.Zero,
                    IntPtr.Zero),
                "SDL_RenderTexture");
            RequireSuccess(SdlNative.SDL_RenderPresent(renderer.DangerousGetHandle()), "SDL_RenderPresent");
            SdlNative.SDL_Delay((uint)Math.Ceiling(duration.TotalMilliseconds));
        }
        finally
        {
            SdlNative.SDL_Quit();
        }
    }

    private static SdlWindowHandle CreateWindow(string title, int width, int height)
    {
        var handle = SdlNative.SDL_CreateWindow(title, width, height, flags: 0);
        return handle == IntPtr.Zero ? throw Error("SDL_CreateWindow") : new SdlWindowHandle(handle);
    }

    private static SdlRendererHandle CreateRenderer(SdlWindowHandle window)
    {
        var handle = SdlNative.SDL_CreateRenderer(window.DangerousGetHandle(), IntPtr.Zero);
        return handle == IntPtr.Zero ? throw Error("SDL_CreateRenderer") : new SdlRendererHandle(handle);
    }

    private static SdlTextureHandle CreateTexture(SdlRendererHandle renderer, int width, int height)
    {
        var format = BitConverter.IsLittleEndian
            ? SdlNative.PixelFormatRgba32LittleEndian
            : SdlNative.PixelFormatRgba32BigEndian;
        var handle = SdlNative.SDL_CreateTexture(
            renderer.DangerousGetHandle(),
            format,
            SdlNative.TextureAccessStreaming,
            width,
            height);
        return handle == IntPtr.Zero ? throw Error("SDL_CreateTexture") : new SdlTextureHandle(handle);
    }

    private static void RequireSuccess(bool success, string operation)
    {
        if (!success)
        {
            throw Error(operation);
        }
    }

    private static SdlException Error(string operation) => new(operation, SdlNative.GetError());
}
