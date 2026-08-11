using System.Globalization;

namespace Oxce.Formats.Yaml;

public static class YamlValueReader
{
    private const NumberStyles FloatingStyles = NumberStyles.AllowLeadingSign |
        NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowExponent;

    public static string ReadString(YamlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlNullNode nullNode => nullNode.Spelling,
            _ => throw TypeError(node, "string"),
        };
    }

    public static string ReadString(YamlMappingNode mapping, string key, string defaultValue) =>
        mapping.TryGet(key, out var node) ? ReadString(node!) : defaultValue;

    public static sbyte ReadInt8(YamlNode node) =>
        TryReadInt8(node, out var value) ? value : throw TypeError(node, "Int8");

    public static byte ReadUInt8(YamlNode node) =>
        TryReadUInt8(node, out var value) ? value : throw TypeError(node, "UInt8");

    public static short ReadInt16(YamlNode node) =>
        TryReadInt16(node, out var value) ? value : throw TypeError(node, "Int16");

    public static ushort ReadUInt16(YamlNode node) =>
        TryReadUInt16(node, out var value) ? value : throw TypeError(node, "UInt16");

    public static int ReadInt32(YamlNode node) =>
        TryReadInt32(node, out var value) ? value : throw TypeError(node, "Int32");

    public static uint ReadUInt32(YamlNode node) =>
        TryReadUInt32(node, out var value) ? value : throw TypeError(node, "UInt32");

    public static long ReadInt64(YamlNode node) =>
        TryReadInt64(node, out var value) ? value : throw TypeError(node, "Int64");

    public static ulong ReadUInt64(YamlNode node) =>
        TryReadUInt64(node, out var value) ? value : throw TypeError(node, "UInt64");

    public static float ReadSingle(YamlNode node) =>
        TryReadSingle(node, out var value) ? value : throw TypeError(node, "Single");

    public static double ReadDouble(YamlNode node) =>
        TryReadDouble(node, out var value) ? value : throw TypeError(node, "Double");

    public static bool ReadBoolean(YamlNode node) =>
        TryReadBoolean(node, out var value) ? value : throw TypeError(node, "Boolean");

    public static byte[] ReadBase64(YamlNode node, int maxDecodedBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDecodedBytes);
        var text = ScalarText(node);
        if (text is null || !TryGetBase64Length(text, out var decodedLength))
        {
            throw TypeError(node, "Base64");
        }

        if (decodedLength > maxDecodedBytes)
        {
            throw new YamlFormatException(
                $"Decoded YAML Base64 value exceeds the {maxDecodedBytes}-byte limit.",
                node.Span);
        }

        var result = new byte[decodedLength];
        if (!Convert.TryFromBase64String(text, result, out var bytesWritten) || bytesWritten != decodedLength)
        {
            throw TypeError(node, "Base64");
        }

        return result;
    }

    public static TEnum ReadEnum<TEnum>(YamlNode node)
        where TEnum : struct, Enum =>
        TryReadEnum(node, out TEnum value) ? value : throw TypeError(node, typeof(TEnum).Name);

    public static int ReadInt32(YamlMappingNode mapping, string key, int defaultValue) =>
        mapping.TryGet(key, out var node) ? ReadInt32(node!) : defaultValue;

    public static bool ReadBoolean(YamlMappingNode mapping, string key, bool defaultValue) =>
        mapping.TryGet(key, out var node) ? ReadBoolean(node!) : defaultValue;

    public static TEnum ReadEnum<TEnum>(YamlMappingNode mapping, string key, TEnum defaultValue)
        where TEnum : struct, Enum =>
        mapping.TryGet(key, out var node) ? ReadEnum<TEnum>(node!) : defaultValue;

    public static T[] ReadSequence<T>(YamlNode node, Func<YamlNode, T> readItem)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(readItem);

        return node switch
        {
            YamlSequenceNode sequence => sequence.Items.Select(readItem).ToArray(),
            YamlMappingNode mapping => mapping.Entries.Select(entry => readItem(entry.Value)).ToArray(),
            _ => [],
        };
    }

    public static SortedDictionary<TKey, TValue> ReadMap<TKey, TValue>(
        YamlNode node,
        Func<YamlNode, TKey> readKey,
        Func<YamlNode, TValue> readValue,
        IComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(readKey);
        ArgumentNullException.ThrowIfNull(readValue);

        var result = new SortedDictionary<TKey, TValue>(comparer);
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var entry in mapping.Entries)
                {
                    result.TryAdd(readKey(entry.Key), readValue(entry.Value));
                }

                break;
            case YamlScalarNode or YamlNullNode:
                break;
            default:
                throw TypeError(node, "mapping");
        }

        return result;
    }

    public static (TFirst First, TSecond Second) ReadPair<TFirst, TSecond>(
        YamlNode node,
        Func<YamlNode, TFirst> readFirst,
        Func<YamlNode, TSecond> readSecond)
    {
        ArgumentNullException.ThrowIfNull(readFirst);
        ArgumentNullException.ThrowIfNull(readSecond);
        var items = RequireSequenceLength(node, 2, "pair");
        return (readFirst(items[0]), readSecond(items[1]));
    }

    public static (TFirst First, TSecond Second, TThird Third) ReadTuple<TFirst, TSecond, TThird>(
        YamlNode node,
        Func<YamlNode, TFirst> readFirst,
        Func<YamlNode, TSecond> readSecond,
        Func<YamlNode, TThird> readThird)
    {
        ArgumentNullException.ThrowIfNull(readFirst);
        ArgumentNullException.ThrowIfNull(readSecond);
        ArgumentNullException.ThrowIfNull(readThird);
        var items = RequireSequenceLength(node, 3, "tuple");
        return (readFirst(items[0]), readSecond(items[1]), readThird(items[2]));
    }

    public static T[] ReadFixedArray<T>(
        YamlNode node,
        int length,
        Func<YamlNode, T> readItem)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(readItem);
        var items = RequireSequenceLength(node, length, "fixed-length array");
        return items.Select(readItem).ToArray();
    }

    public static bool TryReadInt8(YamlNode node, out sbyte value)
    {
        var success = TryReadSigned(node, 8, out var parsed);
        value = unchecked((sbyte)parsed);
        return success;
    }

    public static bool TryReadUInt8(YamlNode node, out byte value)
    {
        var success = TryReadUnsigned(node, 8, out var parsed);
        value = unchecked((byte)parsed);
        return success;
    }

    public static bool TryReadInt16(YamlNode node, out short value)
    {
        var success = TryReadSigned(node, 16, out var parsed);
        value = unchecked((short)parsed);
        return success;
    }

    public static bool TryReadUInt16(YamlNode node, out ushort value)
    {
        var success = TryReadUnsigned(node, 16, out var parsed);
        value = unchecked((ushort)parsed);
        return success;
    }

    public static bool TryReadInt32(YamlNode node, out int value)
    {
        var success = TryReadSigned(node, 32, out var parsed);
        value = unchecked((int)parsed);
        return success;
    }

    public static bool TryReadUInt32(YamlNode node, out uint value)
    {
        var success = TryReadUnsigned(node, 32, out var parsed);
        value = unchecked((uint)parsed);
        return success;
    }

    public static bool TryReadInt64(YamlNode node, out long value) =>
        TryReadSigned(node, 64, out value);

    public static bool TryReadUInt64(YamlNode node, out ulong value) =>
        TryReadUnsigned(node, 64, out value);

    public static bool TryReadBoolean(YamlNode node, out bool value)
    {
        var text = ScalarText(node);
        switch (text)
        {
            case "true":
            case "True":
            case "TRUE":
                value = true;
                return true;
            case "false":
            case "False":
            case "FALSE":
                value = false;
                return true;
        }

        if (TryParseInteger(text, signed: true, bitWidth: 32, out var raw))
        {
            value = unchecked((int)raw) != 0;
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryReadEnum<TEnum>(YamlNode node, out TEnum value)
        where TEnum : struct, Enum
    {
        if (TryReadInt32(node, out var parsed))
        {
            value = (TEnum)Enum.ToObject(typeof(TEnum), parsed);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryReadSingle(YamlNode node, out float value)
    {
        var text = ScalarText(node);
        if (TryReadSpecialFloating(text, out value))
        {
            return true;
        }

        if (IsHexadecimalFloating(text))
        {
            return TryReadHexadecimalSingle(text!, out value);
        }

        if (string.IsNullOrEmpty(text))
        {
            value = default;
            return false;
        }

        for (var length = text.Length; length > 0; length--)
        {
            if (float.TryParse(text.AsSpan(0, length), FloatingStyles, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool TryReadDouble(YamlNode node, out double value)
    {
        var text = ScalarText(node);
        if (TryReadSpecialFloating(text, out value))
        {
            return true;
        }

        if (IsHexadecimalFloating(text))
        {
            return TryReadHexadecimalDouble(text!, out value);
        }

        if (string.IsNullOrEmpty(text))
        {
            value = default;
            return false;
        }

        for (var length = text.Length; length > 0; length--)
        {
            if (double.TryParse(text.AsSpan(0, length), FloatingStyles, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool IsExplicitNull(YamlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node is YamlNullNode;
    }

    private static bool TryReadSigned(YamlNode node, int bitWidth, out long value)
    {
        var success = TryParseInteger(ScalarText(node), signed: true, bitWidth, out var raw);
        value = SignExtend(raw, bitWidth);
        return success;
    }

    private static bool TryReadUnsigned(YamlNode node, int bitWidth, out ulong value) =>
        TryParseInteger(ScalarText(node), signed: false, bitWidth, out value);

    private static bool TryParseInteger(
        string? text,
        bool signed,
        int bitWidth,
        out ulong value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var position = 0;
        if (text[0] == '+')
        {
            position++;
        }

        var negative = false;
        if (position < text.Length && text[position] == '-')
        {
            if (!signed)
            {
                return false;
            }

            negative = true;
            position++;
        }

        if (position == text.Length)
        {
            return false;
        }

        var numberBase = 10;
        if (position + 1 < text.Length && text[position] == '0')
        {
            numberBase = text[position + 1] switch
            {
                'b' or 'B' => 2,
                'o' or 'O' => 8,
                'x' or 'X' => 16,
                _ => 10,
            };
            if (numberBase != 10)
            {
                position += 2;
            }
        }

        if (position == text.Length)
        {
            return false;
        }

        var mask = bitWidth == 64 ? ulong.MaxValue : (1UL << bitWidth) - 1;
        for (; position < text.Length; position++)
        {
            var digit = DigitValue(text[position]);
            if (digit < 0 || digit >= numberBase)
            {
                value = 0;
                return false;
            }

            value = unchecked((value * (uint)numberBase) + (uint)digit) & mask;
        }

        if (negative)
        {
            value = unchecked(0UL - value) & mask;
        }

        return true;
    }

    private static int DigitValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };

    private static long SignExtend(ulong value, int bitWidth)
    {
        if (bitWidth == 64)
        {
            return unchecked((long)value);
        }

        var signBit = 1UL << (bitWidth - 1);
        var mask = (1UL << bitWidth) - 1;
        return unchecked((long)((value & signBit) == 0 ? value : value | ~mask));
    }

    private static bool TryReadSpecialFloating(string? text, out float value)
    {
        value = text switch
        {
            ".nan" or ".NaN" or ".NAN" => float.NaN,
            ".inf" or ".Inf" or ".INF" => float.PositiveInfinity,
            "-.inf" or "-.Inf" or "-.INF" => float.NegativeInfinity,
            _ => default,
        };
        return text is ".nan" or ".NaN" or ".NAN" or
            ".inf" or ".Inf" or ".INF" or
            "-.inf" or "-.Inf" or "-.INF";
    }

    private static bool TryReadSpecialFloating(string? text, out double value)
    {
        value = text switch
        {
            ".nan" or ".NaN" or ".NAN" => double.NaN,
            ".inf" or ".Inf" or ".INF" => double.PositiveInfinity,
            "-.inf" or "-.Inf" or "-.INF" => double.NegativeInfinity,
            _ => default,
        };
        return text is ".nan" or ".NaN" or ".NAN" or
            ".inf" or ".Inf" or ".INF" or
            "-.inf" or "-.Inf" or "-.INF";
    }

    private static bool IsHexadecimalFloating(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var offset = text[0] is '+' or '-' ? 1 : 0;
        return text.Length >= offset + 2 &&
            text[offset] == '0' &&
            text[offset + 1] is 'x' or 'X';
    }

    private static bool TryReadHexadecimalSingle(string text, out float value)
    {
        var negative = text[0] == '-';
        var position = text[0] is '+' or '-' ? 3 : 2;
        value = 0;
        if (!TryReadHexadecimalSignificand(text, ref position, ref value, out var hasExponent))
        {
            return false;
        }

        if (!TryReadHexadecimalExponent(text, position, hasExponent, out var exponent))
        {
            value = default;
            return false;
        }

        value *= IntegerPower(16F, exponent);
        if (negative)
        {
            value = -value;
        }

        return true;
    }

    private static bool TryReadHexadecimalDouble(string text, out double value)
    {
        var negative = text[0] == '-';
        var position = text[0] is '+' or '-' ? 3 : 2;
        value = 0;
        if (!TryReadHexadecimalSignificand(text, ref position, ref value, out var hasExponent))
        {
            return false;
        }

        if (!TryReadHexadecimalExponent(text, position, hasExponent, out var exponent))
        {
            value = default;
            return false;
        }

        value *= IntegerPower(16D, exponent);
        if (negative)
        {
            value = -value;
        }

        return true;
    }

    private static bool TryReadHexadecimalSignificand(
        string text,
        ref int position,
        ref float value,
        out bool hasExponent)
    {
        hasExponent = false;
        while (position < text.Length)
        {
            var character = text[position++];
            if (TryHexadecimalFloatingDigit(character, out var digit))
            {
                value = (value * 16F) + digit;
            }
            else if (character == '.')
            {
                var place = 0.0625F;
                while (position < text.Length && text[position] is not ('p' or 'P'))
                {
                    if (!TryHexadecimalFloatingDigit(text[position++], out digit))
                    {
                        return false;
                    }

                    value += place * digit;
                    place /= 16F;
                }

                if (position < text.Length)
                {
                    position++;
                    hasExponent = true;
                }

                break;
            }
            else if (character is 'p' or 'P')
            {
                hasExponent = true;
                return true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadHexadecimalSignificand(
        string text,
        ref int position,
        ref double value,
        out bool hasExponent)
    {
        hasExponent = false;
        while (position < text.Length)
        {
            var character = text[position++];
            if (TryHexadecimalFloatingDigit(character, out var digit))
            {
                value = (value * 16D) + digit;
            }
            else if (character == '.')
            {
                var place = 0.0625D;
                while (position < text.Length && text[position] is not ('p' or 'P'))
                {
                    if (!TryHexadecimalFloatingDigit(text[position++], out digit))
                    {
                        return false;
                    }

                    value += place * digit;
                    place /= 16D;
                }

                if (position < text.Length)
                {
                    position++;
                    hasExponent = true;
                }

                break;
            }
            else if (character is 'p' or 'P')
            {
                hasExponent = true;
                return true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadHexadecimalExponent(
        string text,
        int position,
        bool hasExponent,
        out short exponent)
    {
        if (!hasExponent)
        {
            exponent = 0;
            return position == text.Length;
        }

        if (position < text.Length && text[position] == '+')
        {
            position++;
        }

        if (position >= text.Length ||
            !TryParseInteger(text[position..], signed: true, bitWidth: 16, out var parsed))
        {
            exponent = default;
            return false;
        }

        exponent = unchecked((short)parsed);
        return true;
    }

    private static bool TryHexadecimalFloatingDigit(char character, out int digit)
    {
        digit = character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a',
            >= 'A' and <= 'F' => character - 'A',
            _ => -1,
        };
        return digit >= 0;
    }

    private static float IntegerPower(float value, short exponent)
    {
        var result = 1F;
        if (exponent >= 0)
        {
            for (var index = 0; index < exponent; index++)
            {
                result *= value;
            }
        }
        else
        {
            var count = unchecked((short)-exponent);
            for (var index = 0; index < count; index++)
            {
                result /= value;
            }
        }

        return result;
    }

    private static double IntegerPower(double value, short exponent)
    {
        var result = 1D;
        if (exponent >= 0)
        {
            for (var index = 0; index < exponent; index++)
            {
                result *= value;
            }
        }
        else
        {
            var count = unchecked((short)-exponent);
            for (var index = 0; index < count; index++)
            {
                result /= value;
            }
        }

        return result;
    }

    private static bool TryGetBase64Length(string text, out int decodedLength)
    {
        decodedLength = 0;
        if ((text.Length & 3) != 0)
        {
            return false;
        }

        var padding = 0;
        if (text.Length > 0 && text[^1] == '=')
        {
            padding++;
        }
        if (text.Length > 1 && text[^2] == '=')
        {
            padding++;
        }

        var contentLength = text.Length - padding;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (index >= contentLength)
            {
                if (character != '=')
                {
                    return false;
                }
            }
            else if (!(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/'))
            {
                return false;
            }
        }

        decodedLength = checked((text.Length / 4 * 3) - padding);
        return true;
    }

    private static string? ScalarText(YamlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlNullNode nullNode => nullNode.Spelling,
            _ => null,
        };
    }

    private static IReadOnlyList<YamlNode> RequireSequenceLength(
        YamlNode node,
        int length,
        string targetType)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is not YamlSequenceNode sequence || sequence.Items.Count != length)
        {
            throw TypeError(node, targetType);
        }

        return sequence.Items;
    }

    private static YamlFormatException TypeError(YamlNode node, string targetType) =>
        new($"Could not deserialize YAML value to {targetType}.", node.Span);
}
