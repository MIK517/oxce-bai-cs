using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Oxce.FixtureSupport;

public static class CanonicalJson
{
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 128,
    };

    public static string Normalize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException($"JSON input exceeds the {MaximumDocumentBytes}-byte fixture limit.");
        }

        using var document = JsonDocument.Parse(utf8Json.ToArray(), DocumentOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteElement(writer, document.RootElement);
        }

        var json = Encoding.UTF8.GetString(stream.ToArray())
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Concat(json, "\n");
    }

    public static bool SemanticallyEquals(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);

    public static string NormalizeFile(string path) =>
        Normalize(FixtureFile.ReadAllBytes(path, MaximumDocumentBytes));

    public static bool FilesSemanticallyEqual(string expected, string actual) =>
        SemanticallyEquals(
            FixtureFile.ReadAllBytes(expected, MaximumDocumentBytes),
            FixtureFile.ReadAllBytes(actual, MaximumDocumentBytes));

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject().ToArray();
                EnsureUniquePropertyNames(properties);
                foreach (var property in properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind: {element.ValueKind}.");
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.TryGetInt64(out var signedValue))
        {
            writer.WriteNumberValue(signedValue);
        }
        else if (element.TryGetUInt64(out var unsignedValue))
        {
            writer.WriteNumberValue(unsignedValue);
        }
        else if (element.TryGetDecimal(out var decimalValue))
        {
            writer.WriteRawValue(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteRawValue(element.GetDouble().ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private static void EnsureUniquePropertyNames(JsonProperty[] properties)
    {
        var duplicate = properties
            .GroupBy(static property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());

        if (duplicate is not null)
        {
            throw new JsonException($"Duplicate JSON property '{duplicate.Key}'.");
        }
    }
}
