using Microsoft.Win32.SafeHandles;

namespace Oxce.Platform.Sdl;

internal sealed class SdlWindowHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SdlWindowHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        SdlNative.SDL_DestroyWindow(handle);
        return true;
    }
}

internal sealed class SdlRendererHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SdlRendererHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        SdlNative.SDL_DestroyRenderer(handle);
        return true;
    }
}

internal sealed class SdlTextureHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SdlTextureHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        SdlNative.SDL_DestroyTexture(handle);
        return true;
    }
}
