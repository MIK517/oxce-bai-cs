namespace Oxce.Extensions.Abstractions;

public readonly record struct ExtensionApiVersion : IComparable<ExtensionApiVersion>
{
    public ExtensionApiVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        Major = major;
        Minor = minor;
    }

    public int Major { get; }
    public int Minor { get; }

    public int CompareTo(ExtensionApiVersion other)
    {
        var major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public static bool operator <(ExtensionApiVersion left, ExtensionApiVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(ExtensionApiVersion left, ExtensionApiVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(ExtensionApiVersion left, ExtensionApiVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(ExtensionApiVersion left, ExtensionApiVersion right) =>
        left.CompareTo(right) >= 0;

    public static ExtensionApiVersion Parse(string value)
    {
        if (!TryParse(value, out var result))
            throw new FormatException($"'{value}' is not a valid major.minor extension API version.");
        return result;
    }

    public static bool TryParse(string? value, out ExtensionApiVersion result)
    {
        result = default;
        if (value is null) return false;
        var parts = value.Split('.');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var minor)) return false;
        result = new ExtensionApiVersion(major, minor);
        return true;
    }

    public override string ToString() =>
        $"{Major.ToString(System.Globalization.CultureInfo.InvariantCulture)}.{Minor.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

public readonly record struct ExtensionApiRange(
    ExtensionApiVersion Minimum,
    ExtensionApiVersion MaximumExclusive)
{
    public bool Contains(ExtensionApiVersion version) =>
        Minimum <= version && version < MaximumExclusive;

    public void Validate()
    {
        if (MaximumExclusive <= Minimum)
            throw new ArgumentException("The maximum API version must be greater than the minimum API version.");
    }
}

public static class ManagedExtensionApi
{
    public static ExtensionApiVersion Current { get; } = new(0, 1);
}

public sealed class ExtensionIdentity
{
    public ExtensionIdentity(string id, Version version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(version);
        Id = id;
        Version = version;
    }

    public string Id { get; }
    public Version Version { get; }
}

public enum ExtensionDiagnosticSeverity
{
    Trace,
    Information,
    Warning,
    Error,
    Critical,
}

public sealed record ExtensionDiagnostic(
    string Code,
    ExtensionDiagnosticSeverity Severity,
    string Message);

public interface IExtensionDiagnosticSink
{
    void Report(ExtensionDiagnostic diagnostic);
}

public interface IExtensionContext
{
    ExtensionApiVersion ApiVersion { get; }
    ExtensionIdentity Identity { get; }
    IExtensionDiagnosticSink Diagnostics { get; }
}

public interface IManagedExtension
{
    void Initialize(IExtensionContext context, CancellationToken cancellationToken);
    void Shutdown(CancellationToken cancellationToken);
}
