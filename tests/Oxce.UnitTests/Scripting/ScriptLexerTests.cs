using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Lexing;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptLexerTests
{
    [Fact]
    public void TokenizesCommentsSymbolsLabelsLiteralsAndLocations()
    {
        const string source = "# heading\nentry: object.member value -0x2a \"a\\\\b\\\"c\";";

        var result = ScriptLexer.Tokenize(source, "fixture.rul");

        Assert.True(result.IsValid);
        Assert.Collection(
            result.Tokens,
            token => AssertToken(token, ScriptTokenKind.Symbol, "entry", 2, 1),
            token => AssertToken(token, ScriptTokenKind.Colon, ":", 2, 6),
            token => AssertToken(token, ScriptTokenKind.Symbol, "object.member", 2, 8),
            token => AssertToken(token, ScriptTokenKind.Symbol, "value", 2, 22),
            token =>
            {
                AssertToken(token, ScriptTokenKind.Numeric, "-0x2a", 2, 28);
                Assert.Equal(-42, token.NumericValue);
            },
            token =>
            {
                AssertToken(token, ScriptTokenKind.Text, "\"a\\\\b\\\"c\"", 2, 34);
                Assert.Equal("a\\b\"c", token.TextValue);
            },
            token => AssertToken(token, ScriptTokenKind.Semicolon, ";", 2, 43),
            token => Assert.Equal(ScriptTokenKind.End, token.Kind));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("+42", 42)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("0x7fffffff", int.MaxValue)]
    [InlineData("0b101010", 42)]
    [InlineData("0o52", 42)]
    [InlineData("2147483648", int.MinValue)]
    [InlineData("4294967295", -1)]
    [InlineData("4294967296", 0)]
    [InlineData("-2147483649", int.MaxValue)]
    public void ParsesReferenceIntegerForms(string source, int expected)
    {
        var result = ScriptLexer.Tokenize(source);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Tokens[0].NumericValue);
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0b2")]
    [InlineData("+")]
    public void RejectsMalformedIntegers(string source)
    {
        var result = ScriptLexer.Tokenize(source);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ScriptDiagnosticCodes.InvalidInteger, diagnostic.Code);
        Assert.Equal(ScriptTokenKind.Invalid, result.Tokens[0].Kind);
    }

    [Theory]
    [InlineData("\"bad\\n\"", ScriptDiagnosticCodes.InvalidTextEscape)]
    [InlineData("\"line\nbreak\"", ScriptDiagnosticCodes.UnterminatedText)]
    [InlineData("\"unterminated", ScriptDiagnosticCodes.UnterminatedText)]
    public void RejectsInvalidTextLiterals(string source, string expectedCode)
    {
        var result = ScriptLexer.Tokenize(source);

        Assert.Contains(result.Diagnostics, item => item.Code == expectedCode);
    }

    private static void AssertToken(
        ScriptToken token,
        ScriptTokenKind kind,
        string lexeme,
        int line,
        int column)
    {
        Assert.Equal(kind, token.Kind);
        Assert.Equal(lexeme, token.Lexeme);
        Assert.Equal(line, token.Span.Start.Line);
        Assert.Equal(column, token.Span.Start.Column);
    }
}
