namespace Oxce.Scripting.Compilation;

using Oxce.Scripting.Api;
using Oxce.Scripting.Types;

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
        Outputs = Array.AsReadOnly(OutputNames.Select(static output => new ScriptNamedValueDeclaration(
            output,
            new ScriptTypeRef(
                ScriptPrimitiveTypes.Scalar,
                ScriptTypeModifier.Register | ScriptTypeModifier.Writable))).ToArray());
        Inputs = Array.Empty<ScriptNamedValueDeclaration>();
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

    private ScriptParserDefinition(ScriptParserDeclaration parser, ScriptApiCatalog catalog)
    {
        Name = parser.Name;
        OutputNames = parser.OutputNames;
        Outputs = parser.Outputs;
        Inputs = parser.Inputs;
        ApiCatalog = catalog;
        ParserGroups = new HashSet<string>([parser.Name], StringComparer.Ordinal);
    }

    public static ScriptParserDefinition FromCatalog(
        string parserName,
        ScriptApiCatalog? catalog = null)
    {
        catalog ??= ReferenceScriptApiCatalog.Instance;
        if (!catalog.TryGetParser(parserName, out var parser))
        {
            throw new ArgumentException($"Unknown script parser '{parserName}'.", nameof(parserName));
        }
        return new ScriptParserDefinition(parser!, catalog);
    }

    public string Name { get; }

    public IReadOnlyList<string> OutputNames { get; }

    public IReadOnlyList<ScriptNamedValueDeclaration> Outputs { get; }

    public IReadOnlyList<ScriptNamedValueDeclaration> Inputs { get; }

    public ScriptApiCatalog ApiCatalog { get; }

    public IReadOnlySet<string> ParserGroups { get; }
}
