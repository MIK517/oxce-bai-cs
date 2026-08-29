namespace Oxce.Scripting.Compilation;

public sealed record ScriptCompilerOptions
{
    public int MaximumInstructions { get; init; } = ScriptLimits.DefaultMaximumInstructions;

    public void Validate() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumInstructions);
}

public sealed class ScriptParserDefinition
{
    public ScriptParserDefinition(string name, IEnumerable<string> outputNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(outputNames);
        Name = name;
        OutputNames = Array.AsReadOnly(outputNames.ToArray());
        if (OutputNames.Count > ScriptLimits.MaximumOutputs)
        {
            throw new ArgumentOutOfRangeException(nameof(outputNames));
        }
        if (OutputNames.Any(string.IsNullOrWhiteSpace) ||
            OutputNames.Distinct(StringComparer.Ordinal).Count() != OutputNames.Count)
        {
            throw new ArgumentException("Script output names must be non-empty and unique.", nameof(outputNames));
        }
    }

    public string Name { get; }

    public IReadOnlyList<string> OutputNames { get; }
}
