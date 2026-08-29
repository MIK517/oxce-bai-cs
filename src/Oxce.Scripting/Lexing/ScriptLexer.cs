using System.Text;
using Oxce.Core.Diagnostics;
using Oxce.Scripting.Diagnostics;

namespace Oxce.Scripting.Lexing;

public sealed class ScriptLexer
{
    private readonly string _source;
    private readonly string _sourceName;
    private readonly List<ScriptToken> _tokens = [];
    private readonly List<DiagnosticEvent> _diagnostics = [];
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    private ScriptLexer(string source, string sourceName)
    {
        _source = source;
        _sourceName = sourceName;
    }

    public static ScriptLexResult Tokenize(string source, string sourceName = "<script>")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        var lexer = new ScriptLexer(source, sourceName);
        lexer.Run();
        return new ScriptLexResult(lexer._tokens, lexer._diagnostics);
    }

    private void Run()
    {
        while (true)
        {
            SkipTrivia();
            if (IsEnd)
            {
                var position = Position;
                _tokens.Add(new ScriptToken(
                    ScriptTokenKind.End,
                    string.Empty,
                    new SourceSpan(_sourceName, position, position)));
                return;
            }

            var start = Position;
            var startOffset = _offset;
            var first = Advance();
            switch (first)
            {
                case ':':
                    AddToken(ScriptTokenKind.Colon, startOffset, start);
                    break;
                case ';':
                    AddToken(ScriptTokenKind.Semicolon, startOffset, start);
                    break;
                case '"':
                    ReadText(startOffset, start);
                    break;
                case '+' or '-':
                    ReadNumber(startOffset, start, requiresFirstDigit: true);
                    break;
                default:
                    if (IsDigit(first))
                    {
                        ReadNumber(startOffset, start, requiresFirstDigit: false);
                    }
                    else if (IsSymbolCharacter(first))
                    {
                        ReadSymbol(startOffset, start);
                    }
                    else
                    {
                        AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidToken,
                            $"Invalid script token '{first}'.");
                    }
                    break;
            }
        }
    }

    private void SkipTrivia()
    {
        while (!IsEnd)
        {
            if (Current == '#')
            {
                while (!IsEnd && Current != '\n')
                {
                    Advance();
                }
            }
            else if (Current is ' ' or '\r' or '\n' or '\t')
            {
                Advance();
            }
            else
            {
                return;
            }
        }
    }

    private void ReadSymbol(int startOffset, SourcePosition start)
    {
        while (!IsEnd && (IsSymbolCharacter(Current) || IsDigit(Current)))
        {
            Advance();
        }

        if (!IsEnd && !StartsNextToken(Current))
        {
            Advance();
            AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidToken,
                $"Invalid character in script symbol '{Slice(startOffset)}'.");
            return;
        }
        AddToken(ScriptTokenKind.Symbol, startOffset, start);
    }

    private void ReadNumber(int startOffset, SourcePosition start, bool requiresFirstDigit)
    {
        if (requiresFirstDigit && (IsEnd || !IsDigit(Current)))
        {
            AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidInteger,
                $"Invalid script integer '{Slice(startOffset)}'.");
            return;
        }

        if (!requiresFirstDigit)
        {
            // The first digit was consumed by the caller.
        }
        else
        {
            Advance();
        }

        var firstDigitOffset = startOffset + (requiresFirstDigit ? 1 : 0);
        if (_source[firstDigitOffset] == '0' && !IsEnd && Current is 'x' or 'X' or 'b' or 'B' or 'o' or 'O')
        {
            Advance();
        }

        while (!IsEnd && !StartsNextToken(Current))
        {
            if (!IsDigit(Current) && !IsHexLetter(Current))
            {
                Advance();
                AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidInteger,
                    $"Invalid script integer '{Slice(startOffset)}'.");
                return;
            }
            Advance();
        }

        var lexeme = Slice(startOffset);
        if (!ScriptIntegerLiteral.TryParse(lexeme, out var value))
        {
            AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidInteger,
                $"Invalid or out-of-range script integer '{lexeme}'.");
            return;
        }
        AddToken(ScriptTokenKind.Numeric, startOffset, start, value);
    }

    private void ReadText(int startOffset, SourcePosition start)
    {
        var value = new StringBuilder();
        while (!IsEnd)
        {
            var character = Advance();
            if (character == '"')
            {
                if (!IsEnd && !StartsNextToken(Current))
                {
                    Advance();
                    AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidToken,
                        "A text literal must be followed by whitespace, ':' or ';'.");
                    return;
                }
                AddToken(ScriptTokenKind.Text, startOffset, start, textValue: value.ToString());
                return;
            }
            if (character == '\n')
            {
                AddInvalid(startOffset, start, ScriptDiagnosticCodes.UnterminatedText,
                    "A script text literal cannot contain a newline.");
                return;
            }
            if (character == '\\')
            {
                if (IsEnd || Current is not ('"' or '\\'))
                {
                    if (!IsEnd)
                    {
                        Advance();
                    }
                    AddInvalid(startOffset, start, ScriptDiagnosticCodes.InvalidTextEscape,
                        "Only escaped quotes and backslashes are valid in script text literals.");
                    return;
                }
                value.Append(Advance());
            }
            else
            {
                value.Append(character);
            }
        }

        AddInvalid(startOffset, start, ScriptDiagnosticCodes.UnterminatedText,
            "Unterminated script text literal.");
    }

    private void AddToken(
        ScriptTokenKind kind,
        int startOffset,
        SourcePosition start,
        int? integerValue = null,
        string? textValue = null) =>
        _tokens.Add(new ScriptToken(
            kind,
            Slice(startOffset),
            new SourceSpan(_sourceName, start, Position),
            integerValue,
            textValue));

    private void AddInvalid(int startOffset, SourcePosition start, string code, string message)
    {
        var span = new SourceSpan(_sourceName, start, Position);
        _tokens.Add(new ScriptToken(ScriptTokenKind.Invalid, Slice(startOffset), span));
        _diagnostics.Add(new DiagnosticEvent(code, DiagnosticSeverity.Error, message, span));
    }

    private char Advance()
    {
        var character = _source[_offset++];
        if (character == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return character;
    }

    private string Slice(int startOffset) => _source[startOffset.._offset];

    private SourcePosition Position => new(_line, _column, _offset);

    private bool IsEnd => _offset >= _source.Length || _source[_offset] == '\0';

    private char Current => _source[_offset];

    private static bool StartsNextToken(char character) =>
        character is '#' or ' ' or '\r' or '\n' or '\t' or ':' or ';';

    private static bool IsDigit(char character) => character is >= '0' and <= '9';

    private static bool IsHexLetter(char character) =>
        character is >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool IsSymbolCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '.';
}
