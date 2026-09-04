using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Threading;
using Oxce.Mods.Files;
using Oxce.Mods.Rulesets;

namespace Oxce.Mods.Resources;

public readonly record struct ContentGenerationId
{
    public ContentGenerationId(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public long Value { get; }

    internal static ContentGenerationId Next() => new(Interlocked.Increment(ref _next));

    private static long _next;
}

public enum ResourceKind
{
    Binary,
    IndexedImage,
    Sprite,
    Sound,
    Palette,
    Font,
    Terrain,
    Music,
    Video,
}

public enum ResourceLoadPolicy
{
    Cache,
    Preload,
    Stream,
}

public readonly record struct ResourceHandle
{
    public ResourceHandle(ContentGenerationId generation, int index, ResourceKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Generation = generation;
        Index = index;
        Kind = kind;
    }

    public ContentGenerationId Generation { get; }
    public int Index { get; }
    public ResourceKind Kind { get; }
}

public sealed record ResolvedResourceDescriptor(
    ResourceHandle Handle,
    string Id,
    ResourceKind Kind,
    string CanonicalPath,
    string SourcePath,
    VirtualFileProvenance Provenance,
    ResourceLoadPolicy LoadPolicy,
    int? RuntimeIndex,
    int Width,
    int Height,
    string OwnerSection,
    string OwnerId);

public sealed record ResolvedResourceIndex(
    ResourceKind Kind,
    string SetId,
    string ModId,
    int DeclaredIndex,
    int RuntimeIndex,
    ResourceHandle Handle);

public sealed class ResolvedResourceCatalog
{
    private readonly ResolvedResourceDescriptor[] _descriptors;
    private readonly ResolvedResourceIndex[] _indexEntries;
    private readonly ReadOnlyDictionary<string, ResourceHandle> _handlesById;
    private readonly FrozenDictionary<(ResourceKind Kind, string SetId, string ModId, int DeclaredIndex), ResolvedResourceIndex>
        _indexes;

    internal ResolvedResourceCatalog(
        ContentGenerationId generation,
        IEnumerable<ResolvedResourceDescriptor> descriptors,
        IEnumerable<ResolvedResourceIndex>? indexes = null)
    {
        Generation = generation;
        _descriptors = descriptors.ToArray();
        if (_descriptors.Select(static descriptor => descriptor.Id).Distinct(StringComparer.Ordinal).Count() !=
            _descriptors.Length)
        {
            throw new ArgumentException("Resolved resource descriptor IDs must be unique.", nameof(descriptors));
        }

        for (var index = 0; index < _descriptors.Length; index++)
        {
            var descriptor = _descriptors[index];
            if (descriptor.Handle.Generation != generation || descriptor.Handle.Index != index ||
                descriptor.Handle.Kind != descriptor.Kind)
            {
                throw new ArgumentException("Resolved resource handles must be dense and generation-scoped.",
                    nameof(descriptors));
            }
        }

        _handlesById = new ReadOnlyDictionary<string, ResourceHandle>(
            _descriptors.ToDictionary(static descriptor => descriptor.Id, static descriptor => descriptor.Handle,
                StringComparer.Ordinal));
        _indexEntries = (indexes ?? []).ToArray();
        _indexes = _indexEntries.ToFrozenDictionary(
            static index => (index.Kind, index.SetId, index.ModId, index.DeclaredIndex));
    }

    public ContentGenerationId Generation { get; }
    public IReadOnlyList<ResolvedResourceDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ResolvedResourceIndex> Indexes => _indexEntries;

    public ResolvedResourceDescriptor this[ResourceHandle handle]
    {
        get
        {
            Validate(handle);
            return _descriptors[handle.Index];
        }
    }

    public bool TryGet(string id, out ResourceHandle handle) => _handlesById.TryGetValue(id, out handle);

    public bool TryResolveIndex(
        ResourceKind kind,
        string setId,
        string modId,
        int declaredIndex,
        out ResolvedResourceIndex? resolved) =>
        _indexes.TryGetValue((kind, setId, modId, declaredIndex), out resolved);

    public ResourceHandle GetRequired(string id) => TryGet(id, out var handle)
        ? handle
        : throw new KeyNotFoundException($"Resolved resource '{id}' was not found.");

    public void Validate(ResourceHandle handle)
    {
        if (handle.Generation != Generation)
        {
            throw new InvalidOperationException(
                $"Resource handle belongs to content generation {handle.Generation.Value}, not {Generation.Value}.");
        }
        if ((uint)handle.Index >= (uint)_descriptors.Length || _descriptors[handle.Index].Kind != handle.Kind)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "Resource handle is outside its descriptor catalog.");
        }
    }

    public static ResolvedResourceCatalog FromPaths(
        VirtualFileCatalog files,
        IEnumerable<(string Id, string Path, ResourceKind Kind, ResourceLoadPolicy Policy)> resources)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(resources);
        var generation = ContentGenerationId.Next();
        var descriptors = resources.Select((resource, index) =>
        {
            var entry = files.GetRequired(resource.Path);
            return new ResolvedResourceDescriptor(
                new ResourceHandle(generation, index, resource.Kind),
                resource.Id,
                resource.Kind,
                entry.CanonicalPath,
                entry.SourcePath,
                entry.Provenance,
                resource.Policy,
                null,
                0,
                0,
                "direct",
                resource.Id);
        });
        return new ResolvedResourceCatalog(generation, descriptors);
    }
}

public sealed record ResourceResolutionOptions
{
    public const int DefaultMaximumDescriptors = 250_000;
    public const int DefaultMaximumDimension = 32_768;

    public int MaximumDescriptors { get; init; } = DefaultMaximumDescriptors;
    public int MaximumDimension { get; init; } = DefaultMaximumDimension;
    public IReadOnlyDictionary<string, int> SharedSpriteCounts { get; init; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal));
    public IReadOnlyDictionary<string, int> SharedSoundCounts { get; init; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal));
    public CancellationToken CancellationToken { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDescriptors);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDimension);
        if (SharedSpriteCounts.Any(static pair => pair.Value < 0) ||
            SharedSoundCounts.Any(static pair => pair.Value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(SharedSpriteCounts), "Shared resource counts cannot be negative.");
        }
    }
}

public sealed record ResourceResolutionResult(
    ResolvedResourceCatalog Catalog,
    IReadOnlyList<ResolvedResourceIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public sealed record ResolvedResourceIssue(
    string Code,
    string Message,
    string OwnerSection,
    string OwnerId,
    string Path,
    RuleOperationSource Source);
