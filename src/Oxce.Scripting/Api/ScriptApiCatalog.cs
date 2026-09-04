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
    ScriptReferenceLocation Reference)
{
    public ScriptTypeRef Type { get; init; } = new(ScriptPrimitiveTypes.Scalar);
}

public sealed record ScriptNamedValueDeclaration(string Name, ScriptTypeRef Type);

public sealed class ScriptParserDeclaration
{
    public ScriptParserDeclaration(
        string name,
        string group,
        IReadOnlyList<string> outputNames,
        bool supportsEvents,
        ScriptReferenceLocation reference)
        : this(
            name,
            group,
            outputNames.Select(static output => new ScriptNamedValueDeclaration(
                output,
                new ScriptTypeRef(
                    ScriptPrimitiveTypes.Scalar,
                    ScriptTypeModifier.Register | ScriptTypeModifier.Writable))),
            [],
            supportsEvents,
            reference)
    {
    }

    public ScriptParserDeclaration(
        string name,
        string group,
        IEnumerable<ScriptNamedValueDeclaration> outputs,
        IEnumerable<ScriptNamedValueDeclaration> inputs,
        bool supportsEvents,
        ScriptReferenceLocation reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(inputs);
        Name = name;
        Group = group;
        Outputs = Array.AsReadOnly(outputs.ToArray());
        Inputs = Array.AsReadOnly(inputs.ToArray());
        OutputNames = Array.AsReadOnly(Outputs.Select(static output => output.Name).ToArray());
        SupportsEvents = supportsEvents;
        Reference = reference.Validate();
    }

    public string Name { get; }
    public string Group { get; }
    public IReadOnlyList<ScriptNamedValueDeclaration> Outputs { get; }
    public IReadOnlyList<ScriptNamedValueDeclaration> Inputs { get; }
    public IReadOnlyList<string> OutputNames { get; }
    public bool SupportsEvents { get; }
    public ScriptReferenceLocation Reference { get; }
}

public sealed class ScriptApiCatalog
{
    private readonly ReadOnlyDictionary<int, ScriptBindingDeclaration> _bindingsById;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<ScriptBindingDeclaration>> _bindingsByName;
    private readonly ReadOnlyDictionary<(string Name, string ParserGroup),
        IReadOnlyList<ScriptBindingDeclaration>> _bindingsByNameAndParserGroup;
    private readonly ReadOnlyDictionary<string, ScriptParserDeclaration> _parsersByName;
    private readonly ReadOnlyDictionary<ScriptTypeId, ScriptTypeDefinition> _typesById;
    private readonly ReadOnlyDictionary<string, ScriptTypeDefinition> _typesByName;

    public ScriptApiCatalog(
        IEnumerable<ScriptBindingDeclaration> bindings,
        IEnumerable<ScriptConstantDeclaration>? constants = null,
        IEnumerable<ScriptParserDeclaration>? parsers = null,
        IEnumerable<ScriptTypeDefinition>? types = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var bindingArray = bindings.ToArray();
        var constantArray = (constants ?? []).ToArray();
        var parserArray = (parsers ?? []).ToArray();
        var typeArray = (types ?? []).Select(static type => type.Validate()).ToArray();
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
            if (parser.Outputs.Count > ScriptLimits.MaximumOutputs ||
                parser.Outputs.Any(static output => string.IsNullOrWhiteSpace(output.Name)) ||
                parser.Outputs.Select(static output => output.Name).Distinct(StringComparer.Ordinal).Count() != parser.Outputs.Count ||
                parser.Inputs.Any(static input => string.IsNullOrWhiteSpace(input.Name)) ||
                parser.Inputs.Select(static input => input.Name).Distinct(StringComparer.Ordinal).Count() != parser.Inputs.Count ||
                parser.Outputs.Concat(parser.Inputs).Select(static value => value.Name)
                    .Distinct(StringComparer.Ordinal).Count() != parser.Outputs.Count + parser.Inputs.Count)
            {
                throw new ArgumentException("Parser value names must be bounded, non-empty, and unique.", nameof(parsers));
            }
        }
        constantArray = constantArray.Select(static constant => constant with
        {
            ParserGroups = Array.AsReadOnly(constant.ParserGroups.ToArray()),
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
        if (typeArray.Select(static type => type.Id).Distinct().Count() != typeArray.Length ||
            typeArray.Select(static type => type.Name).Distinct(StringComparer.Ordinal).Count() != typeArray.Length)
        {
            throw new ArgumentException("Script type IDs and names must be unique.", nameof(types));
        }

        Bindings = Array.AsReadOnly(bindingArray);
        Constants = Array.AsReadOnly(constantArray);
        Parsers = Array.AsReadOnly(parserArray);
        Types = Array.AsReadOnly(typeArray);
        _bindingsById = new ReadOnlyDictionary<int, ScriptBindingDeclaration>(
            bindingArray.ToDictionary(static binding => binding.Id.Value));
        _bindingsByName = new ReadOnlyDictionary<string, IReadOnlyList<ScriptBindingDeclaration>>(
            bindingArray.GroupBy(static binding => binding.Name, StringComparer.Ordinal).ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ScriptBindingDeclaration>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal));
        _bindingsByNameAndParserGroup = IndexBindingsByParserGroup(bindingArray);
        _parsersByName = new ReadOnlyDictionary<string, ScriptParserDeclaration>(
            parserArray.ToDictionary(static parser => parser.Name, StringComparer.Ordinal));
        _typesById = new ReadOnlyDictionary<ScriptTypeId, ScriptTypeDefinition>(
            typeArray.ToDictionary(static type => type.Id));
        _typesByName = new ReadOnlyDictionary<string, ScriptTypeDefinition>(
            typeArray.ToDictionary(static type => type.Name, StringComparer.Ordinal));
    }

    private ScriptApiCatalog(
        ScriptApiCatalog sharedCatalog,
        IEnumerable<ScriptConstantDeclaration> scopedConstants)
    {
        ArgumentNullException.ThrowIfNull(sharedCatalog);
        ArgumentNullException.ThrowIfNull(scopedConstants);
        var local = scopedConstants.Select(static constant => constant with
        {
            ParserGroups = Array.AsReadOnly(constant.ParserGroups.ToArray()),
        }).ToArray();
        foreach (var constant in local)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(constant.Name);
            if (constant.ParserGroups.Count == 0 || constant.ParserGroups.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Constants require at least one parser group.", nameof(scopedConstants));
            }
            constant.Reference.Validate();
        }
        var names = sharedCatalog.Constants.Select(static constant => constant.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (local.Any(constant => !names.Add(constant.Name)))
        {
            throw new ArgumentException("Scoped constant names must be unique across the shared catalog.",
                nameof(scopedConstants));
        }

        Bindings = sharedCatalog.Bindings;
        Constants = Array.AsReadOnly(sharedCatalog.Constants.Concat(local).ToArray());
        Parsers = sharedCatalog.Parsers;
        Types = sharedCatalog.Types;
        _bindingsById = sharedCatalog._bindingsById;
        _bindingsByName = sharedCatalog._bindingsByName;
        _bindingsByNameAndParserGroup = sharedCatalog._bindingsByNameAndParserGroup;
        _parsersByName = sharedCatalog._parsersByName;
        _typesById = sharedCatalog._typesById;
        _typesByName = sharedCatalog._typesByName;
        IsScope = true;
    }

    public static ScriptApiCatalog Empty { get; } = new([]);
    public IReadOnlyList<ScriptBindingDeclaration> Bindings { get; }
    public IReadOnlyList<ScriptConstantDeclaration> Constants { get; }
    public IReadOnlyList<ScriptParserDeclaration> Parsers { get; }
    public IReadOnlyList<ScriptTypeDefinition> Types { get; }
    public bool IsScope { get; }

    public ScriptApiCatalog CreateScope(IEnumerable<ScriptConstantDeclaration> constants) =>
        new(this, constants);

    public IReadOnlyList<ScriptBindingDeclaration> GetBindings(
        string name,
        IReadOnlySet<string> parserGroups)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(parserGroups);
        if (parserGroups.Count == 1)
        {
            foreach (var parserGroup in parserGroups)
            {
                return _bindingsByNameAndParserGroup.GetValueOrDefault((name, parserGroup), []);
            }
        }
        return _bindingsByName.TryGetValue(name, out var declarations)
            ? declarations.Where(binding => binding.ParserGroups.Any(parserGroups.Contains)).ToArray()
            : [];
    }

    public bool TryGetBinding(ScriptBindingId id, out ScriptBindingDeclaration? declaration) =>
        _bindingsById.TryGetValue(id.Value, out declaration);

    public bool TryGetParser(string name, out ScriptParserDeclaration? declaration) =>
        _parsersByName.TryGetValue(name, out declaration);

    public bool TryGetType(ScriptTypeId id, out ScriptTypeDefinition? definition) =>
        _typesById.TryGetValue(id, out definition);

    public bool TryGetType(string name, out ScriptTypeDefinition? definition) =>
        _typesByName.TryGetValue(name, out definition);

    public IEnumerable<ScriptConstantDeclaration> GetConstants(IReadOnlySet<string> parserGroups) =>
        Constants.Where(constant => constant.ParserGroups.Any(parserGroups.Contains));

    private static ReadOnlyDictionary<(string Name, string ParserGroup),
        IReadOnlyList<ScriptBindingDeclaration>> IndexBindingsByParserGroup(
            IEnumerable<ScriptBindingDeclaration> bindings) => new(
        bindings.SelectMany(static binding => binding.ParserGroups.Select(parserGroup =>
                (Key: (binding.Name, parserGroup), Binding: binding)))
            .GroupBy(static item => item.Key)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ScriptBindingDeclaration>)
                    Array.AsReadOnly(group.Select(static item => item.Binding).ToArray())));

}
