using System.Text;
using Oxce.Core.Diagnostics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using DotNetYamlParser = YamlDotNet.Core.Parser;
using DotNetScalarStyle = YamlDotNet.Core.ScalarStyle;

namespace Oxce.Formats.Yaml;

public static class YamlCompatibilityReader
{
    public static YamlDocumentSet Parse(
        string yaml,
        string sourceName,
        YamlReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        options ??= new YamlReadOptions();
        options.Validate();

        var byteCount = Encoding.UTF8.GetByteCount(yaml);
        if (byteCount > options.MaxBytes)
        {
            throw LimitError(sourceName, $"YAML input exceeds the {options.MaxBytes}-byte limit.");
        }

        try
        {
            using var textReader = new StringReader(yaml);
            var parser = new DotNetYamlParser(textReader);
            return new DomBuilder(parser, sourceName, options).Build();
        }
        catch (YamlFormatException)
        {
            throw;
        }
        catch (YamlException exception)
        {
            var span = CreateSpan(sourceName, exception.Start, exception.End);
            throw new YamlFormatException("Invalid YAML syntax.", span, exception);
        }
    }

    public static YamlDocumentSet ParseFile(
        string path,
        YamlReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new YamlReadOptions();
        options.Validate();

        var fullPath = Path.GetFullPath(path);
        return Parse(ReadUtf8File(fullPath, options.MaxBytes), fullPath, options);
    }

    private static string ReadUtf8File(string path, int maxBytes)
    {
        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        if (input.Length > maxBytes)
        {
            throw LimitError(path, $"YAML input exceeds the {maxBytes}-byte limit.");
        }

        using var output = new MemoryStream(capacity: checked((int)input.Length));
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + read > maxBytes)
            {
                throw LimitError(path, $"YAML input exceeds the {maxBytes}-byte limit.");
            }

            output.Write(buffer, 0, read);
        }

        try
        {
            var yaml = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(output.GetBuffer(), 0, checked((int)output.Length));
            return yaml.Length > 0 && yaml[0] == '\uFEFF' ? yaml[1..] : yaml;
        }
        catch (DecoderFallbackException exception)
        {
            throw new YamlFormatException("YAML input is not valid UTF-8.", UnknownSpan(path), exception);
        }
    }

    private static YamlFormatException LimitError(string sourceName, string message)
    {
        return new YamlFormatException(message, UnknownSpan(sourceName));
    }

    private static SourceSpan UnknownSpan(string sourceName)
    {
        var position = new SourcePosition(1, 1, 0);
        return new SourceSpan(sourceName, position, position);
    }

    private static SourceSpan CreateSpan(string sourceName, Mark start, Mark end) =>
        new(
            sourceName,
            SourcePosition.FromZeroBased(start.Line, start.Column, start.Index),
            SourcePosition.FromZeroBased(end.Line, end.Column, end.Index));

    private sealed class DomBuilder
    {
        private readonly IParser _parser;
        private readonly string _sourceName;
        private readonly YamlReadOptions _options;
        private readonly Dictionary<string, YamlNode> _anchors = new(StringComparer.Ordinal);
        private ParsingEvent? _current;
        private int _nodeCount;
        private int _aliasCount;

        public DomBuilder(IParser parser, string sourceName, YamlReadOptions options)
        {
            _parser = parser;
            _sourceName = sourceName;
            _options = options;
        }

        public YamlDocumentSet Build()
        {
            Advance();
            Take<StreamStart>();
            var documents = new List<YamlDocument>();

            while (_current is not StreamEnd)
            {
                if (documents.Count >= _options.MaxDocuments)
                {
                    throw Error(_current, $"YAML stream exceeds the {_options.MaxDocuments}-document limit.");
                }

                var start = Take<DocumentStart>();
                YamlNode root;
                if (_current is DocumentEnd)
                {
                    root = new YamlNullNode(Span(_current), string.Empty);
                }
                else
                {
                    root = ReadNode(1);
                }

                var end = Take<DocumentEnd>();
                documents.Add(new YamlDocument(root, CreateSpan(_sourceName, start.Start, end.End)));
            }

            Take<StreamEnd>();
            if (_current is not null)
            {
                throw Error(_current, "Unexpected content after the YAML stream.");
            }

            return new YamlDocumentSet(_sourceName, documents);
        }

        private YamlNode ReadNode(int depth)
        {
            if (depth > _options.MaxDepth)
            {
                throw Error(_current, $"YAML nesting exceeds the {_options.MaxDepth}-level limit.");
            }

            _nodeCount = checked(_nodeCount + 1);
            if (_nodeCount > _options.MaxNodes)
            {
                throw Error(_current, $"YAML input exceeds the {_options.MaxNodes}-node limit.");
            }

            return _current switch
            {
                Scalar scalar => ReadScalar(scalar),
                SequenceStart sequence => ReadSequence(sequence, depth),
                MappingStart mapping => ReadMapping(mapping, depth),
                AnchorAlias alias => ReadAlias(alias),
                _ => throw Error(_current, "Expected a YAML value."),
            };
        }

        private YamlNode ReadScalar(Scalar scalar)
        {
            Take<Scalar>();
            var span = CreateSpan(_sourceName, scalar.Start, scalar.End);
            var tag = ValueOrNull(scalar.Tag);
            var anchor = ValueOrNull(scalar.Anchor);
            YamlNode node = scalar.Style == DotNetScalarStyle.Plain && IsNullSpelling(scalar.Value)
                ? new YamlNullNode(span, scalar.Value, tag, anchor)
                : new YamlScalarNode(span, scalar.Value, ConvertStyle(scalar.Style), tag, anchor);
            RegisterAnchor(anchor, node, scalar);
            return node;
        }

        private YamlSequenceNode ReadSequence(SequenceStart sequence, int depth)
        {
            Take<SequenceStart>();
            var items = new List<YamlNode>();
            while (_current is not SequenceEnd)
            {
                items.Add(ReadNode(depth + 1));
            }

            var end = Take<SequenceEnd>();
            var anchor = ValueOrNull(sequence.Anchor);
            var node = new YamlSequenceNode(
                CreateSpan(_sourceName, sequence.Start, end.End),
                items,
                ValueOrNull(sequence.Tag),
                anchor);
            RegisterAnchor(anchor, node, sequence);
            return node;
        }

        private YamlMappingNode ReadMapping(MappingStart mapping, int depth)
        {
            Take<MappingStart>();
            var explicitEntries = new List<YamlMappingEntry>();
            var mergedEntries = new List<YamlMappingEntry>();
            while (_current is not MappingEnd)
            {
                var key = ReadNode(depth + 1);
                var value = ReadNode(depth + 1);
                if (key is YamlScalarNode { Value: "<<", Style: YamlScalarStyle.Plain })
                {
                    AddMergeEntries(mergedEntries, value);
                }
                else
                {
                    explicitEntries.Add(new YamlMappingEntry(key, value));
                }
            }

            var end = Take<MappingEnd>();
            var anchor = ValueOrNull(mapping.Anchor);
            var node = new YamlMappingNode(
                CreateSpan(_sourceName, mapping.Start, end.End),
                CombineMergedAndExplicitEntries(mergedEntries, explicitEntries),
                ValueOrNull(mapping.Tag),
                anchor);
            RegisterAnchor(anchor, node, mapping);
            return node;
        }

        private YamlNode ReadAlias(AnchorAlias alias)
        {
            Take<AnchorAlias>();
            _aliasCount = checked(_aliasCount + 1);
            if (_aliasCount > _options.MaxAliases)
            {
                throw Error(alias, $"YAML input exceeds the {_options.MaxAliases}-alias limit.");
            }

            var name = alias.Value.Value;
            return _anchors.TryGetValue(name, out var node)
                ? node
                : throw Error(alias, $"YAML alias '*{name}' has no preceding anchor.");
        }

        private static void AddMergeEntries(List<YamlMappingEntry> entries, YamlNode value)
        {
            switch (value)
            {
                case YamlMappingNode mapping:
                    entries.AddRange(mapping.Entries);
                    break;
                case YamlSequenceNode sequence:
                    foreach (var item in sequence.Items)
                    {
                        if (item is not YamlMappingNode itemMapping)
                        {
                            throw Error(item, "YAML merge sequence entries must be mappings.");
                        }

                        entries.AddRange(itemMapping.Entries);
                    }

                    break;
                default:
                    throw Error(value, "YAML merge value must be a mapping or sequence of mappings.");
            }
        }

        private static IEnumerable<YamlMappingEntry> CombineMergedAndExplicitEntries(
            List<YamlMappingEntry> mergedEntries,
            List<YamlMappingEntry> explicitEntries)
        {
            var explicitKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in explicitEntries)
            {
                var key = entry.ScalarKey;
                if (key is not null)
                {
                    explicitKeys.Add(key);
                }
            }

            return mergedEntries
                .Where(entry => entry.ScalarKey is null || !explicitKeys.Contains(entry.ScalarKey))
                .Concat(explicitEntries);
        }

        private void RegisterAnchor(string? anchor, YamlNode node, ParsingEvent source)
        {
            if (anchor is null)
            {
                return;
            }

            if (!_anchors.TryAdd(anchor, node))
            {
                throw Error(source, $"YAML anchor '&{anchor}' is defined more than once.");
            }
        }

        private T Take<T>()
            where T : ParsingEvent
        {
            if (_current is not T result)
            {
                throw Error(_current, $"Expected YAML event {typeof(T).Name}.");
            }

            Advance();
            return result;
        }

        private void Advance()
        {
            _current = _parser.MoveNext() ? _parser.Current : null;
        }

        private SourceSpan Span(ParsingEvent? value) => value is null
            ? UnknownSpan()
            : CreateSpan(_sourceName, value.Start, value.End);

        private SourceSpan UnknownSpan()
        {
            var position = new SourcePosition(1, 1, 0);
            return new SourceSpan(_sourceName, position, position);
        }

        private YamlFormatException Error(ParsingEvent? value, string message) =>
            new(message, Span(value));

        private static YamlFormatException Error(YamlNode value, string message) =>
            new(message, value.Span);

        private static string? ValueOrNull(AnchorName value) => value.IsEmpty ? null : value.Value;

        private static string? ValueOrNull(TagName value) => value.IsEmpty ? null : value.Value;

        private static bool IsNullSpelling(string value) => value is "~" or "null" or "Null" or "NULL";

        private static YamlScalarStyle ConvertStyle(DotNetScalarStyle style) => style switch
        {
            DotNetScalarStyle.Plain => YamlScalarStyle.Plain,
            DotNetScalarStyle.SingleQuoted => YamlScalarStyle.SingleQuoted,
            DotNetScalarStyle.DoubleQuoted => YamlScalarStyle.DoubleQuoted,
            DotNetScalarStyle.Literal => YamlScalarStyle.Literal,
            DotNetScalarStyle.Folded => YamlScalarStyle.Folded,
            _ => YamlScalarStyle.Plain,
        };
    }
}
