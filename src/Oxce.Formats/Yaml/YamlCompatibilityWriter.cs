using System.Text;

namespace Oxce.Formats.Yaml;

public static class YamlCompatibilityWriter
{
    public static string Emit(YamlDocumentSet documents, YamlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        options ??= new YamlWriteOptions();
        options.Validate();
        return new Writer(options).Write(documents);
    }

    public static void EmitFile(
        string path,
        YamlDocumentSet documents,
        YamlWriteOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(Path.GetFullPath(path), Emit(documents, options), new UTF8Encoding(false));
    }

    private sealed class Writer
    {
        private readonly YamlWriteOptions _options;
        private readonly StringBuilder _output = new();
        private readonly HashSet<YamlNode> _emittedAnchors = new(ReferenceEqualityComparer.Instance);
        private int _byteCount;

        public Writer(YamlWriteOptions options)
        {
            _options = options;
        }

        public string Write(YamlDocumentSet documents)
        {
            for (var index = 0; index < documents.Documents.Count; index++)
            {
                if (index != 0)
                {
                    Append("---\n");
                }

                WriteRoot(documents.Documents[index].Root, depth: 1);
            }

            return _output.ToString();
        }

        private void WriteRoot(YamlNode node, int depth)
        {
            CheckDepth(node, depth);
            if (CanWriteInline(node))
            {
                _ = TryWriteInline(node);
                Append('\n');
                return;
            }

            WriteCollectionPrefix(node, appendLeadingSpace: false);
            if (HasPrefix(node))
            {
                Append('\n');
            }

            WriteCollection(node, indent: 0, depth);
        }

        private void WriteCollection(YamlNode node, int indent, int depth)
        {
            CheckDepth(node, depth);
            switch (node)
            {
                case YamlMappingNode mapping:
                    WriteMapping(mapping, indent, depth);
                    break;
                case YamlSequenceNode sequence:
                    WriteSequence(sequence, indent, depth);
                    break;
                default:
                    throw Error(node, "Expected a YAML collection.");
            }
        }

        private void WriteMapping(YamlMappingNode mapping, int indent, int depth)
        {
            foreach (var entry in mapping.Entries)
            {
                AppendIndent(indent);
                if (!TryWriteInlineKey(entry.Key))
                {
                    throw Error(entry.Key, "Complex YAML mapping keys cannot be emitted yet.");
                }

                Append(':');
                WriteNestedValue(entry.Value, indent, depth);
            }
        }

        private void WriteSequence(YamlSequenceNode sequence, int indent, int depth)
        {
            foreach (var item in sequence.Items)
            {
                AppendIndent(indent);
                Append('-');
                WriteNestedValue(item, indent, depth);
            }
        }

        private void WriteNestedValue(YamlNode node, int indent, int depth)
        {
            CheckDepth(node, depth + 1);
            if (CanWriteInline(node))
            {
                Append(' ');
                _ = TryWriteInline(node);
                Append('\n');
                return;
            }

            WriteCollectionPrefix(node, appendLeadingSpace: true);
            Append('\n');
            WriteCollection(node, indent + _options.IndentSize, depth + 1);
        }

        private bool TryWriteInlineKey(YamlNode node)
        {
            switch (node)
            {
                case YamlScalarNode scalar:
                    WriteScalar(scalar);
                    return true;
                case YamlNullNode:
                    Append('~');
                    return true;
                default:
                    return false;
            }
        }

        private bool TryWriteInline(YamlNode node)
        {
            if (node.Anchor is not null && _emittedAnchors.Contains(node))
            {
                Append('*');
                Append(node.Anchor);
                return true;
            }

            switch (node)
            {
                case YamlScalarNode scalar:
                    WriteInlinePrefix(node);
                    WriteScalar(scalar);
                    return true;
                case YamlNullNode:
                    WriteInlinePrefix(node);
                    Append('~');
                    return true;
                case YamlMappingNode { Entries.Count: 0 }:
                    WriteInlinePrefix(node);
                    Append("{}");
                    return true;
                case YamlSequenceNode { Items.Count: 0 }:
                    WriteInlinePrefix(node);
                    Append("[]");
                    return true;
                default:
                    return false;
            }
        }

        private void WriteInlinePrefix(YamlNode node)
        {
            var hasPrefix = node.Tag is not null ||
                node.Anchor is not null && !_emittedAnchors.Contains(node);
            WriteCollectionPrefix(node, appendLeadingSpace: false);
            if (hasPrefix)
            {
                Append(' ');
            }
        }

        private void WriteCollectionPrefix(YamlNode node, bool appendLeadingSpace)
        {
            var hasTag = node.Tag is not null;
            var hasAnchor = node.Anchor is not null && !_emittedAnchors.Contains(node);
            if (!hasTag && !hasAnchor)
            {
                return;
            }

            if (appendLeadingSpace)
            {
                Append(' ');
            }

            if (hasTag)
            {
                Append(FormatTag(node.Tag!));
                if (hasAnchor)
                {
                    Append(' ');
                }
            }

            if (hasAnchor)
            {
                Append('&');
                Append(node.Anchor!);
                _emittedAnchors.Add(node);
            }
        }

        private void WriteScalar(YamlScalarNode scalar)
        {
            if (scalar.Style == YamlScalarStyle.Plain && CanEmitPlain(scalar.Value))
            {
                Append(scalar.Value);
                return;
            }

            if (scalar.Style == YamlScalarStyle.SingleQuoted)
            {
                Append('\'');
                Append(scalar.Value.Replace("'", "''", StringComparison.Ordinal));
                Append('\'');
                return;
            }

            Append('"');
            Append(EscapeDoubleQuoted(scalar.Value));
            Append('"');
        }

        private bool CanWriteInline(YamlNode node)
        {
            return node.Anchor is not null && _emittedAnchors.Contains(node) ||
                node is YamlScalarNode or YamlNullNode or
                YamlMappingNode { Entries.Count: 0 } or
                YamlSequenceNode { Items.Count: 0 };
        }

        private static bool HasPrefix(YamlNode node) => node.Tag is not null || node.Anchor is not null;

        private static string FormatTag(string tag) => tag.StartsWith('!') ? tag : $"!<{tag}>";

        private static bool CanEmitPlain(string value)
        {
            if (string.IsNullOrEmpty(value) || value is "~" or "null" or "Null" or "NULL")
            {
                return false;
            }

            if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0], StringComparison.Ordinal))
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsControl(character) || character is '\r' or '\n')
                {
                    return false;
                }

                if (character == ':' && index + 1 < value.Length && char.IsWhiteSpace(value[index + 1]))
                {
                    return false;
                }

                if (character == '#' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
                {
                    return false;
                }
            }

            return true;
        }

        private static string EscapeDoubleQuoted(string value)
        {
            var output = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': output.Append("\\\\"); break;
                    case '"': output.Append("\\\""); break;
                    case '\0': output.Append("\\0"); break;
                    case '\a': output.Append("\\a"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\t': output.Append("\\t"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\v': output.Append("\\v"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\r': output.Append("\\r"); break;
                    default:
                        if (character < ' ')
                        {
                            output.Append("\\x");
                            output.Append(((int)character).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            output.Append(character);
                        }
                        break;
                }
            }

            return output.ToString();
        }

        private void AppendIndent(int count)
        {
            if (count != 0)
            {
                Append(new string(' ', count));
            }
        }

        private void Append(char value) => Append(value.ToString());

        private void Append(string value)
        {
            var addedBytes = Encoding.UTF8.GetByteCount(value);
            if (_byteCount > _options.MaxBytes - addedBytes)
            {
                var position = new Oxce.Core.Diagnostics.SourcePosition(1, 1, 0);
                var span = new Oxce.Core.Diagnostics.SourceSpan("(emitted YAML)", position, position);
                throw new YamlFormatException(
                    $"Emitted YAML exceeds the {_options.MaxBytes}-byte limit.",
                    span);
            }

            _output.Append(value);
            _byteCount += addedBytes;
        }

        private void CheckDepth(YamlNode node, int depth)
        {
            if (depth > _options.MaxDepth)
            {
                throw Error(node, $"Emitted YAML exceeds the {_options.MaxDepth}-level depth limit.");
            }
        }

        private static YamlFormatException Error(YamlNode node, string message) =>
            new(message, node.Span);
    }
}
