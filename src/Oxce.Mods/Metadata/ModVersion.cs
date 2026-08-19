namespace Oxce.Mods.Metadata;

public sealed class ModVersion
{
    private readonly byte[] _normalized;

    private ModVersion(string text, byte[] normalized, string? error)
    {
        Text = text;
        _normalized = normalized;
        Error = error;
    }

    public string Text { get; }

    public string? Error { get; }

    public bool IsValid => Error is null;

    public static ModVersion Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        const byte numberPrefixMaximum = 10;
        const byte textPrefix = 11;
        var normalized = new List<byte>(text.Length + 1);
        var state = TokenState.Nothing;
        var lastNumberPrefix = -1;

        foreach (var sourceCharacter in text)
        {
            var character = sourceCharacter is >= 'a' and <= 'z'
                ? (char)(sourceCharacter + ('A' - 'a'))
                : sourceCharacter;
            if (character is >= 'A' and <= 'Z')
            {
                if (state != TokenState.Text)
                {
                    normalized.Add(textPrefix);
                }

                state = TokenState.Text;
                normalized.Add(checked((byte)character));
            }
            else if (character is >= '0' and <= '9')
            {
                if (state != TokenState.Number)
                {
                    state = TokenState.Number;
                    lastNumberPrefix = normalized.Count;
                    normalized.Add(0);
                    if (character > '0')
                    {
                        normalized[lastNumberPrefix]++;
                        normalized.Add(checked((byte)character));
                    }
                }
                else
                {
                    if (normalized[lastNumberPrefix] == numberPrefixMaximum)
                    {
                        return Invalid(text, "unsupported number length");
                    }

                    if (normalized[lastNumberPrefix] != 0 || character > '0')
                    {
                        normalized[lastNumberPrefix]++;
                        normalized.Add(checked((byte)character));
                    }
                }
            }
            else if (character == '.')
            {
                if (state == TokenState.Dot)
                {
                    return Invalid(text, "duplicated dots");
                }

                state = TokenState.Dot;
            }
            else
            {
                return Invalid(text, "unexpected symbol");
            }
        }

        while (normalized.Count > 1 && normalized[^1] == 0)
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        return new ModVersion(text, normalized.ToArray(), null);
    }

    public bool Satisfies(ModVersion required)
    {
        ArgumentNullException.ThrowIfNull(required);
        return string.Equals(Text, required.Text, StringComparison.Ordinal) || CompareNormalized(required) >= 0;
    }

    private int CompareNormalized(ModVersion other)
    {
        var sharedLength = Math.Min(_normalized.Length, other._normalized.Length);
        for (var index = 0; index < sharedLength; ++index)
        {
            var comparison = _normalized[index].CompareTo(other._normalized[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return _normalized.Length.CompareTo(other._normalized.Length);
    }

    public override string ToString() => Text;

    private static ModVersion Invalid(string text, string error) => new(text, [], error);

    private enum TokenState
    {
        Nothing,
        Number,
        Text,
        Dot,
    }
}
