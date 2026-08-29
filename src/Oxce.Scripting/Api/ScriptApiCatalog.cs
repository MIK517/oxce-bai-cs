using System.Collections.ObjectModel;
using Oxce.Scripting.Types;

namespace Oxce.Scripting.Api;

public readonly record struct ScriptBindingId
{
    public ScriptBindingId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }
}

public sealed record ScriptReferenceLocation(string Path, int Line)
{
    public ScriptReferenceLocation Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Line);
        return this;
    }
}

public sealed record ScriptBindingParameter(string Name, ScriptTypeRef Type, bool Writable)
{
    public ScriptBindingParameter Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Writable != Type.IsWritable || Writable && !Type.IsRegister)
        {
            throw new ArgumentException("Binding parameter writability must match its script type.");
        }
        return this;
    }
}

public sealed class ScriptBindingDeclaration
{
    public ScriptBindingDeclaration(
        ScriptBindingId id,
        string name,
        IEnumerable<ScriptBindingParameter> parameters,
        IEnumerable<string> parserGroups,
        ScriptReferenceLocation reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(parserGroups);
        Id = id;
        Name = name;
        Parameters = Array.AsReadOnly(parameters.Select(static parameter => parameter.Validate()).ToArray());
        ParserGroups = Array.AsReadOnly(parserGroups.Distinct(StringComparer.Ordinal).ToArray());
        Reference = reference.Validate();
        if (Parameters.Count > ScriptLimits.MaximumArguments)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters));
        }
        if (ParserGroups.Count == 0 || ParserGroups.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A binding must belong to at least one parser group.", nameof(parserGroups));
        }
    }

    public ScriptBindingId Id { get; }
    public string Name { get; }
    public IReadOnlyList<ScriptBindingParameter> Parameters { get; }
    public IReadOnlyList<string> ParserGroups { get; }
    public ScriptReferenceLocation Reference { get; }
}

public sealed record ScriptConstantDeclaration(
    string Name,
    int Value,
    IReadOnlyList<string> ParserGroups,
    ScriptReferenceLocation Reference);

public sealed record ScriptParserDeclaration(
    string Name,
    string Group,
    IReadOnlyList<string> OutputNames,
    bool SupportsEvents,
    ScriptReferenceLocation Reference);

public sealed class ScriptApiCatalog
{
    private readonly ReadOnlyDictionary<int, ScriptBindingDeclaration> _bindingsById;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<ScriptBindingDeclaration>> _bindingsByName;

    public ScriptApiCatalog(
        IEnumerable<ScriptBindingDeclaration> bindings,
        IEnumerable<ScriptConstantDeclaration>? constants = null,
        IEnumerable<ScriptParserDeclaration>? parsers = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var bindingArray = bindings.ToArray();
        var constantArray = (constants ?? []).ToArray();
        var parserArray = (parsers ?? []).ToArray();
        foreach (var constant in constantArray)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(constant.Name);
            ArgumentNullException.ThrowIfNull(constant.ParserGroups);
            if (constant.ParserGroups.Count == 0 || constant.ParserGroups.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Constants require at least one parser group.", nameof(constants));
            }
            constant.Reference.Validate();
        }
        foreach (var parser in parserArray)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parser.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(parser.Group);
            ArgumentNullException.ThrowIfNull(parser.OutputNames);
            if (parser.OutputNames.Count > ScriptLimits.MaximumOutputs ||
                parser.OutputNames.Any(string.IsNullOrWhiteSpace) ||
                parser.OutputNames.Distinct(StringComparer.Ordinal).Count() != parser.OutputNames.Count)
            {
                throw new ArgumentException("Parser output names must be bounded, non-empty, and unique.", nameof(parsers));
            }
            parser.Reference.Validate();
        }
        constantArray = constantArray.Select(static constant => constant with
        {
            ParserGroups = Array.AsReadOnly(constant.ParserGroups.ToArray()),
        }).ToArray();
        parserArray = parserArray.Select(static parser => parser with
        {
            OutputNames = Array.AsReadOnly(parser.OutputNames.ToArray()),
        }).ToArray();
        if (bindingArray.Select(static binding => binding.Id.Value).Distinct().Count() != bindingArray.Length)
        {
            throw new ArgumentException("Binding IDs must be unique.", nameof(bindings));
        }
        if (constantArray.Select(static constant => constant.Name).Distinct(StringComparer.Ordinal).Count() !=
            constantArray.Length)
        {
            throw new ArgumentException("Constant names must be unique.", nameof(constants));
        }
        if (parserArray.Select(static parser => parser.Name).Distinct(StringComparer.Ordinal).Count() != parserArray.Length)
        {
            throw new ArgumentException("Parser names must be unique.", nameof(parsers));
        }

        Bindings = Array.AsReadOnly(bindingArray);
        Constants = Array.AsReadOnly(constantArray);
        Parsers = Array.AsReadOnly(parserArray);
        _bindingsById = new ReadOnlyDictionary<int, ScriptBindingDeclaration>(
            bindingArray.ToDictionary(static binding => binding.Id.Value));
        _bindingsByName = new ReadOnlyDictionary<string, IReadOnlyList<ScriptBindingDeclaration>>(
            bindingArray.GroupBy(static binding => binding.Name, StringComparer.Ordinal).ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ScriptBindingDeclaration>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal));
    }

    public static ScriptApiCatalog Empty { get; } = new([]);
    public IReadOnlyList<ScriptBindingDeclaration> Bindings { get; }
    public IReadOnlyList<ScriptConstantDeclaration> Constants { get; }
    public IReadOnlyList<ScriptParserDeclaration> Parsers { get; }

    public IReadOnlyList<ScriptBindingDeclaration> GetBindings(string name, IReadOnlySet<string> parserGroups) =>
        _bindingsByName.TryGetValue(name, out var declarations)
            ? declarations.Where(binding => binding.ParserGroups.Any(parserGroups.Contains)).ToArray()
            : [];

    public bool TryGetBinding(ScriptBindingId id, out ScriptBindingDeclaration? declaration) =>
        _bindingsById.TryGetValue(id.Value, out declaration);

    public IEnumerable<ScriptConstantDeclaration> GetConstants(IReadOnlySet<string> parserGroups) =>
        Constants.Where(constant => constant.ParserGroups.Any(parserGroups.Contains));
}
