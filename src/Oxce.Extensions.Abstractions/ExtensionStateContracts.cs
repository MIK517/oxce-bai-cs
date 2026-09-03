using System.Collections.ObjectModel;

namespace Oxce.Extensions.Abstractions;

public enum ExtensionStateValueKind
{
    Null,
    Boolean,
    WholeNumber,
    Number,
    Text,
    List,
    Map,
}

public sealed class ExtensionStateValue
{
    private ExtensionStateValue(
        ExtensionStateValueKind kind,
        object? scalar,
        IReadOnlyList<ExtensionStateValue>? items,
        IReadOnlyDictionary<string, ExtensionStateValue>? properties)
    {
        Kind = kind;
        Scalar = scalar;
        Items = items;
        Properties = properties;
    }

    public ExtensionStateValueKind Kind { get; }
    public object? Scalar { get; }
    public IReadOnlyList<ExtensionStateValue>? Items { get; }
    public IReadOnlyDictionary<string, ExtensionStateValue>? Properties { get; }

    public static ExtensionStateValue Null { get; } = new(ExtensionStateValueKind.Null, null, null, null);
    public static ExtensionStateValue Boolean(bool value) => new(ExtensionStateValueKind.Boolean, value, null, null);
    public static ExtensionStateValue WholeNumber(long value) => new(ExtensionStateValueKind.WholeNumber, value, null, null);

    public static ExtensionStateValue Number(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return new ExtensionStateValue(ExtensionStateValueKind.Number, value, null, null);
    }

    public static ExtensionStateValue Text(string value) =>
        new(ExtensionStateValueKind.Text, value ?? throw new ArgumentNullException(nameof(value)), null, null);

    public static ExtensionStateValue List(IEnumerable<ExtensionStateValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ExtensionStateValue(
            ExtensionStateValueKind.List,
            null,
            Array.AsReadOnly(values.Select(value => value ?? throw new ArgumentException("State lists cannot contain null references.", nameof(values))).ToArray()),
            null);
    }

    public static ExtensionStateValue Map(IEnumerable<KeyValuePair<string, ExtensionStateValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var map = new SortedDictionary<string, ExtensionStateValue>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!map.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException($"Duplicate extension-state property '{pair.Key}'.", nameof(values));
        }
        return new ExtensionStateValue(
            ExtensionStateValueKind.Map,
            null,
            null,
            new ReadOnlyDictionary<string, ExtensionStateValue>(map));
    }
}

public sealed record ExtensionStateSnapshot(
    int SchemaVersion,
    bool RequiredForContinuation,
    ExtensionStateValue Data);

public sealed record ExtensionStateRecord(
    string ExtensionId,
    Version ExtensionVersion,
    int SchemaVersion,
    bool RequiredForContinuation,
    ExtensionStateValue Data);

public sealed class ExtensionStateDocument
{
    public ExtensionStateDocument(IEnumerable<ExtensionStateRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var result = records.OrderBy(static record => record.ExtensionId, StringComparer.Ordinal).ToArray();
        if (result.Select(static record => record.ExtensionId).Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new ArgumentException("Extension-state records must have unique extension IDs.", nameof(records));
        Records = Array.AsReadOnly(result);
    }

    public IReadOnlyList<ExtensionStateRecord> Records { get; }
}

public interface IManagedExtensionState
{
    ExtensionStateSnapshot CaptureState();
    void RestoreState(ExtensionStateSnapshot state, CancellationToken cancellationToken);
}
