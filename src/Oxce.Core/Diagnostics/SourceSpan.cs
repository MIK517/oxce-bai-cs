namespace Oxce.Core.Diagnostics;

public readonly record struct SourcePosition(int Line, int Column, long Offset)
{
    public static SourcePosition FromZeroBased(long line, long column, long offset) =>
        new(checked((int)line + 1), checked((int)column + 1), offset);
}

public readonly record struct SourceSpan(
    string SourceName,
    SourcePosition Start,
    SourcePosition End)
{
    public override string ToString() =>
        $"{SourceName} at line {Start.Line}:{Start.Column}";
}
