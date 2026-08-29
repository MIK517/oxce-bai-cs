using Oxce.Scripting;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Syntax;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptSyntaxParserTests
{
    [Fact]
    public void ParsesLabeledStatementsAndArguments()
    {
        var result = ScriptSyntaxParser.Parse("start: set result 42; return result;", "probe");

        Assert.True(result.IsValid);
        Assert.Collection(
            result.Statements,
            statement =>
            {
                Assert.Equal("start", statement.Label?.Lexeme);
                Assert.Equal("set", statement.Operation.Lexeme);
                Assert.Equal(["result", "42"], statement.Arguments.Select(item => item.Lexeme));
                Assert.Equal(1, statement.Span.Start.Line);
                Assert.Equal(1, statement.Span.Start.Column);
            },
            statement =>
            {
                Assert.Null(statement.Label);
                Assert.Equal("return", statement.Operation.Lexeme);
                Assert.Equal("result", Assert.Single(statement.Arguments).Lexeme);
            });
    }

    [Fact]
    public void RequiresSemicolon()
    {
        var result = ScriptSyntaxParser.Parse("return result");

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics,
            item => item.Code == ScriptDiagnosticCodes.MissingStatementTerminator);
    }

    [Fact]
    public void RequiresOperationAfterLabel()
    {
        var result = ScriptSyntaxParser.Parse("label:;");

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == ScriptDiagnosticCodes.InvalidLabel);
    }

    [Fact]
    public void EnforcesReferenceArgumentLimit()
    {
        var arguments = string.Join(' ', Enumerable.Range(0, ScriptLimits.MaximumArguments + 1));

        var result = ScriptSyntaxParser.Parse($"probe {arguments};");

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == ScriptDiagnosticCodes.TooManyArguments);
    }
}
