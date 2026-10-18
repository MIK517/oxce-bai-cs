namespace Oxce.Formats.Yaml;

/// <summary>Keyed reads of a mapping entry with <see cref="YamlValueReader"/>'s conversions; a missing key gives the default.</summary>
public static class YamlMappingReadExtensions
{
    public static string ReadString(this YamlMappingNode mapping, string key, string defaultValue) =>
        YamlValueReader.ReadString(mapping, key, defaultValue);

    public static int ReadInt32(this YamlMappingNode mapping, string key, int defaultValue) =>
        YamlValueReader.ReadInt32(mapping, key, defaultValue);

    public static long ReadInt64(this YamlMappingNode mapping, string key, long defaultValue) =>
        YamlValueReader.ReadInt64(mapping, key, defaultValue);

    public static ulong ReadUInt64(this YamlMappingNode mapping, string key, ulong defaultValue) =>
        YamlValueReader.ReadUInt64(mapping, key, defaultValue);

    public static double ReadDouble(this YamlMappingNode mapping, string key, double defaultValue) =>
        YamlValueReader.ReadDouble(mapping, key, defaultValue);

    public static bool ReadBoolean(this YamlMappingNode mapping, string key, bool defaultValue) =>
        YamlValueReader.ReadBoolean(mapping, key, defaultValue);
}
