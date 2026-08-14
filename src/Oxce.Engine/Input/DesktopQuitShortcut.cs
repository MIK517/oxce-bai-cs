namespace Oxce.Engine.Input;

public enum DesktopPlatform
{
    Other,
    Windows,
    MacOS,
}

public static class DesktopQuitShortcut
{
    public const uint EscapeKey = 0x0000001b;
    public const uint QKey = 0x00000071;
    public const uint F4Key = 0x4000003d;

    public static bool IsMatch(in GameInputEvent input, DesktopPlatform platform)
    {
        if (input.Kind != GameInputEventKind.KeyPressed)
        {
            return false;
        }

        return platform switch
        {
            DesktopPlatform.Windows => input.KeyCode == F4Key &&
                (input.Modifiers & InputKeyModifiers.Alt) != 0,
            DesktopPlatform.MacOS => input.KeyCode == QKey &&
                (input.Modifiers & InputKeyModifiers.Gui) != 0,
            _ => false,
        };
    }
}
