using System.Text;
using System.Text.Json;
using Oxce.Core.Diagnostics;

namespace Oxce.Formats.Yaml;

public static class YamlSemanticNormalizer
{
    public static byte[] NormalizeToUtf8Json(
        YamlDocumentSet documents,
        YamlNormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        options ??= new YamlNormalizationOptions();
        options.Validate();

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("documents");
            foreach (var document in documents.Documents)
            {
                WriteNode(writer, document.Root, 1, options);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            EnsureOutputLimit(writer, documents.SourceName, options.MaxOutputBytes);
        }

        if (output.Length >= options.MaxOutputBytes)
        {
            throw OutputLimitError(documents.SourceName, options.MaxOutputBytes);
        }

        output.WriteByte((byte)'\n');
        return output.ToArray();
    }

    public static string NormalizeToJson(
        YamlDocumentSet documents,
        YamlNormalizationOptions? options = null) =>
        Encoding.UTF8.GetString(NormalizeToUtf8Json(documents, options));

    private static void WriteNode(
        Utf8JsonWriter writer,
        YamlNode node,
        int depth,
        YamlNormalizationOptions options)
    {
        if (depth > options.MaxDepth)
        {
            throw new YamlFormatException(
                $"YAML semantic normalization exceeds the {options.MaxDepth}-level limit.",
                node.Span);
        }

        writer.WriteStartObject();
        writer.WriteString("kind", KindText(node.Kind));
        if (node.Tag is not null)
        {
            writer.WriteString("tag", node.Tag);
        }

        switch (node)
        {
            case YamlNullNode nullNode:
                writer.WriteString("value", nullNode.Spelling);
                break;
            case YamlScalarNode scalar:
                writer.WriteString("value", scalar.Value);
                break;
            case YamlSequenceNode sequence:
                writer.WriteStartArray("items");
                foreach (var item in sequence.Items)
                {
                    WriteNode(writer, item, depth + 1, options);
                }

                writer.WriteEndArray();
                break;
            case YamlMappingNode mapping:
                writer.WriteStartArray("entries");
                foreach (var entry in mapping.Entries)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("key");
                    WriteNode(writer, entry.Key, depth + 1, options);
                    writer.WritePropertyName("value");
                    WriteNode(writer, entry.Value, depth + 1, options);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException($"Unsupported YAML node type {node.GetType().Name}.");
        }

        writer.WriteEndObject();
        EnsureOutputLimit(writer, node.Span.SourceName, options.MaxOutputBytes);
    }

    private static string KindText(YamlNodeKind kind) => kind switch
    {
        YamlNodeKind.Null => "null",
        YamlNodeKind.Scalar => "scalar",
        YamlNodeKind.Sequence => "sequence",
        YamlNodeKind.Mapping => "mapping",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown YAML node kind."),
    };

    private static void EnsureOutputLimit(Utf8JsonWriter writer, string sourceName, int maxOutputBytes)
    {
        if (writer.BytesCommitted + writer.BytesPending > maxOutputBytes)
        {
            throw OutputLimitError(sourceName, maxOutputBytes);
        }
    }

    private static YamlFormatException OutputLimitError(string sourceName, int maxOutputBytes)
    {
        var position = new SourcePosition(1, 1, 0);
        return new YamlFormatException(
            $"YAML semantic normalization exceeds the {maxOutputBytes}-byte output limit.",
            new SourceSpan(sourceName, position, position));
    }
}
