using System.Runtime.InteropServices;
using Oxce.Engine.Input;

namespace Oxce.Platform.Sdl;

[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct SdlEvent
{
    [FieldOffset(0)]
    internal uint Type;

    [FieldOffset(0)]
    internal SdlWindowEvent Window;

    [FieldOffset(0)]
    internal SdlKeyboardEvent Key;

    [FieldOffset(0)]
    internal SdlTextInputEvent Text;

    [FieldOffset(0)]
    internal SdlMouseMotionEvent Motion;

    [FieldOffset(0)]
    internal SdlMouseButtonEvent Button;

    [FieldOffset(0)]
    internal SdlMouseWheelEvent Wheel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlWindowEvent
{
    internal uint Type;
    internal uint Reserved;
    internal ulong Timestamp;
    internal uint WindowId;
    internal int Data1;
    internal int Data2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlKeyboardEvent
{
    internal uint Type;
    internal uint Reserved;
    internal ulong Timestamp;
    internal uint WindowId;
    internal uint Which;
    internal uint ScanCode;
    internal uint KeyCode;
    internal InputKeyModifiers Modifiers;
    internal ushort Raw;
    internal byte Down;
    internal byte Repeat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlTextInputEvent
{
    internal uint Type;
    internal uint Reserved;
    internal ulong Timestamp;
    internal uint WindowId;
    internal IntPtr Text;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlMouseMotionEvent
{
    internal uint Type;
    internal uint Reserved;
    internal ulong Timestamp;
    internal uint WindowId;
    internal uint Which;
    internal uint State;
    internal float X;
    internal float Y;
    internal float DeltaX;
    internal float DeltaY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlMouseButtonEvent
{
    internal uint Type;
    internal uint Reserved;
    internal ulong Timestamp;
    internal uint WindowId;
    internal uint Which;
    internal byte Button;
    internal byte Down;
    internal byte Clicks;
    internal byte Padding;
    internal float X;
    internal float Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlMouseWheelEvent
{
    internal uint Type;
    internal uint Reserved;
    internal ulong Timestamp;
    internal uint WindowId;
    internal uint Which;
    internal float X;
    internal float Y;
    internal uint Direction;
    internal float MouseX;
    internal float MouseY;
    internal int IntegerX;
    internal int IntegerY;
}

internal static class SdlEventTranslator
{
    internal static bool TryTranslate(in SdlEvent source, out GameInputEvent input)
    {
        switch (source.Type)
        {
            case SdlNative.EventQuit:
                input = GameInputEvent.Simple(GameInputEventKind.QuitRequested, ReadTimestamp(source));
                return true;
            case SdlNative.EventWindowCloseRequested:
                input = GameInputEvent.Simple(
                    GameInputEventKind.QuitRequested,
                    source.Window.Timestamp,
                    source.Window.WindowId);
                return true;
            case SdlNative.EventWindowResized:
                input = GameInputEvent.Resize(
                    source.Window.Timestamp,
                    source.Window.WindowId,
                    source.Window.Data1,
                    source.Window.Data2);
                return true;
            case SdlNative.EventWindowMinimized:
                return TranslateWindowSimple(source, GameInputEventKind.WindowMinimized, out input);
            case SdlNative.EventWindowRestored:
                return TranslateWindowSimple(source, GameInputEventKind.WindowRestored, out input);
            case SdlNative.EventWindowFocusGained:
                return TranslateWindowSimple(source, GameInputEventKind.FocusGained, out input);
            case SdlNative.EventWindowFocusLost:
                return TranslateWindowSimple(source, GameInputEventKind.FocusLost, out input);
            case SdlNative.EventKeyDown:
            case SdlNative.EventKeyUp:
                input = GameInputEvent.Key(
                    source.Type == SdlNative.EventKeyDown
                        ? GameInputEventKind.KeyPressed
                        : GameInputEventKind.KeyReleased,
                    source.Key.Timestamp,
                    source.Key.WindowId,
                    source.Key.ScanCode,
                    source.Key.KeyCode,
                    source.Key.Modifiers,
                    source.Key.Repeat != 0);
                return true;
            case SdlNative.EventTextInput:
                var text = Marshal.PtrToStringUTF8(source.Text.Text);
                if (string.IsNullOrEmpty(text))
                {
                    input = default;
                    return false;
                }

                input = GameInputEvent.TextEntry(source.Text.Timestamp, source.Text.WindowId, text);
                return true;
            case SdlNative.EventMouseMotion:
                input = GameInputEvent.PointerMotion(
                    source.Motion.Timestamp,
                    source.Motion.WindowId,
                    source.Motion.X,
                    source.Motion.Y,
                    source.Motion.DeltaX,
                    source.Motion.DeltaY,
                    source.Motion.State);
                return true;
            case SdlNative.EventMouseButtonDown:
            case SdlNative.EventMouseButtonUp:
                input = GameInputEvent.PointerButtonChange(
                    source.Type == SdlNative.EventMouseButtonDown
                        ? GameInputEventKind.PointerPressed
                        : GameInputEventKind.PointerReleased,
                    source.Button.Timestamp,
                    source.Button.WindowId,
                    source.Button.X,
                    source.Button.Y,
                    source.Button.Button,
                    source.Button.Clicks);
                return true;
            case SdlNative.EventMouseWheel:
                var direction = source.Wheel.Direction == SdlNative.MouseWheelFlipped ? -1 : 1;
                input = GameInputEvent.PointerWheel(
                    source.Wheel.Timestamp,
                    source.Wheel.WindowId,
                    source.Wheel.MouseX,
                    source.Wheel.MouseY,
                    source.Wheel.X * direction,
                    source.Wheel.Y * direction);
                return true;
            default:
                input = default;
                return false;
        }
    }

    private static bool TranslateWindowSimple(
        in SdlEvent source,
        GameInputEventKind kind,
        out GameInputEvent input)
    {
        input = GameInputEvent.Simple(kind, source.Window.Timestamp, source.Window.WindowId);
        return true;
    }

    private static ulong ReadTimestamp(in SdlEvent source) => source.Window.Timestamp;
}
