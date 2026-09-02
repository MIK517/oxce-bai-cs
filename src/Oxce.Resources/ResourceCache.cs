using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.Mods.Files;
using Oxce.Mods.Resources;

namespace Oxce.Resources;

public sealed record ResourceCacheOptions
{
    public const long DefaultMaximumBytes = 512L * 1024 * 1024;
    public const long DefaultMaximumEntryBytes = 128L * 1024 * 1024;

    public long MaximumBytes { get; init; } = DefaultMaximumBytes;
    public long MaximumEntryBytes { get; init; } = DefaultMaximumEntryBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEntryBytes);
        if (MaximumEntryBytes > MaximumBytes && MaximumBytes != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumEntryBytes),
                "The maximum cache entry cannot exceed the total cache budget.");
        }
    }
}

public readonly record struct ResourceDecodeResult<T>(T Value, long SizeBytes)
{
    public ResourceDecodeResult<T> Validate()
    {
        ArgumentNullException.ThrowIfNull(Value);
        ArgumentOutOfRangeException.ThrowIfNegative(SizeBytes);
        return this;
    }
}

public readonly record struct ResourceCacheTelemetry(
    long Hits,
    long Misses,
    long Loads,
    long Evictions,
    long RejectedOversizedEntries,
    long CurrentBytes,
    int EntryCount);

public sealed record ResourcePreloadGroup
{
    public ResourcePreloadGroup(string id, IEnumerable<ResourceHandle> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(resources);
        Id = id;
        Resources = Array.AsReadOnly(resources.ToArray());
    }

    public string Id { get; }
    public IReadOnlyList<ResourceHandle> Resources { get; }
}

public sealed class ResourceRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly VirtualFileCatalog _files;
    private readonly ResolvedResourceCatalog _catalog;
    private readonly ResourceCacheOptions _options;
    private readonly Dictionary<CacheKey, CacheEntry> _cache = [];
    private readonly LinkedList<CacheKey> _leastRecentlyUsed = [];
    private long _hits;
    private long _misses;
    private long _loads;
    private long _evictions;
    private long _rejectedOversizedEntries;
    private long _currentBytes;
    private bool _disposed;

    public ResourceRuntime(
        VirtualFileCatalog files,
        ResolvedResourceCatalog catalog,
        ResourceCacheOptions? options = null)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options ?? new ResourceCacheOptions();
        _options.Validate();
    }

    public ContentGenerationId Generation => _catalog.Generation;
    public ResolvedResourceCatalog Catalog => _catalog;

    public ResourceCacheTelemetry Telemetry
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return new ResourceCacheTelemetry(
                    _hits, _misses, _loads, _evictions, _rejectedOversizedEntries, _currentBytes, _cache.Count);
            }
        }
    }

    public ReadOnlyMemory<byte> LoadBytes(ResourceHandle handle) => Load(
        handle,
        "bytes",
        stream =>
        {
            var reader = BinaryDataReader.FromStream(stream, checked((int)Math.Min(_options.MaximumEntryBytes, int.MaxValue)));
            var bytes = reader.ReadMemory(reader.Remaining).ToArray();
            return new ResourceDecodeResult<ReadOnlyMemory<byte>>(bytes, bytes.Length);
        });

    public IndexedImageData LoadIndexedImage(ResourceHandle handle) => Load(
        handle,
        "indexed-image",
        stream =>
        {
            var image = IndexedImageCodec.Decode(BinaryDataReader.FromStream(
                stream,
                checked((int)Math.Min(_options.MaximumEntryBytes, int.MaxValue))));
            var size = checked((long)image.Pixels.Length + image.Palette.Count * 4L);
            return new ResourceDecodeResult<IndexedImageData>(image, size);
        });

    public T Load<T>(
        ResourceHandle handle,
        string variant,
        Func<Stream, ResourceDecodeResult<T>> decoder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        ArgumentNullException.ThrowIfNull(decoder);
        var descriptor = _catalog[handle];
        if (descriptor.LoadPolicy == ResourceLoadPolicy.Stream)
        {
            throw new InvalidOperationException(
                $"Streaming resource '{descriptor.Id}' cannot be retained through the decoded cache.");
        }
        var key = new CacheKey(handle, variant);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached.Value is not T value)
                {
                    throw new InvalidOperationException($"Cached resource variant '{variant}' has an incompatible type.");
                }
                _hits++;
                Touch(cached);
                return value;
            }
            _misses++;
        }

        ResourceDecodeResult<T> decoded;
        using (var stream = OpenDescriptor(descriptor))
        {
            decoded = decoder(stream).Validate();
        }
        if (decoded.SizeBytes > _options.MaximumEntryBytes)
        {
            lock (_gate) _rejectedOversizedEntries++;
            throw new InvalidDataException(
                $"Decoded resource '{descriptor.Id}' is {decoded.SizeBytes} bytes and exceeds the {_options.MaximumEntryBytes}-byte entry limit.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(key, out var winner))
            {
                _hits++;
                Touch(winner);
                return (T)winner.Value;
            }
            _loads++;
            if (_options.MaximumBytes == 0 || decoded.SizeBytes > _options.MaximumBytes)
            {
                return decoded.Value;
            }
            EvictUntilFits(decoded.SizeBytes);
            var node = _leastRecentlyUsed.AddLast(key);
            _cache.Add(key, new CacheEntry(decoded.Value!, decoded.SizeBytes, node));
            _currentBytes += decoded.SizeBytes;
            return decoded.Value;
        }
    }

    public Stream OpenStream(ResourceHandle handle)
    {
        var descriptor = _catalog[handle];
        if (descriptor.LoadPolicy != ResourceLoadPolicy.Stream)
        {
            throw new InvalidOperationException($"Resource '{descriptor.Id}' is not declared for streaming.");
        }
        lock (_gate) ThrowIfDisposed();
        return OpenDescriptor(descriptor);
    }

    public void PreloadBytes(IEnumerable<ResourceHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(handles);
        foreach (var handle in handles)
        {
            if (_catalog[handle].LoadPolicy == ResourceLoadPolicy.Stream)
            {
                throw new InvalidOperationException("Streaming resources cannot belong to decoded preload groups.");
            }
            _ = LoadBytes(handle);
        }
    }

    public void Preload(ResourcePreloadGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        PreloadBytes(group.Resources);
    }

    public void InvalidateGeneration(ContentGenerationId generation)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (generation != Generation) return;
            ClearCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ClearCore();
            _disposed = true;
        }
    }

    private Stream OpenDescriptor(ResolvedResourceDescriptor descriptor)
    {
        if (!_files.TryGet(descriptor.CanonicalPath, out var current) ||
            !string.Equals(current!.SourcePath, descriptor.SourcePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Resource descriptor '{descriptor.Id}' no longer matches the active virtual-file generation.");
        }
        return current.OpenRead();
    }

    private void Touch(CacheEntry entry)
    {
        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddLast(entry.Node);
    }

    private void EvictUntilFits(long size)
    {
        while (_currentBytes > _options.MaximumBytes - size)
        {
            var node = _leastRecentlyUsed.First ?? throw new InvalidOperationException("Cache accounting is inconsistent.");
            var entry = _cache[node.Value];
            _cache.Remove(node.Value);
            _leastRecentlyUsed.RemoveFirst();
            _currentBytes -= entry.SizeBytes;
            _evictions++;
        }
    }

    private void ClearCore()
    {
        _cache.Clear();
        _leastRecentlyUsed.Clear();
        _currentBytes = 0;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct CacheKey(ResourceHandle Handle, string Variant);
    private sealed record CacheEntry(object Value, long SizeBytes, LinkedListNode<CacheKey> Node);
}
