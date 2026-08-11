using System.Globalization;

namespace Oxce.Formats.Yaml;

public static class YamlValueReader
{
    public static string ReadString(YamlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node is YamlScalarNode scalar
            ? scalar.Value
            : throw TypeError(node, "string");
    }

    public static string ReadString(YamlMappingNode mapping, string key, string defaultValue) =>
        mapping.TryGet(key, out var node) ? ReadString(node!) : defaultValue;

    public static int ReadInt32(YamlNode node)
    {
        var value = ReadString(node);
        if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw TypeError(node, "Int32");
    }

    public static int ReadInt32(YamlMappingNode mapping, string key, int defaultValue) =>
        mapping.TryGet(key, out var node) ? ReadInt32(node!) : defaultValue;

    public static bool ReadBoolean(YamlNode node)
    {
        var value = ReadString(node);
        if (string.Equals(value, "true", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.Ordinal))
        {
            return false;
        }

        throw TypeError(node, "Boolean");
    }

    public static bool ReadBoolean(YamlMappingNode mapping, string key, bool defaultValue) =>
        mapping.TryGet(key, out var node) ? ReadBoolean(node!) : defaultValue;

    public static bool IsExplicitNull(YamlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node is YamlNullNode;
    }

    private static YamlFormatException TypeError(YamlNode node, string targetType) =>
        new($"Could not deserialize YAML value to {targetType}.", node.Span);
}
