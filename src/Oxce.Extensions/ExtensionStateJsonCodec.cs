using System.Text;
using System.Text.Json;
using Oxce.Extensions.Abstractions;

namespace Oxce.Extensions;

public sealed class ExtensionStateLimits
{
    public int MaximumRecords { get; init; } = 256;
    public int MaximumDepth { get; init; } = 32;
    public int MaximumNodes { get; init; } = 100_000;
    public int MaximumCollectionItems { get; init; } = 10_000;
    public int MaximumStringLength { get; init; } = 256 * 1024;
    public int MaximumEncodedBytes { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCollectionItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumStringLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEncodedBytes);
    }
}

public static class ExtensionStateValidator
{
    public static void Validate(
        ExtensionStateRecord record,
        ExtensionStateLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        limits ??= new ExtensionStateLimits();
        limits.Validate();
        ValidateMetadata(record);
        var nodes = 0;
        ValidateValue(record.Data, 1, ref nodes, limits);
    }

    public static void Validate(
        ExtensionStateDocument document,
        ExtensionStateLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        limits ??= new ExtensionStateLimits();
        limits.Validate();
        if (document.Records.Count > limits.MaximumRecords)
            throw new InvalidDataException(
                $"Extension state has {document.Records.Count} records; the limit is {limits.MaximumRecords}.");
        var nodes = 0;
        foreach (var record in document.Records)
        {
            ValidateMetadata(record);
            ValidateValue(record.Data, 1, ref nodes, limits);
        }
    }

    private static void ValidateMetadata(ExtensionStateRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ExtensionId);
        ArgumentNullException.ThrowIfNull(record.ExtensionVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(record.SchemaVersion);
        ArgumentNullException.ThrowIfNull(record.Data);
    }

    private static void ValidateValue(
        ExtensionStateValue value,
        int depth,
        ref int nodes,
        ExtensionStateLimits limits)
    {
        if (depth > limits.MaximumDepth)
            throw new InvalidDataException($"Extension state exceeds the depth limit of {limits.MaximumDepth}.");
        nodes = checked(nodes + 1);
        if (nodes > limits.MaximumNodes)
            throw new InvalidDataException($"Extension state exceeds the node limit of {limits.MaximumNodes}.");
        switch (value.Kind)
        {
            case ExtensionStateValueKind.Null:
                Require(value.Scalar is null && value.Items is null && value.Properties is null, value.Kind);
                break;
            case ExtensionStateValueKind.Boolean:
                Require(value.Scalar is bool && value.Items is null && value.Properties is null, value.Kind);
                break;
            case ExtensionStateValueKind.WholeNumber:
                Require(value.Scalar is long && value.Items is null && value.Properties is null, value.Kind);
                break;
            case ExtensionStateValueKind.Number:
                Require(value.Scalar is double number && double.IsFinite(number) &&
                    value.Items is null && value.Properties is null, value.Kind);
                break;
            case ExtensionStateValueKind.Text:
                Require(value.Scalar is string && value.Items is null && value.Properties is null, value.Kind);
                if (((string)value.Scalar!).Length > limits.MaximumStringLength)
                    throw new InvalidDataException("Extension-state string exceeds its length limit.");
                break;
            case ExtensionStateValueKind.List:
                Require(value.Scalar is null && value.Items is not null && value.Properties is null, value.Kind);
                if (value.Items!.Count > limits.MaximumCollectionItems)
                    throw new InvalidDataException("Extension-state list exceeds its item limit.");
                foreach (var item in value.Items) ValidateValue(item, depth + 1, ref nodes, limits);
                break;
            case ExtensionStateValueKind.Map:
                Require(value.Scalar is null && value.Items is null && value.Properties is not null, value.Kind);
                if (value.Properties!.Count > limits.MaximumCollectionItems)
                    throw new InvalidDataException("Extension-state map exceeds its property limit.");
                foreach (var pair in value.Properties)
                {
                    if (pair.Key.Length > limits.MaximumStringLength)
                        throw new InvalidDataException("Extension-state property name exceeds its length limit.");
                    ValidateValue(pair.Value, depth + 1, ref nodes, limits);
                }
                break;
            default:
                throw new InvalidDataException($"Unknown extension-state value kind '{value.Kind}'.");
        }
    }

    private static void Require(bool condition, ExtensionStateValueKind kind)
    {
        if (!condition) throw new InvalidDataException($"Extension-state value '{kind}' has inconsistent storage.");
    }
}

public static class ExtensionStateJsonCodec
{
    public static byte[] Write(
        ExtensionStateDocument document,
        ExtensionStateLimits? limits = null)
    {
        limits ??= new ExtensionStateLimits();
        ExtensionStateValidator.Validate(document, limits);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WritePropertyName("extensions");
            writer.WriteStartArray();
            foreach (var record in document.Records)
            {
                writer.WriteStartObject();
                writer.WriteString("id", record.ExtensionId);
                writer.WriteString("extensionVersion", record.ExtensionVersion.ToString());
                writer.WriteNumber("stateSchemaVersion", record.SchemaVersion);
                writer.WriteBoolean("requiredForContinuation", record.RequiredForContinuation);
                writer.WritePropertyName("data");
                WriteValue(writer, record.Data);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        if (stream.Length > limits.MaximumEncodedBytes)
            throw new InvalidDataException(
                $"Encoded extension state is {stream.Length} bytes; the limit is {limits.MaximumEncodedBytes}.");
        return stream.ToArray();
    }

    public static ExtensionStateDocument Read(
        ReadOnlySpan<byte> utf8Json,
        ExtensionStateLimits? limits = null)
    {
        limits ??= new ExtensionStateLimits();
        limits.Validate();
        if (utf8Json.Length > limits.MaximumEncodedBytes)
            throw new InvalidDataException(
                $"Encoded extension state is {utf8Json.Length} bytes; the limit is {limits.MaximumEncodedBytes}.");
        using var json = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = limits.MaximumDepth + 8,
        });
        var root = json.RootElement;
        RequireObject(root, "extension-state document");
        EnsureProperties(root, "schemaVersion", "extensions");
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported extension-state document schema.");
        var extensions = root.GetProperty("extensions");
        if (extensions.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Extension-state 'extensions' must be an array.");
        if (extensions.GetArrayLength() > limits.MaximumRecords)
            throw new InvalidDataException("Extension-state record limit was exceeded.");
        var records = new List<ExtensionStateRecord>();
        foreach (var element in extensions.EnumerateArray())
        {
            RequireObject(element, "extension-state record");
            EnsureProperties(element, "id", "extensionVersion", "stateSchemaVersion",
                "requiredForContinuation", "data");
            var id = RequiredString(element, "id");
            var versionText = RequiredString(element, "extensionVersion");
            if (!Version.TryParse(versionText, out var version))
                throw new InvalidDataException($"Extension-state version '{versionText}' is invalid.");
            var record = new ExtensionStateRecord(
                id,
                version,
                element.GetProperty("stateSchemaVersion").GetInt32(),
                element.GetProperty("requiredForContinuation").GetBoolean(),
                ReadValue(element.GetProperty("data"), 1, limits));
            ExtensionStateValidator.Validate(record, limits);
            records.Add(record);
        }
        var document = new ExtensionStateDocument(records);
        ExtensionStateValidator.Validate(document, limits);
        return document;
    }

    private static void WriteValue(Utf8JsonWriter writer, ExtensionStateValue value)
    {
        switch (value.Kind)
        {
            case ExtensionStateValueKind.Null:
                writer.WriteNullValue();
                break;
            case ExtensionStateValueKind.Boolean:
                writer.WriteBooleanValue((bool)value.Scalar!);
                break;
            case ExtensionStateValueKind.WholeNumber:
                writer.WriteNumberValue((long)value.Scalar!);
                break;
            case ExtensionStateValueKind.Number:
                writer.WriteNumberValue((double)value.Scalar!);
                break;
            case ExtensionStateValueKind.Text:
                writer.WriteStringValue((string)value.Scalar!);
                break;
            case ExtensionStateValueKind.List:
                writer.WriteStartArray();
                foreach (var item in value.Items!) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            case ExtensionStateValueKind.Map:
                writer.WriteStartObject();
                foreach (var pair in value.Properties!)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteValue(writer, pair.Value);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static ExtensionStateValue ReadValue(
        JsonElement element,
        int depth,
        ExtensionStateLimits limits)
    {
        if (depth > limits.MaximumDepth)
            throw new InvalidDataException("Extension-state depth limit was exceeded.");
        return element.ValueKind switch
        {
            JsonValueKind.Null => ExtensionStateValue.Null,
            JsonValueKind.True => ExtensionStateValue.Boolean(true),
            JsonValueKind.False => ExtensionStateValue.Boolean(false),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => ExtensionStateValue.WholeNumber(integer),
            JsonValueKind.Number => ExtensionStateValue.Number(element.GetDouble()),
            JsonValueKind.String => ExtensionStateValue.Text(RequiredStringValue(element)),
            JsonValueKind.Array => ReadList(element, depth, limits),
            JsonValueKind.Object => ReadMap(element, depth, limits),
            _ => throw new InvalidDataException($"Unsupported extension-state JSON kind '{element.ValueKind}'."),
        };
    }

    private static ExtensionStateValue ReadList(JsonElement element, int depth, ExtensionStateLimits limits)
    {
        if (element.GetArrayLength() > limits.MaximumCollectionItems)
            throw new InvalidDataException("Extension-state list limit was exceeded.");
        return ExtensionStateValue.List(element.EnumerateArray().Select(item => ReadValue(item, depth + 1, limits)));
    }

    private static ExtensionStateValue ReadMap(JsonElement element, int depth, ExtensionStateLimits limits)
    {
        var properties = element.EnumerateObject().ToArray();
        if (properties.Length > limits.MaximumCollectionItems)
            throw new InvalidDataException("Extension-state map limit was exceeded.");
        return ExtensionStateValue.Map(properties.Select(property =>
            new KeyValuePair<string, ExtensionStateValue>(
                property.Name, ReadValue(property.Value, depth + 1, limits))));
    }

    private static string RequiredString(JsonElement element, string name) =>
        RequiredStringValue(element.GetProperty(name));

    private static string RequiredStringValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Expected an extension-state string.");
        return element.GetString() ?? throw new InvalidDataException("Extension-state strings cannot be null.");
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The {name} must be an object.");
    }

    private static void EnsureProperties(JsonElement element, params string[] expected)
    {
        var allowed = expected.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Remove(property.Name))
                throw new InvalidDataException($"Unknown extension-state property '{property.Name}'.");
        if (allowed.Count != 0)
            throw new InvalidDataException(
                $"Missing extension-state property/properties: {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
    }
}
