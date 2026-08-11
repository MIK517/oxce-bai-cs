using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;

namespace Oxce.Formats.Yaml;

public enum YamlNodeKind
{
    Null,
    Scalar,
    Sequence,
    Mapping,
}

public enum YamlScalarStyle
{
    Plain,
    SingleQuoted,
    DoubleQuoted,
    Literal,
    Folded,
}

public abstract class YamlNode
{
    protected YamlNode(YamlNodeKind kind, SourceSpan span, string? tag, string? anchor)
    {
        Kind = kind;
        Span = span;
        Tag = tag;
        Anchor = anchor;
    }

    public YamlNodeKind Kind { get; }

    public SourceSpan Span { get; }

    public string? Tag { get; }

    public string? Anchor { get; }
}

public sealed class YamlNullNode : YamlNode
{
    public YamlNullNode(SourceSpan span, string spelling, string? tag = null, string? anchor = null)
        : base(YamlNodeKind.Null, span, tag, anchor)
    {
        Spelling = spelling;
    }

    public string Spelling { get; }
}

public sealed class YamlScalarNode : YamlNode
{
    public YamlScalarNode(
        SourceSpan span,
        string value,
        YamlScalarStyle style,
        string? tag = null,
        string? anchor = null)
        : base(YamlNodeKind.Scalar, span, tag, anchor)
    {
        Value = value;
        Style = style;
    }

    public string Value { get; }

    public YamlScalarStyle Style { get; }
}

public sealed class YamlSequenceNode : YamlNode
{
    public YamlSequenceNode(
        SourceSpan span,
        IEnumerable<YamlNode> items,
        string? tag = null,
        string? anchor = null)
        : base(YamlNodeKind.Sequence, span, tag, anchor)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = new ReadOnlyCollection<YamlNode>(items.ToArray());
    }

    public IReadOnlyList<YamlNode> Items { get; }
}

public sealed record YamlMappingEntry(YamlNode Key, YamlNode Value)
{
    public string? ScalarKey => (Key as YamlScalarNode)?.Value;
}

public sealed class YamlMappingNode : YamlNode
{
    public YamlMappingNode(
        SourceSpan span,
        IEnumerable<YamlMappingEntry> entries,
        string? tag = null,
        string? anchor = null)
        : base(YamlNodeKind.Mapping, span, tag, anchor)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = new ReadOnlyCollection<YamlMappingEntry>(entries.ToArray());
    }

    public IReadOnlyList<YamlMappingEntry> Entries { get; }

    public bool TryGet(string key, out YamlNode? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.ScalarKey, key, StringComparison.Ordinal))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    public IEnumerable<YamlNode> GetAll(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Entries
            .Where(entry => string.Equals(entry.ScalarKey, key, StringComparison.Ordinal))
            .Select(static entry => entry.Value);
    }
}

public sealed record YamlDocument(YamlNode Root, SourceSpan Span);

public sealed class YamlDocumentSet
{
    public YamlDocumentSet(string sourceName, IEnumerable<YamlDocument> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(documents);
        SourceName = sourceName;
        Documents = new ReadOnlyCollection<YamlDocument>(documents.ToArray());
    }

    public string SourceName { get; }

    public IReadOnlyList<YamlDocument> Documents { get; }
}
