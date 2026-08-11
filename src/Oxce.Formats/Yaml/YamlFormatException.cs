using Oxce.Core.Diagnostics;

namespace Oxce.Formats.Yaml;

public sealed class YamlFormatException : FormatException
{
    public YamlFormatException(string message, SourceSpan span)
        : base($"{message} ({span})")
    {
        Span = span;
    }

    public YamlFormatException(string message, SourceSpan span, Exception innerException)
        : base($"{message} ({span})", innerException)
    {
        Span = span;
    }

    public SourceSpan Span { get; }
}
