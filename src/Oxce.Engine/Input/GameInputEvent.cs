namespace Oxce.Engine.Input;

public enum GameInputEventKind
{
    None,
    QuitRequested,
    KeyPressed,
    KeyReleased,
    TextInput,
    PointerMoved,
    PointerPressed,
    PointerReleased,
    PointerWheel,
    WindowResized,
    WindowMinimized,
    WindowRestored,
    FocusGained,
    FocusLost,
}

[Flags]
public enum InputKeyModifiers : ushort
{
    None = 0,
    LeftShift = 0x0001,
    RightShift = 0x0002,
    Level5Shift = 0x0004,
    LeftControl = 0x0040,
    RightControl = 0x0080,
    LeftAlt = 0x0100,
    RightAlt = 0x0200,
    LeftGui = 0x0400,
    RightGui = 0x0800,
    NumLock = 0x1000,
    CapsLock = 0x2000,
    Mode = 0x4000,
    ScrollLock = 0x8000,

    Shift = LeftShift | RightShift,
    Control = LeftControl | RightControl,
    Alt = LeftAlt | RightAlt,
    Gui = LeftGui | RightGui,
}

public readonly record struct GameInputEvent
{
    private GameInputEvent(GameInputEventKind kind, ulong timestampNanoseconds, uint windowId)
    {
        Kind = kind;
        TimestampNanoseconds = timestampNanoseconds;
        WindowId = windowId;
    }

    public GameInputEventKind Kind { get; init; }

    public ulong TimestampNanoseconds { get; init; }

    public uint WindowId { get; init; }

    public uint ScanCode { get; init; }

    public uint KeyCode { get; init; }

    public InputKeyModifiers Modifiers { get; init; }

    public bool IsRepeat { get; init; }

    public string? Text { get; init; }

    public float X { get; init; }

    public float Y { get; init; }

    public float DeltaX { get; init; }

    public float DeltaY { get; init; }

    public uint PointerButtons { get; init; }

    public byte PointerButton { get; init; }

    public byte ClickCount { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public static GameInputEvent Simple(
        GameInputEventKind kind,
        ulong timestampNanoseconds,
        uint windowId = 0)
    {
        if (kind is not (
            GameInputEventKind.QuitRequested or
            GameInputEventKind.WindowMinimized or
            GameInputEventKind.WindowRestored or
            GameInputEventKind.FocusGained or
            GameInputEventKind.FocusLost))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new GameInputEvent(kind, timestampNanoseconds, windowId);
    }

    public static GameInputEvent Key(
        GameInputEventKind kind,
        ulong timestampNanoseconds,
        uint windowId,
        uint scanCode,
        uint keyCode,
        InputKeyModifiers modifiers,
        bool isRepeat = false)
    {
        if (kind is not (GameInputEventKind.KeyPressed or GameInputEventKind.KeyReleased))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new GameInputEvent(kind, timestampNanoseconds, windowId)
        {
            ScanCode = scanCode,
            KeyCode = keyCode,
            Modifiers = modifiers,
            IsRepeat = isRepeat,
        };
    }

    public static GameInputEvent TextEntry(
        ulong timestampNanoseconds,
        uint windowId,
        string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new GameInputEvent(GameInputEventKind.TextInput, timestampNanoseconds, windowId)
        {
            Text = text,
        };
    }

    public static GameInputEvent PointerMotion(
        ulong timestampNanoseconds,
        uint windowId,
        float x,
        float y,
        float deltaX,
        float deltaY,
        uint pointerButtons) => new(GameInputEventKind.PointerMoved, timestampNanoseconds, windowId)
        {
            X = x,
            Y = y,
            DeltaX = deltaX,
            DeltaY = deltaY,
            PointerButtons = pointerButtons,
        };

    public static GameInputEvent PointerButtonChange(
        GameInputEventKind kind,
        ulong timestampNanoseconds,
        uint windowId,
        float x,
        float y,
        byte button,
        byte clickCount)
    {
        if (kind is not (GameInputEventKind.PointerPressed or GameInputEventKind.PointerReleased))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new GameInputEvent(kind, timestampNanoseconds, windowId)
        {
            X = x,
            Y = y,
            PointerButton = button,
            ClickCount = clickCount,
        };
    }

    public static GameInputEvent PointerWheel(
        ulong timestampNanoseconds,
        uint windowId,
        float x,
        float y,
        float deltaX,
        float deltaY) => new(GameInputEventKind.PointerWheel, timestampNanoseconds, windowId)
        {
            X = x,
            Y = y,
            DeltaX = deltaX,
            DeltaY = deltaY,
        };

    public static GameInputEvent Resize(
        ulong timestampNanoseconds,
        uint windowId,
        int width,
        int height) => new(GameInputEventKind.WindowResized, timestampNanoseconds, windowId)
        {
            Width = width,
            Height = height,
        };
}
