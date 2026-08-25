using System.Text;
using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets;

public static class RulesetCatalogNormalizer
{
    public const int SchemaVersion = 1;

    public static byte[] NormalizeToUtf8Json(
        UnresolvedRuleCatalog catalog,
        RulesetCatalogNormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        options ??= new RulesetCatalogNormalizationOptions();
        options.Validate();

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("stage", "composed");
            writer.WriteStartArray("sections");
            foreach (var section in catalog.Sections)
            {
                writer.WriteStartObject();
                writer.WriteString("name", section.Definition.Name);
                writer.WriteString("identityKey", section.Definition.IdentityKey);
                writer.WriteStartArray("rules");
                foreach (var rule in section.Rules)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", rule.Id);
                    writer.WritePropertyName("creationSource");
                    WriteSource(writer, rule.CreationSource, options);
                    writer.WritePropertyName("lastUpdateSource");
                    WriteSource(writer, rule.LastUpdateSource, options);
                    writer.WriteStartArray("operations");
                    foreach (var operation in rule.Operations)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("kind", KindText(operation.Kind));
                        writer.WritePropertyName("source");
                        WriteSource(writer, operation.Source, options);
                        writer.WritePropertyName("node");
                        WriteNode(writer, operation.Node, 1, options);
                        writer.WriteEndObject();
                        EnsureOutputLimit(writer, operation.Node.Span, options.MaximumOutputBytes);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        if (output.Length >= options.MaximumOutputBytes)
        {
            throw OutputLimitError(UnknownSpan("ruleset-catalog"), options.MaximumOutputBytes);
        }

        output.WriteByte((byte)'\n');
        return output.ToArray();
    }

    public static string NormalizeToJson(
        UnresolvedRuleCatalog catalog,
        RulesetCatalogNormalizationOptions? options = null) =>
        Encoding.UTF8.GetString(NormalizeToUtf8Json(catalog, options));

    private static void WriteSource(
        Utf8JsonWriter writer,
        RuleOperationSource source,
        RulesetCatalogNormalizationOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("layer", source.LayerId);
        writer.WriteString("mod", source.ModId);
        writer.WriteString("path", options.NormalizeSourceName?.Invoke(source.SourcePath) ?? source.SourcePath);
        writer.WriteNumber("line", source.Span.Start.Line);
        writer.WriteNumber("column", source.Span.Start.Column);
        writer.WriteEndObject();
    }

    private static void WriteNode(
        Utf8JsonWriter writer,
        YamlNode node,
        int depth,
        RulesetCatalogNormalizationOptions options)
    {
        if (depth > options.MaximumDepth)
        {
            throw new YamlFormatException(
                $"Ruleset catalog normalization exceeds the {options.MaximumDepth}-level limit.",
                node.Span);
        }

        writer.WriteStartObject();
        writer.WriteString("kind", node.Kind switch
        {
            YamlNodeKind.Null => "null",
            YamlNodeKind.Scalar => "scalar",
            YamlNodeKind.Sequence => "sequence",
            YamlNodeKind.Mapping => "mapping",
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        });
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
        EnsureOutputLimit(writer, node.Span, options.MaximumOutputBytes);
    }

    private static string KindText(RuleOperationKind kind) => kind switch
    {
        RuleOperationKind.Default => "default",
        RuleOperationKind.New => "new",
        RuleOperationKind.Override => "override",
        RuleOperationKind.Update => "update",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void EnsureOutputLimit(Utf8JsonWriter writer, SourceSpan span, int maximumOutputBytes)
    {
        if (writer.BytesCommitted + writer.BytesPending > maximumOutputBytes)
        {
            throw OutputLimitError(span, maximumOutputBytes);
        }
    }

    private static YamlFormatException OutputLimitError(SourceSpan span, int maximumOutputBytes) =>
        new($"Ruleset catalog normalization exceeds the {maximumOutputBytes}-byte output limit.", span);

    private static SourceSpan UnknownSpan(string sourceName)
    {
        var position = new SourcePosition(1, 1, 0);
        return new SourceSpan(sourceName, position, position);
    }
}
