namespace Oxce.Mods.Files;

public sealed class VirtualFileCatalog
{
    private readonly VirtualFileLayer[] _layers;
    private readonly Dictionary<string, VirtualFileEntry> _resources;
    private readonly Dictionary<string, IReadOnlyList<string>> _directories;

    public VirtualFileCatalog(IEnumerable<VirtualFileLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var materialized = layers.ToArray();
        if (materialized.Any(layer => layer is null))
        {
            throw new ArgumentException("Catalog layers cannot contain null values.", nameof(layers));
        }

        var duplicateLayer = materialized
            .GroupBy(layer => layer.Provenance.LayerId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicateLayer is not null)
        {
            throw new ArgumentException($"Catalog layer ID '{duplicateLayer.Key}' is duplicated.", nameof(layers));
        }

        _layers = materialized;
        var resources = new Dictionary<string, VirtualFileEntry>(StringComparer.Ordinal);
        var directorySets = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var rulesets = new List<VirtualFileEntry>();
        foreach (var layer in materialized)
        {
            foreach (var ruleset in layer.Rulesets)
            {
                rulesets.Add(ruleset);
            }

            MergeDirectory(directorySets, string.Empty, layer.List(string.Empty));
            foreach (var entry in layer.Entries)
            {
                resources[entry.CanonicalPath] = entry;
                var separator = entry.CanonicalPath.LastIndexOf('/');
                if (separator >= 0)
                {
                    MergeDirectory(
                        directorySets,
                        entry.CanonicalPath[..separator],
                        [entry.CanonicalPath[(separator + 1)..]]);
                }
            }
        }

        _resources = resources;
        _directories = directorySets.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
        Rulesets = rulesets.AsReadOnly();

    }

    public IReadOnlyList<VirtualFileLayer> Layers => _layers;

    public IReadOnlyList<VirtualFileEntry> Rulesets { get; }

    public bool TryGet(string relativePath, out VirtualFileEntry? entry) =>
        _resources.TryGetValue(VirtualPath.NormalizeFile(relativePath), out entry);

    public VirtualFileEntry GetRequired(string relativePath) =>
        TryGet(relativePath, out var entry)
            ? entry!
            : throw new FileNotFoundException($"Virtual file '{relativePath}' was not found.", relativePath);

    public IReadOnlyList<VirtualFileEntry?> GetSlice(string relativePath)
    {
        var canonicalPath = VirtualPath.NormalizeFile(relativePath);
        var result = new VirtualFileEntry?[_layers.Length];
        for (var index = 0; index < _layers.Length; ++index)
        {
            _layers[index].TryGet(canonicalPath, out result[index]);
        }

        return result;
    }

    public IReadOnlyList<string> List(string relativeDirectory)
    {
        var canonicalDirectory = VirtualPath.NormalizeDirectory(relativeDirectory);
        return _directories.TryGetValue(canonicalDirectory, out var names) ? names : Array.Empty<string>();
    }

    public IReadOnlyList<string> List(string relativeDirectory, int layerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layerIndex, _layers.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(layerIndex);
        return _layers[layerIndex].List(relativeDirectory);
    }

    private static void MergeDirectory(
        Dictionary<string, SortedSet<string>> directories,
        string directory,
        IEnumerable<string> names)
    {
        if (!directories.TryGetValue(directory, out var destination))
        {
            destination = new SortedSet<string>(StringComparer.Ordinal);
            directories.Add(directory, destination);
        }

        destination.UnionWith(names);
    }
}
