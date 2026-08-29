using Oxce.Core.Diagnostics;
using Oxce.Scripting.Lexing;

namespace Oxce.Scripting.Syntax;

public sealed class ScriptStatementSyntax
{
    public ScriptStatementSyntax(
        ScriptToken? label,
        ScriptToken operation,
        IEnumerable<ScriptToken> arguments,
        SourceSpan span)
    {
        Label = label;
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = Array.AsReadOnly(arguments.ToArray());
        Span = span;
    }

    public ScriptToken? Label { get; }

    public ScriptToken Operation { get; }

    public IReadOnlyList<ScriptToken> Arguments { get; }

    public SourceSpan Span { get; }
}
public sealed class ScriptSyntaxTree
{
    public ScriptSyntaxTree(
        IEnumerable<ScriptStatementSyntax> statements,
        IEnumerable<DiagnosticEvent> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Statements = Array.AsReadOnly(statements.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public IReadOnlyList<ScriptStatementSyntax> Statements { get; }

    public IReadOnlyList<DiagnosticEvent> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity < DiagnosticSeverity.Error);
}
