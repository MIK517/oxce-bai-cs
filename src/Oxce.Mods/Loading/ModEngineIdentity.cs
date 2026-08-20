using Oxce.Mods.Metadata;

namespace Oxce.Mods.Loading;

public sealed class ModEngineIdentity
{
    private readonly int[] _version;

    public ModEngineIdentity(string name, string version)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);
        Name = name;
        Version = version;
        _version = ParseVersion(version);
    }

    public string Name { get; }

    public string Version { get; }

    public bool Supports(ModMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var required = ParseVersion(metadata.RequiredExtendedVersion);
        if (metadata.RequiredExtendedEngine.Length == 0)
        {
            return IsAtLeast(new int[4], required);
        }

        if (!string.Equals(Name, metadata.RequiredExtendedEngine, StringComparison.Ordinal))
        {
            return false;
        }

        return IsAtLeast(_version, required);
    }

    private static bool IsAtLeast(int[] current, int[] required)
    {
        for (var index = 0; index < current.Length; ++index)
        {
            if (current[index] != required[index])
            {
                return current[index] > required[index];
            }
        }

        return true;
    }

    private static int[] ParseVersion(string version)
    {
        var result = new int[4];
        var components = version.Split('.');
        for (var index = 0; index < result.Length && index < components.Length; ++index)
        {
            result[index] = ParseIntegerPrefix(components[index]);
        }

        return result;
    }

    private static int ParseIntegerPrefix(string component)
    {
        var span = component.AsSpan().TrimStart();
        var negative = false;
        if (!span.IsEmpty && span[0] is '+' or '-')
        {
            negative = span[0] == '-';
            span = span[1..];
        }

        var digits = 0;
        long value = 0;
        foreach (var character in span)
        {
            if (character is < '0' or > '9')
            {
                break;
            }

            ++digits;
            var digit = character - '0';
            var limit = negative ? -(long)int.MinValue : int.MaxValue;
            if (value > (limit - digit) / 10)
            {
                return 0;
            }

            value = value * 10 + digit;
        }

        if (digits == 0)
        {
            return 0;
        }

        return negative ? checked((int)-value) : checked((int)value);
    }
}
