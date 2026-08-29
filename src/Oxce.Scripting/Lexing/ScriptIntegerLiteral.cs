using System.Globalization;

namespace Oxce.Scripting.Lexing;

public static class ScriptIntegerLiteral
{
    public static bool TryParse(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.IsEmpty)
        {
            return false;
        }

        var negative = false;
        if (text[0] is '+' or '-')
        {
            negative = text[0] == '-';
            text = text[1..];
            if (text.IsEmpty)
            {
                return false;
            }
        }

        var numberBase = 10;
        if (text.Length >= 2 && text[0] == '0')
        {
            numberBase = text[1] switch
            {
                'x' or 'X' => 16,
                'b' or 'B' => 2,
                'o' or 'O' => 8,
                _ => 10,
            };
            if (numberBase != 10)
            {
                text = text[2..];
                if (text.IsEmpty)
                {
                    return false;
                }
            }
        }

        uint magnitude = 0;
        foreach (var character in text)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => -1,
            };
            if (digit < 0 || digit >= numberBase)
            {
                return false;
            }

            magnitude = unchecked(magnitude * (uint)numberBase + (uint)digit);
        }

        value = unchecked((int)magnitude);
        if (negative)
        {
            value = unchecked(-value);
        }
        return true;
    }

    public static int Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text.AsSpan(), out var value)
            ? value
            : throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{text}' is not a signed 32-bit OXCE script integer."));
    }
}
