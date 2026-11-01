using System.Text;
using Oxce.Rendering;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// Word-wrapped text and the framed button window the geoscape's popups, the naming box and later screens share
/// (the reference's <c>Window</c>, <c>Text</c> and <c>TextButton</c> in their simplest indexed form).
/// </summary>
internal static class PopupWidgets
{
    public const int Left = 36;
    public const int Width = 440;
    public const int ButtonHeight = 14;
    public const byte FrameColor = 239;
    public const byte ButtonColor = 133;
    public const byte TextColor = 138;

    /// <summary>The height <see cref="Wrap"/> would use for a text.</summary>
    public static int WrappedHeight(IndexedSpriteFont font, string text, int width)
    {
        var lines = 0;
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length != 0 && font.Measure(candidate).Width > width)
            {
                lines++;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length != 0) lines++;
        return lines * (font.Height + 2);
    }

    /// <summary>Draws word-wrapped text and returns the y below it.</summary>
    public static int Wrap(IndexedSurface surface, IndexedSpriteFont font, string text, int x, int y, int width, byte color = TextColor)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length != 0 && font.Measure(candidate).Width > width)
            {
                font.DrawTextShifted(surface, line.ToString(), x, y, color);
                y += font.Height + 2;
                line.Clear().Append(word);
            }
            else line.Clear().Append(candidate);
        }
        if (line.Length != 0)
        {
            font.DrawTextShifted(surface, line.ToString(), x, y, color);
            y += font.Height + 2;
        }
        return y;
    }

    /// <summary>The window sized to its title, lines and buttons, centred vertically in a frame of the given height.</summary>
    public static (int Top, int Height) Bounds(IndexedSpriteFont font, int frameHeight, string title, IReadOnlyList<string> lines, int buttons)
    {
        var text = WrappedHeight(font, title, Width - 20) + 6;
        foreach (var line in lines) text += WrappedHeight(font, line, Width - 20) + 2;
        var height = Math.Min(frameHeight - 8, 20 + text + 8 + buttons * (ButtonHeight + 2) + 8);
        height = Math.Max(height, 120);
        return ((frameHeight - height) / 2, height);
    }

    public static (int X, int Y, int Width, int Height) ButtonRectangle(IndexedSpriteFont font, int frameHeight, string title,
        IReadOnlyList<string> lines, int buttons, int index)
    {
        var (top, height) = Bounds(font, frameHeight, title, lines, buttons);
        var bottom = top + height - 10;
        return (Left + 10, bottom - (buttons - index) * (ButtonHeight + 2), Width - 20, ButtonHeight);
    }

    public static int ButtonAt(IndexedSpriteFont font, int frameHeight, string title, IReadOnlyList<string> lines, int buttons, int x, int y)
    {
        for (var index = 0; index < buttons; index++)
        {
            var (bx, by, bw, bh) = ButtonRectangle(font, frameHeight, title, lines, buttons, index);
            if (x >= bx && x < bx + bw && y >= by && y < by + bh) return index;
        }
        return -1;
    }

    /// <summary>Draws the framed window with its title and lines; the caller draws the buttons.</summary>
    public static void DrawWindow(IndexedSurface surface, IndexedSpriteFont font, string title, IReadOnlyList<string> lines, int buttons)
    {
        var (top, height) = Bounds(font, surface.Height, title, lines, buttons);
        surface.FillRectangle(Left, top, Width, height, 0);
        surface.FillRectangle(Left, top, Width, 2, FrameColor);
        surface.FillRectangle(Left, top + height - 2, Width, 2, FrameColor);
        surface.FillRectangle(Left, top, 2, height, FrameColor);
        surface.FillRectangle(Left + Width - 2, top, 2, height, FrameColor);
        var y = Wrap(surface, font, title, Left + 10, top + 10, Width - 20, FrameColor) + 6;
        foreach (var line in lines) y = Wrap(surface, font, line, Left + 10, y, Width - 20) + 2;
    }

    /// <summary>Draws one button; a disabled one shows as a dark bar with light text.</summary>
    public static void DrawButton(IndexedSurface surface, IndexedSpriteFont font, (int X, int Y, int Width, int Height) rectangle,
        string label, bool disabled)
    {
        surface.FillRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, disabled ? (byte)0 : ButtonColor);
        font.DrawTextShifted(surface, label, rectangle.X + 4, rectangle.Y + 3, disabled ? TextColor : (byte)0);
    }
}
