using Oxce.Core.Diagnostics;
using Oxce.Scripting.Diagnostics;

namespace Oxce.Scripting.Globals;

public readonly record struct ScriptTagTypeId
{
    public ScriptTagTypeId(ushort value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ushort Value { get; }
}

public sealed record ScriptTagTypeDefinition(ScriptTagTypeId Id, string Name, int Limit);

public sealed record ScriptTagDefinition(
    ScriptTagTypeId OwnerType,
    int Index,
    string Name,
    string ValueType,
    string SourceFile);

public sealed class ScriptTagCatalog
{
    private readonly Dictionary<ScriptTagTypeId, ScriptTagTypeDefinition> _types;
    private readonly Dictionary<(ScriptTagTypeId Type, string Name), ScriptTagDefinition> _tags;

    internal ScriptTagCatalog(
        IEnumerable<ScriptTagTypeDefinition> types,
        IEnumerable<string> valueTypes,
        IEnumerable<ScriptTagDefinition> tags)
    {
        Types = Array.AsReadOnly(types.ToArray());
        ValueTypes = Array.AsReadOnly(valueTypes.ToArray());
        Tags = Array.AsReadOnly(tags.ToArray());
        _types = Types.ToDictionary(static type => type.Id);
        _tags = Tags.ToDictionary(static tag => (tag.OwnerType, tag.Name));
    }

    public IReadOnlyList<ScriptTagTypeDefinition> Types { get; }
    public IReadOnlyList<string> ValueTypes { get; }
    public IReadOnlyList<ScriptTagDefinition> Tags { get; }

    public bool TryGetType(ScriptTagTypeId id, out ScriptTagTypeDefinition? type) =>
        _types.TryGetValue(id, out type);

    public bool TryGetTag(ScriptTagTypeId type, string name, out ScriptTagDefinition? tag) =>
        _tags.TryGetValue((type, name), out tag);

    public bool TryGetTag(ScriptTagTypeId type, int index, out ScriptTagDefinition? tag)
    {
        tag = Tags.FirstOrDefault(candidate => candidate.OwnerType == type && candidate.Index == index);
        return tag is not null;
    }
}

public sealed class ScriptTagCatalogBuilder
{
    private readonly Dictionary<ScriptTagTypeId, ScriptTagTypeDefinition> _types = [];
    private readonly HashSet<string> _typeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _valueTypes = new(StringComparer.Ordinal) { "int" };
    private readonly List<ScriptTagDefinition> _tags = [];
    private readonly Dictionary<(ScriptTagTypeId Type, string Name), ScriptTagDefinition> _tagsByOwnerAndName = [];
    private readonly Dictionary<ScriptTagTypeId, int> _tagCounts = [];
    private readonly HashSet<string> _globalNames = new(StringComparer.Ordinal);
    private int _revision;

    public int Revision => _revision;

    public int TagCount => _tags.Count;

    public void AddType(ScriptTagTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(type.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(type.Limit);
        if (_types.ContainsKey(type.Id) || _typeNames.Contains(type.Name))
        {
            throw new ArgumentException("Script tag type IDs and names must be unique.", nameof(type));
        }
        _types.Add(type.Id, type);
        _typeNames.Add(type.Name);
        _revision = checked(_revision + 1);
    }

    public void AddValueType(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_valueTypes.Add(name))
        {
            throw new ArgumentException($"Script tag value type '{name}' is already registered.", nameof(name));
        }
        _revision = checked(_revision + 1);
    }

    public ScriptTagDefinition AddTag(
        ScriptTagTypeId ownerType,
        string name,
        string valueType,
        string sourceFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        if (!_types.TryGetValue(ownerType, out var type))
        {
            throw new ArgumentException($"Unknown script tag type {ownerType.Value}.", nameof(ownerType));
        }
        if (!_valueTypes.Contains(valueType))
        {
            throw new ArgumentException($"Unknown script tag value type '{valueType}'.", nameof(valueType));
        }
        var qualifiedName = name.StartsWith("Tag.", StringComparison.Ordinal) ? name : $"Tag.{name}";
        if (_globalNames.Contains(qualifiedName))
        {
            throw new ArgumentException($"Script name '{qualifiedName}' is already used by another tag.", nameof(name));
        }
        var index = checked(_tagCounts.GetValueOrDefault(ownerType) + 1);
        if (index > type.Limit)
        {
            throw new ArgumentOutOfRangeException(nameof(name),
                $"Script tag type '{type.Name}' exceeds its {type.Limit}-tag limit.");
        }
        _globalNames.Add(qualifiedName);
        var tag = new ScriptTagDefinition(ownerType, index, qualifiedName, valueType, sourceFile);
        _tags.Add(tag);
        _tagsByOwnerAndName.Add((ownerType, qualifiedName), tag);
        _tagCounts[ownerType] = index;
        _revision = checked(_revision + 1);
        return tag;
    }

    public bool TryGetTag(
        ScriptTagTypeId ownerType,
        string name,
        out ScriptTagDefinition? tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var qualifiedName = name.StartsWith("Tag.", StringComparison.Ordinal) ? name : $"Tag.{name}";
        return _tagsByOwnerAndName.TryGetValue((ownerType, qualifiedName), out tag);
    }

    public ScriptTagCatalog Build() => new(
        _types.Values.OrderBy(static type => type.Id.Value),
        _valueTypes.Order(StringComparer.Ordinal),
        _tags);
}

public sealed record ScriptValueEntry(string TagName, string ValueType, int Value);

public sealed class ScriptValueState
{
    private readonly ScriptTagCatalog _catalog;
    private readonly ScriptTagTypeId _ownerType;
    private readonly int[] _values;

    public ScriptValueState(ScriptTagCatalog catalog, ScriptTagTypeId ownerType)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryGetType(ownerType, out _))
        {
            throw new ArgumentException($"Unknown script tag type {ownerType.Value}.", nameof(ownerType));
        }
        _catalog = catalog;
        _ownerType = ownerType;
        _values = new int[catalog.Tags.Count(tag => tag.OwnerType == ownerType)];
    }

    public int Get(string tagName) => Resolve(tagName) is { } tag ? _values[tag.Index - 1] : 0;

    public void Set(string tagName, int value)
    {
        var tag = Resolve(tagName) ??
            throw new ArgumentException($"Unknown script tag '{tagName}'.", nameof(tagName));
        _values[tag.Index - 1] = value;
    }

    public IReadOnlyList<ScriptValueEntry> Capture() => _catalog.Tags
        .Where(tag => tag.OwnerType == _ownerType && _values[tag.Index - 1] != 0)
        .Select(tag => new ScriptValueEntry(tag.Name["Tag.".Length..], tag.ValueType, _values[tag.Index - 1]))
        .ToArray();

    public static bool TryRestore(
        ScriptTagCatalog catalog,
        ScriptTagTypeId ownerType,
        IEnumerable<ScriptValueEntry> entries,
        out ScriptValueState? state,
        out IReadOnlyList<DiagnosticEvent> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(entries);
        var candidate = new ScriptValueState(catalog, ownerType);
        var events = new List<DiagnosticEvent>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var qualifiedName = entry.TagName.StartsWith("Tag.", StringComparison.Ordinal)
                ? entry.TagName
                : $"Tag.{entry.TagName}";
            if (!names.Add(qualifiedName) ||
                !catalog.TryGetTag(ownerType, qualifiedName, out var tag) ||
                !string.Equals(tag?.ValueType, entry.ValueType, StringComparison.Ordinal))
            {
                events.Add(new DiagnosticEvent(
                    ScriptDiagnosticCodes.InvalidScriptValueState,
                    DiagnosticSeverity.Error,
                    $"Script value '{entry.TagName}' is duplicate, unknown, or has an incompatible value type."));
                continue;
            }
            candidate._values[tag!.Index - 1] = entry.Value;
        }
        diagnostics = events.AsReadOnly();
        state = events.Count == 0 ? candidate : null;
        return state is not null;
    }

    private ScriptTagDefinition? Resolve(string tagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        var qualifiedName = tagName.StartsWith("Tag.", StringComparison.Ordinal) ? tagName : $"Tag.{tagName}";
        return _catalog.TryGetTag(_ownerType, qualifiedName, out var tag) ? tag : null;
    }
}
