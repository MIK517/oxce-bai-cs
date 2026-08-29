namespace Oxce.Scripting.Compilation;

using Oxce.Scripting.Api;

public sealed record ScriptCompilerOptions
{
    public int MaximumInstructions { get; init; } = ScriptLimits.DefaultMaximumInstructions;

    public void Validate() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumInstructions);
}

public sealed class ScriptParserDefinition
{
    public ScriptParserDefinition(
        string name,
        IEnumerable<string> outputNames,
        ScriptApiCatalog? apiCatalog = null,
        IEnumerable<string>? parserGroups = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(outputNames);
        Name = name;
        OutputNames = Array.AsReadOnly(outputNames.ToArray());
        ApiCatalog = apiCatalog ?? ScriptApiCatalog.Empty;
        ParserGroups = new HashSet<string>(parserGroups ?? [name], StringComparer.Ordinal);
        if (OutputNames.Count > ScriptLimits.MaximumOutputs)
        {
            throw new ArgumentOutOfRangeException(nameof(outputNames));
        }
        if (OutputNames.Any(string.IsNullOrWhiteSpace) ||
            OutputNames.Distinct(StringComparer.Ordinal).Count() != OutputNames.Count)
        {
            throw new ArgumentException("Script output names must be non-empty and unique.", nameof(outputNames));
        }
        if (ParserGroups.Count == 0 || ParserGroups.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Parser groups must be non-empty.", nameof(parserGroups));
        }
    }

    public string Name { get; }

    public IReadOnlyList<string> OutputNames { get; }

    public ScriptApiCatalog ApiCatalog { get; }

    public IReadOnlySet<string> ParserGroups { get; }
}
