using System.Runtime.InteropServices;
using Oxce.Engine.Input;
using Oxce.Platform.Sdl;
using Xunit;

namespace Oxce.UnitTests.Platform;

public sealed class SdlEventTranslatorTests
{
    [Fact]
    public void NativeEventUnionHasPinnedSdl3Size()
    {
        Assert.Equal(128, Marshal.SizeOf<SdlEvent>());
        Assert.Equal(32, Marshal.SizeOf<SdlWindowEvent>());
        Assert.Equal(40, Marshal.SizeOf<SdlKeyboardEvent>());
        Assert.Equal(32, Marshal.SizeOf<SdlTextInputEvent>());
        Assert.Equal(48, Marshal.SizeOf<SdlMouseMotionEvent>());
        Assert.Equal(40, Marshal.SizeOf<SdlMouseButtonEvent>());
        Assert.Equal(56, Marshal.SizeOf<SdlMouseWheelEvent>());
        Assert.Equal(32, Marshal.OffsetOf<SdlKeyboardEvent>(nameof(SdlKeyboardEvent.Modifiers)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<SdlTextInputEvent>(nameof(SdlTextInputEvent.Text)).ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<SdlMouseWheelEvent>(nameof(SdlMouseWheelEvent.MouseX)).ToInt32());
    }

    [Fact]
    public void KeyboardEventPreservesCodesModifiersAndRepeat()
    {
        var source = new SdlEvent
        {
            Key = new SdlKeyboardEvent
            {
                Type = SdlNative.EventKeyDown,
                Timestamp = 123,
                WindowId = 7,
                ScanCode = 61,
                KeyCode = DesktopQuitShortcut.F4Key,
                Modifiers = InputKeyModifiers.LeftAlt,
                Repeat = 1,
            },
        };

        Assert.True(SdlEventTranslator.TryTranslate(source, out var input));
        Assert.Equal(GameInputEventKind.KeyPressed, input.Kind);
        Assert.Equal(123ul, input.TimestampNanoseconds);
        Assert.Equal(7u, input.WindowId);
        Assert.Equal(61u, input.ScanCode);
        Assert.Equal(DesktopQuitShortcut.F4Key, input.KeyCode);
        Assert.Equal(InputKeyModifiers.LeftAlt, input.Modifiers);
        Assert.True(input.IsRepeat);
    }

    [Fact]
    public void FlippedWheelNormalizesDeltas()
    {
        var source = new SdlEvent
        {
            Wheel = new SdlMouseWheelEvent
            {
                Type = SdlNative.EventMouseWheel,
                Timestamp = 456,
                WindowId = 8,
                X = 2,
                Y = -3,
                Direction = SdlNative.MouseWheelFlipped,
                MouseX = 12,
                MouseY = 34,
            },
        };

        Assert.True(SdlEventTranslator.TryTranslate(source, out var input));
        Assert.Equal(GameInputEventKind.PointerWheel, input.Kind);
        Assert.Equal(-2, input.DeltaX);
        Assert.Equal(3, input.DeltaY);
        Assert.Equal(12, input.X);
        Assert.Equal(34, input.Y);
    }

    [Fact]
    public void WindowCloseBecomesQuitRequest()
    {
        var source = new SdlEvent
        {
            Window = new SdlWindowEvent
            {
                Type = SdlNative.EventWindowCloseRequested,
                Timestamp = 789,
                WindowId = 9,
            },
        };

        Assert.True(SdlEventTranslator.TryTranslate(source, out var input));
        Assert.Equal(GameInputEventKind.QuitRequested, input.Kind);
        Assert.Equal(9u, input.WindowId);
    }
}
