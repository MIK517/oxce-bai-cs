using System.Collections.ObjectModel;
using Oxce.Scripting.Types;

namespace Oxce.Scripting.Binding;

public sealed class ScriptDeclarationCatalog
{
    private readonly ReadOnlyDictionary<string, ScriptTypeDefinition> _typesByName;
    private readonly ReadOnlyDictionary<ScriptTypeId, ScriptTypeDefinition> _typesById;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<ScriptOperationOverload>> _operations;

    internal ScriptDeclarationCatalog(
        IEnumerable<ScriptTypeDefinition> types,
        IEnumerable<ScriptOperationOverload> operations)
    {
        var typeArray = types.ToArray();
        var operationArray = operations.ToArray();
        Types = Array.AsReadOnly(typeArray);
        Operations = Array.AsReadOnly(operationArray);
        _typesByName = new ReadOnlyDictionary<string, ScriptTypeDefinition>(
            typeArray.ToDictionary(type => type.Name, StringComparer.Ordinal));
        _typesById = new ReadOnlyDictionary<ScriptTypeId, ScriptTypeDefinition>(
            typeArray.ToDictionary(type => type.Id));
        _operations = new ReadOnlyDictionary<string, IReadOnlyList<ScriptOperationOverload>>(
            operationArray
                .GroupBy(operation => operation.Name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ScriptOperationOverload>)Array.AsReadOnly(group.ToArray()),
                    StringComparer.Ordinal));
    }

    public IReadOnlyList<ScriptTypeDefinition> Types { get; }

    public IReadOnlyList<ScriptOperationOverload> Operations { get; }

    public bool TryGetType(string name, out ScriptTypeDefinition? type) =>
        _typesByName.TryGetValue(name, out type);

    public bool TryGetType(ScriptTypeId id, out ScriptTypeDefinition? type) =>
        _typesById.TryGetValue(id, out type);

    public IReadOnlyList<ScriptOperationOverload> GetOperations(string name) =>
        _operations.TryGetValue(name, out var operations)
            ? operations
            : Array.Empty<ScriptOperationOverload>();
}

public sealed class ScriptDeclarationCatalogBuilder
{
    private readonly List<ScriptTypeDefinition> _types = [];
    private readonly List<ScriptOperationOverload> _operations = [];
    private readonly HashSet<string> _typeNames = new(StringComparer.Ordinal);
    private readonly HashSet<ScriptTypeId> _typeIds = [];
    private readonly HashSet<ScriptOperationId> _operationIds = [];

    public void AddType(ScriptTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        type.Validate();
        if (_typeNames.Contains(type.Name) || _typeIds.Contains(type.Id))
        {
            throw new ArgumentException(
                $"Script type name '{type.Name}' and ID '{type.Id}' must both be unique.",
                nameof(type));
        }
        _typeNames.Add(type.Name);
        _typeIds.Add(type.Id);
        _types.Add(type);
    }

    public void AddOperation(ScriptOperationOverload operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_operationIds.Add(operation.Id))
        {
            throw new ArgumentException(
                $"Script operation ID '{operation.Id.Value}' is already registered.",
                nameof(operation));
        }
        _operations.Add(operation);
    }

    public ScriptDeclarationCatalog Build() => new(_types, _operations);
}
