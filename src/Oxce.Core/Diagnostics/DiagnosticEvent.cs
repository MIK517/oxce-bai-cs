namespace Oxce.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Trace,
    Information,
    Warning,
    Error,
    Critical,
}

public readonly record struct DiagnosticContext(
    string? LayerId = null,
    string? ModId = null,
    string? RuleType = null,
    string? RuleId = null,
    string? RelatedId = null);

public sealed record DiagnosticEvent
{
    public DiagnosticEvent(
        string code,
        DiagnosticSeverity severity,
        string message,
        SourceSpan? source = null,
        DiagnosticContext context = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        Code = code;
        Severity = severity;
        Message = message;
        Source = source;
        Context = context;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Message { get; }

    public SourceSpan? Source { get; }

    public DiagnosticContext Context { get; }
}
