using System.Diagnostics;
using System.Runtime.InteropServices;
using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Rendering;

namespace Oxce.Platform.Sdl;

public sealed class SdlIndexedWindowHost : IGameHost
{
    private readonly IIndexedLoopClient _client;
    private readonly SdlWindowOptions _options;
    private int _presentedFrameCount;
    private int _tickCount;

    public SdlIndexedWindowHost(IIndexedLoopClient client, SdlWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _client = client;
        _options = options;
    }

    public SdlRunDiagnostics? LastRunDiagnostics { get; private set; }

    public int Run(CancellationToken cancellationToken = default)
    {
        LastRunDiagnostics = null;
        _presentedFrameCount = 0;
        _tickCount = 0;
        if (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        var initialFrame = _client.Frame;
        ArgumentNullException.ThrowIfNull(initialFrame);
        ArgumentNullException.ThrowIfNull(_client.Palette);
        var rgba = new byte[checked(initialFrame.Pixels.Length * IndexedFrameConverter.RgbaBytesPerPixel)];
        if (!SdlNative.SDL_InitSubSystem(SdlNative.InitVideo))
        {
            throw Error("SDL_InitSubSystem(SDL_INIT_VIDEO)");
        }

        try
        {
            using var window = CreateWindow(
                _options.Title,
                checked(initialFrame.Width * _options.Scale),
                checked(initialFrame.Height * _options.Scale),
                _options.Resizable);
            if (_options.EnableTextInput)
            {
                RequireSuccess(SdlNative.SDL_StartTextInput(window.DangerousGetHandle()), "SDL_StartTextInput");
            }

            using var renderer = CreateRenderer(window);
            var videoDriver = SdlRuntimeInfo.CurrentVideoDriver ?? "unknown";
            var rendererName = SdlRuntimeInfo.GetRendererName(renderer.DangerousGetHandle()) ?? "unknown";
            RequireSuccess(
                SdlNative.SDL_SetRenderLogicalPresentation(
                    renderer.DangerousGetHandle(),
                    initialFrame.Width,
                    initialFrame.Height,
                    SdlNative.LogicalPresentationLetterbox),
                "SDL_SetRenderLogicalPresentation");
            using var texture = CreateTexture(renderer, initialFrame.Width, initialFrame.Height);
            RequireSuccess(
                SdlNative.SDL_SetTextureScaleMode(texture.DangerousGetHandle(), SdlNative.ScaleModeNearest),
                "SDL_SetTextureScaleMode");
            try
            {
                var result = RunLoop(renderer, texture, initialFrame.Width, initialFrame.Height, rgba, cancellationToken);
                LastRunDiagnostics = new SdlRunDiagnostics(
                    SdlRuntimeInfo.Version,
                    videoDriver,
                    rendererName,
                    _tickCount,
                    _presentedFrameCount);
                return result;
            }
            finally
            {
                if (_options.EnableTextInput)
                {
                    SdlNative.SDL_StopTextInput(window.DangerousGetHandle());
                }
            }
        }
        finally
        {
            SdlNative.SDL_QuitSubSystem(SdlNative.InitVideo);
        }
    }

    private int RunLoop(
        SdlRendererHandle renderer,
        SdlTextureHandle texture,
        int width,
        int height,
        byte[] rgba,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var previousFrameTime = TimeSpan.Zero;
        var targetFrameTime = TimeSpan.FromSeconds(1d / _options.TargetFrameRate);
        var presented = false;
        var quit = false;
        while (!quit && !cancellationToken.IsCancellationRequested && !_client.ExitRequested)
        {
            quit = ProcessEvents(renderer);
            if (quit || cancellationToken.IsCancellationRequested || _client.ExitRequested)
            {
                break;
            }

            var currentFrameTime = clock.Elapsed;
            _client.Tick(currentFrameTime - previousFrameTime);
            _tickCount++;
            previousFrameTime = currentFrameTime;
            Present(renderer, texture, width, height, rgba);
            presented = true;

            if (_options.MaximumRunTime is { } maximumRunTime && clock.Elapsed >= maximumRunTime)
            {
                break;
            }

            var remaining = targetFrameTime - (clock.Elapsed - currentFrameTime);
            if (remaining > TimeSpan.Zero)
            {
                SdlNative.SDL_Delay((uint)Math.Floor(remaining.TotalMilliseconds));
            }
        }

        if (!presented && !cancellationToken.IsCancellationRequested && !_client.ExitRequested)
        {
            Present(renderer, texture, width, height, rgba);
        }

        return 0;
    }

    private bool ProcessEvents(SdlRendererHandle renderer)
    {
        var source = default(SdlEvent);
        while (SdlNative.SDL_PollEvent(ref source))
        {
            RequireSuccess(
                SdlNative.SDL_ConvertEventToRenderCoordinates(renderer.DangerousGetHandle(), ref source),
                "SDL_ConvertEventToRenderCoordinates");
            if (!SdlEventTranslator.TryTranslate(source, out var input))
            {
                continue;
            }

            _client.HandleInput(input);
            if (input.Kind == GameInputEventKind.QuitRequested ||
                (_options.ExitOnEscape &&
                 input.Kind == GameInputEventKind.KeyPressed &&
                 input.KeyCode == DesktopQuitShortcut.EscapeKey) ||
                DesktopQuitShortcut.IsMatch(input, GetDesktopPlatform()))
            {
                return true;
            }

            source = default;
        }

        return false;
    }

    private void Present(
        SdlRendererHandle renderer,
        SdlTextureHandle texture,
        int width,
        int height,
        byte[] rgba)
    {
        var frame = _client.Frame;
        if (frame.Width != width || frame.Height != height)
        {
            throw new InvalidOperationException("The indexed loop client changed frame dimensions while the SDL window was running.");
        }

        IndexedFrameConverter.ConvertToRgba32(frame, _client.Palette, rgba);
        var pinnedPixels = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            RequireSuccess(
                SdlNative.SDL_UpdateTexture(
                    texture.DangerousGetHandle(),
                    IntPtr.Zero,
                    pinnedPixels.AddrOfPinnedObject(),
                    checked(width * IndexedFrameConverter.RgbaBytesPerPixel)),
                "SDL_UpdateTexture");
        }
        finally
        {
            pinnedPixels.Free();
        }

        RequireSuccess(SdlNative.SDL_RenderClear(renderer.DangerousGetHandle()), "SDL_RenderClear");
        RequireSuccess(
            SdlNative.SDL_RenderTexture(
                renderer.DangerousGetHandle(),
                texture.DangerousGetHandle(),
                IntPtr.Zero,
                IntPtr.Zero),
            "SDL_RenderTexture");
        RequireSuccess(SdlNative.SDL_RenderPresent(renderer.DangerousGetHandle()), "SDL_RenderPresent");
        _presentedFrameCount++;
    }

    private static DesktopPlatform GetDesktopPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return DesktopPlatform.Windows;
        }

        return OperatingSystem.IsMacOS() ? DesktopPlatform.MacOS : DesktopPlatform.Other;
    }

    private static SdlWindowHandle CreateWindow(string title, int width, int height, bool resizable)
    {
        var flags = resizable ? SdlNative.WindowResizable : 0;
        var handle = SdlNative.SDL_CreateWindow(title, width, height, flags);
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
