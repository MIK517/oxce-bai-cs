using System.Collections.ObjectModel;

namespace Oxce.Mods.Files;

public sealed class VirtualFileLayer
{
    private readonly ReadOnlyDictionary<string, VirtualFileEntry> _resources;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<string>> _directories;

    private VirtualFileLayer(
        VirtualFileProvenance provenance,
        Dictionary<string, VirtualFileEntry> resources,
        List<VirtualFileEntry> rulesets,
        Dictionary<string, IReadOnlyList<string>> directories)
    {
        Provenance = provenance;
        _resources = new ReadOnlyDictionary<string, VirtualFileEntry>(resources);
        Rulesets = rulesets.AsReadOnly();
        _directories = new ReadOnlyDictionary<string, IReadOnlyList<string>>(directories);
    }

    public VirtualFileProvenance Provenance { get; }

    public IReadOnlyList<VirtualFileEntry> Rulesets { get; }

    internal IEnumerable<VirtualFileEntry> Entries => _resources.Values;

    public static VirtualFileLayer FromEntries(
        VirtualFileProvenance provenance,
        IEnumerable<VirtualFileSource> sources,
        bool ignoreRulesets = false)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(sources);
        var resources = new Dictionary<string, VirtualFileEntry>(StringComparer.Ordinal);
        var rulesets = new List<VirtualFileEntry>();

        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            var canonicalPath = VirtualPath.NormalizeFile(source.RelativePath);
            var entry = new VirtualFileEntry(canonicalPath, source.SourcePath, provenance);
            if (IsRuleset(canonicalPath))
            {
                if (!ignoreRulesets)
                {
                    rulesets.Add(entry);
                }

                continue;
            }

            resources[canonicalPath] = entry;
        }

        var directorySets = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var canonicalPath in resources.Keys)
        {
            var separator = canonicalPath.LastIndexOf('/');
            var directory = separator < 0 ? string.Empty : canonicalPath[..separator];
            var basename = canonicalPath[(separator + 1)..];
            if (!directorySets.TryGetValue(directory, out var names))
            {
                names = new SortedSet<string>(StringComparer.Ordinal);
                directorySets.Add(directory, names);
            }

            names.Add(basename);
        }

        var directories = directorySets.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);
        return new VirtualFileLayer(provenance, resources, rulesets, directories);
    }

    public static VirtualFileLayer ScanDirectory(
        string directory,
        string layerId,
        string? modId = null,
        DirectoryScanOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        options ??= new DirectoryScanOptions();
        options.Validate();
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Virtual file layer directory '{root}' was not found.");
        }

        var pending = new Stack<(string Path, int Depth)>();
        var files = new List<VirtualFileSource>();
        pending.Push((root, 0));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            var children = Directory.EnumerateFileSystemEntries(current.Path)
                .Order(StringComparer.Ordinal)
                .ToArray();
            for (var index = children.Length - 1; index >= 0; --index)
            {
                var child = children[index];
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (current.Depth >= options.MaximumDepth)
                    {
                        throw new InvalidDataException(
                            $"Virtual file layer '{root}' exceeds the {options.MaximumDepth}-level directory limit.");
                    }

                    pending.Push((child, current.Depth + 1));
                    continue;
                }

                var relativePath = Path.GetRelativePath(root, child);
                if (relativePath.Length > options.MaximumRelativePathLength)
                {
                    throw new InvalidDataException(
                        $"Virtual file path exceeds the {options.MaximumRelativePathLength}-character limit.");
                }

                files.Add(new VirtualFileSource(relativePath, Path.GetFullPath(child)));
                if (files.Count > options.MaximumFiles)
                {
                    throw new InvalidDataException(
                        $"Virtual file layer '{root}' exceeds the {options.MaximumFiles}-file limit.");
                }
            }
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return FromEntries(
            new VirtualFileProvenance(layerId, modId, root),
            files,
            options.IgnoreRulesets);
    }

    public bool TryGet(string relativePath, out VirtualFileEntry? entry) =>
        _resources.TryGetValue(VirtualPath.NormalizeFile(relativePath), out entry);

    public IReadOnlyList<string> List(string relativeDirectory)
    {
        var canonicalDirectory = VirtualPath.NormalizeDirectory(relativeDirectory);
        return _directories.TryGetValue(canonicalDirectory, out var names) ? names : Array.Empty<string>();
    }

    private static bool IsRuleset(string canonicalPath) =>
        canonicalPath.EndsWith(".rul", StringComparison.Ordinal);
}
