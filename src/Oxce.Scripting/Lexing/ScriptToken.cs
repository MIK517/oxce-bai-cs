using Oxce.Core.Diagnostics;

namespace Oxce.Scripting.Lexing;

public enum ScriptTokenKind
{
    End,
    Invalid,
    Colon,
    Semicolon,
    Symbol,
    Numeric,
    Text,
}

public sealed record ScriptToken(
    ScriptTokenKind Kind,
    string Lexeme,
    SourceSpan Span,
    int? NumericValue = null,
    string? TextValue = null);

public sealed class ScriptLexResult
{
    public ScriptLexResult(IEnumerable<ScriptToken> tokens, IEnumerable<DiagnosticEvent> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Tokens = Array.AsReadOnly(tokens.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public IReadOnlyList<ScriptToken> Tokens { get; }

    public IReadOnlyList<DiagnosticEvent> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity < DiagnosticSeverity.Error);
}
