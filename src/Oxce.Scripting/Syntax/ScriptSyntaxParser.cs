using Oxce.Core.Diagnostics;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Lexing;

namespace Oxce.Scripting.Syntax;

public static class ScriptSyntaxParser
{
    public static ScriptSyntaxTree Parse(string source, string sourceName = "<script>")
    {
        var lexed = ScriptLexer.Tokenize(source, sourceName);
        var diagnostics = new List<DiagnosticEvent>(lexed.Diagnostics);
        var statements = new List<ScriptStatementSyntax>();
        var position = 0;

        while (lexed.Tokens[position].Kind != ScriptTokenKind.End)
        {
            var statementStart = lexed.Tokens[position].Span.Start;
            var first = lexed.Tokens[position++];
            if (first.Kind == ScriptTokenKind.Invalid)
            {
                RecoverToTerminator(lexed.Tokens, ref position);
                continue;
            }
            if (first.Kind != ScriptTokenKind.Symbol)
            {
                diagnostics.Add(Error(
                    ScriptDiagnosticCodes.MissingOperation,
                    "A script statement must begin with an operation or label.",
                    first.Span));
                RecoverToTerminator(lexed.Tokens, ref position);
                continue;
            }

            ScriptToken? label = null;
            ScriptToken operation;
            if (lexed.Tokens[position].Kind == ScriptTokenKind.Colon)
            {
                label = first;
                position++;
                operation = lexed.Tokens[position++];
                if (operation.Kind != ScriptTokenKind.Symbol)
                {
                    diagnostics.Add(Error(
                        ScriptDiagnosticCodes.InvalidLabel,
                        $"Label '{label.Lexeme}' must be followed by an operation.",
                        operation.Span));
                    RecoverToTerminator(lexed.Tokens, ref position);
                    continue;
                }
            }
            else
            {
                operation = first;
            }

            var arguments = new List<ScriptToken>();
            while (lexed.Tokens[position].Kind is not ScriptTokenKind.Semicolon and not ScriptTokenKind.End)
            {
                var argument = lexed.Tokens[position++];
                if (argument.Kind is ScriptTokenKind.Colon or ScriptTokenKind.Invalid)
                {
                    diagnostics.Add(Error(
                        ScriptDiagnosticCodes.InvalidToken,
                        $"Invalid argument '{argument.Lexeme}' in operation '{operation.Lexeme}'.",
                        argument.Span));
                }
                arguments.Add(argument);
            }

            if (lexed.Tokens[position].Kind == ScriptTokenKind.End)
            {
                diagnostics.Add(Error(
                    ScriptDiagnosticCodes.MissingStatementTerminator,
                    $"Operation '{operation.Lexeme}' must end with ';'.",
                    operation.Span));
                break;
            }

            var terminator = lexed.Tokens[position++];
            if (arguments.Count > ScriptLimits.MaximumArguments)
            {
                diagnostics.Add(Error(
                    ScriptDiagnosticCodes.TooManyArguments,
                    $"Operation '{operation.Lexeme}' has {arguments.Count} arguments; the limit is {ScriptLimits.MaximumArguments}.",
                    operation.Span));
            }
            else if (arguments.All(static argument => argument.Kind != ScriptTokenKind.Invalid))
            {
                statements.Add(new ScriptStatementSyntax(
                    label,
                    operation,
                    arguments,
                    new SourceSpan(sourceName, statementStart, terminator.Span.End)));
            }
        }

        return new ScriptSyntaxTree(statements, diagnostics);
    }

    private static void RecoverToTerminator(IReadOnlyList<ScriptToken> tokens, ref int position)
    {
        while (tokens[position].Kind is not ScriptTokenKind.Semicolon and not ScriptTokenKind.End)
        {
            position++;
        }
        if (tokens[position].Kind == ScriptTokenKind.Semicolon)
        {
            position++;
        }
    }

    private static DiagnosticEvent Error(string code, string message, SourceSpan span) =>
        new(code, DiagnosticSeverity.Error, message, span);
}
