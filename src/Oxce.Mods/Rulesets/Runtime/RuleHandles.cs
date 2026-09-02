using System.Collections;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Oxce.Mods.Resources;

namespace Oxce.Mods.Rulesets.Runtime;

public readonly struct RuleHandle<TFamily> : IEquatable<RuleHandle<TFamily>>
{
    internal RuleHandle(ContentGenerationId generation, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Generation = generation;
        Index = index;
    }

    public ContentGenerationId Generation { get; }

    internal int Index { get; }

    public bool Equals(RuleHandle<TFamily> other) => Generation == other.Generation && Index == other.Index;

    public override bool Equals(object? obj) => obj is RuleHandle<TFamily> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Generation, Index);

    public static bool operator ==(RuleHandle<TFamily> left, RuleHandle<TFamily> right) => left.Equals(right);

    public static bool operator !=(RuleHandle<TFamily> left, RuleHandle<TFamily> right) => !left.Equals(right);

    public override string ToString() => $"{typeof(TFamily).Name}@{Generation.Value}";
}

public sealed class RuleHandleList<TFamily> : IReadOnlyList<RuleHandle<TFamily>>
{
    private readonly RuleHandle<TFamily>[] _handles;

    internal RuleHandleList(IEnumerable<RuleHandle<TFamily>> handles) => _handles = handles.ToArray();

    public int Count => _handles.Length;

    public RuleHandle<TFamily> this[int index] => _handles[index];

    public ReadOnlySpan<RuleHandle<TFamily>> AsSpan() => _handles;

    public IEnumerator<RuleHandle<TFamily>> GetEnumerator() =>
        ((IEnumerable<RuleHandle<TFamily>>)_handles).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _handles.GetEnumerator();
}

public sealed record RuntimeRuleReference<TFamily>(string Id, RuleHandle<TFamily>? Handle)
{
    public bool IsResolved => Handle.HasValue;
}

public sealed class RuntimeRuleReferenceList<TFamily> : IReadOnlyList<RuntimeRuleReference<TFamily>>
{
    private readonly RuntimeRuleReference<TFamily>[] _references;

    internal RuntimeRuleReferenceList(IEnumerable<RuntimeRuleReference<TFamily>> references) =>
        _references = references.ToArray();

    public int Count => _references.Length;

    public RuntimeRuleReference<TFamily> this[int index] => _references[index];

    public IEnumerator<RuntimeRuleReference<TFamily>> GetEnumerator() =>
        ((IEnumerable<RuntimeRuleReference<TFamily>>)_references).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _references.GetEnumerator();
}

public sealed class RuleHandleScratch<TFamily> : IDisposable
{
    private RuleHandle<TFamily>[]? _buffer;

    public RuleHandleScratch(int initialCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _buffer = ArrayPool<RuleHandle<TFamily>>.Shared.Rent(initialCapacity);
    }

    public int Count { get; private set; }

    public void Add(RuleHandle<TFamily> handle)
    {
        var buffer = Buffer;
        if (Count == buffer.Length)
        {
            var replacement = ArrayPool<RuleHandle<TFamily>>.Shared.Rent(checked(buffer.Length * 2));
            buffer.AsSpan(0, Count).CopyTo(replacement);
            ArrayPool<RuleHandle<TFamily>>.Shared.Return(buffer);
            _buffer = replacement;
            buffer = replacement;
        }

        buffer[Count++] = handle;
    }

    public ReadOnlySpan<RuleHandle<TFamily>> AsSpan() => Buffer.AsSpan(0, Count);

    public void Clear() => Count = 0;

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null) return;
        _buffer = null;
        Count = 0;
        ArrayPool<RuleHandle<TFamily>>.Shared.Return(buffer);
    }

    private RuleHandle<TFamily>[] Buffer => _buffer ??
        throw new ObjectDisposedException(nameof(RuleHandleScratch<TFamily>));
}

public sealed record RuntimeRule<TProjection>(
    string Id,
    TProjection Value,
    RuleOperationSource CreationSource,
    RuleOperationSource LastUpdateSource)
    where TProjection : notnull;

public sealed class RuntimeRuleFamily<TFamily, TProjection>
    where TProjection : notnull
{
    private readonly RuntimeRule<TProjection>[] _rules;
    private readonly ReadOnlyCollection<RuntimeRule<TProjection>> _view;
    private readonly FrozenDictionary<string, int> _indexesById;

    internal RuntimeRuleFamily(ContentGenerationId generation, IEnumerable<RuntimeRule<TProjection>> rules)
    {
        Generation = generation;
        _rules = rules.ToArray();
        _view = Array.AsReadOnly(_rules);
        _indexesById = _rules.Select(static (rule, index) => (rule.Id, index))
            .ToFrozenDictionary(static pair => pair.Id, static pair => pair.index, StringComparer.Ordinal);
        if (_indexesById.Count != _rules.Length)
        {
            throw new ArgumentException("Runtime rule IDs must be unique within a family.", nameof(rules));
        }
    }

    public ContentGenerationId Generation { get; }

    public int Count => _rules.Length;

    public IReadOnlyList<RuntimeRule<TProjection>> Rules => _view;

    public RuntimeRule<TProjection> this[RuleHandle<TFamily> handle]
    {
        get
        {
            Validate(handle);
            return _rules[handle.Index];
        }
    }

    public bool TryGet(string id, out RuleHandle<TFamily> handle)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_indexesById.TryGetValue(id, out var index))
        {
            handle = new RuleHandle<TFamily>(Generation, index);
            return true;
        }

        handle = default;
        return false;
    }

    public RuleHandle<TFamily> GetRequired(string id) => TryGet(id, out var handle)
        ? handle
        : throw new KeyNotFoundException($"Runtime rule '{id}' was not found in family '{typeof(TFamily).Name}'.");

    public string GetExternalId(RuleHandle<TFamily> handle) => this[handle].Id;

    public void Validate(RuleHandle<TFamily> handle)
    {
        if (handle.Generation != Generation)
        {
            throw new InvalidOperationException(
                $"Rule handle belongs to content generation {handle.Generation.Value}, not {Generation.Value}.");
        }

        if ((uint)handle.Index >= (uint)_rules.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "Rule handle is outside its runtime family.");
        }
    }
}

public readonly struct CountryRuleFamily;
public readonly struct RegionRuleFamily;
public readonly struct FacilityRuleFamily;
public readonly struct CraftRuleFamily;
public readonly struct CraftWeaponRuleFamily;
public readonly struct ItemRuleFamily;
public readonly struct SoldierRuleFamily;
public readonly struct ArmorRuleFamily;
public readonly struct SkillRuleFamily;
public readonly struct ResearchRuleFamily;
public readonly struct EventRuleFamily;
