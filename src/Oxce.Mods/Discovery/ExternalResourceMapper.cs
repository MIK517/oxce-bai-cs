using Oxce.Mods.Files;

namespace Oxce.Mods.Discovery;

internal static class ExternalResourceMapper
{
    public static bool TryMap(
        ModCandidate candidate,
        IReadOnlyList<string> roots,
        ModDiscoveryOptions options,
        out ModCandidate? mapped,
        out string? missingResource)
    {
        var layers = new List<VirtualFileLayer>();
        for (var index = 0; index < candidate.Metadata.ExternalResourceDirectories.Count; ++index)
        {
            var resource = candidate.Metadata.ExternalResourceDirectories[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            _ = VirtualPath.NormalizeDirectory(resource);
            var resourceLayers = new List<VirtualFileLayer>();

            var archivePath = roots.Select(root => Path.Combine(root, resource + ".zip")).FirstOrDefault(File.Exists);
            if (archivePath is not null)
            {
                var archive = ZipArchiveIndex.Read(archivePath, options.ArchiveScan);
                var prefix = archive.ContainsPrefix(resource) ? resource : string.Empty;
                var provenance = new VirtualFileProvenance(
                    $"{candidate.Metadata.Id}:external:{index}:zip",
                    candidate.Metadata.Id,
                    archivePath);
                var layer = archive.CreateLayer(prefix, provenance, ignoreRulesets: true);
                if (prefix.Length != 0 && layer.Entries.Count() <= 1)
                {
                    layer = archive.CreateLayer(string.Empty, provenance, ignoreRulesets: true);
                }

                if (layer.Entries.Count() > 1)
                {
                    resourceLayers.Add(layer);
                }
            }

            var directoryPath = roots.Select(root => Path.Combine(root, resource)).FirstOrDefault(Directory.Exists);
            if (directoryPath is not null)
            {
                var scan = options.LayerScan;
                var layer = VirtualFileLayer.ScanDirectory(
                    directoryPath,
                    $"{candidate.Metadata.Id}:external:{index}:directory",
                    candidate.Metadata.Id,
                    new DirectoryScanOptions
                    {
                        MaximumFiles = scan.MaximumFiles,
                        MaximumDepth = scan.MaximumDepth,
                        MaximumRelativePathLength = scan.MaximumRelativePathLength,
                        IgnoreRulesets = true,
                    });
                if (layer.Entries.Count() > 1)
                {
                    resourceLayers.Add(layer);
                }
            }

            if (resourceLayers.Count == 0)
            {
                mapped = null;
                missingResource = resource;
                return false;
            }

            // Each reference push_front makes earlier metadata entries higher priority.
            layers.InsertRange(0, resourceLayers);
        }

        if (candidate.Metadata.ExternalResourceDirectories.Count != 0)
        {
            var commonLayers = new List<VirtualFileLayer>();
            var commonArchive = roots.Select(root => Path.Combine(root, "common.zip")).FirstOrDefault(File.Exists);
            if (commonArchive is not null)
            {
                var archive = ZipArchiveIndex.Read(commonArchive, options.ArchiveScan);
                var prefix = archive.ContainsPrefix("common") ? "common" : string.Empty;
                var layer = archive.CreateLayer(
                    prefix,
                    new VirtualFileProvenance($"{candidate.Metadata.Id}:common:zip", candidate.Metadata.Id,
                        commonArchive),
                    ignoreRulesets: true);
                if (layer.Entries.Any()) commonLayers.Add(layer);
            }
            var commonDirectory = roots.Select(root => Path.Combine(root, "common")).FirstOrDefault(Directory.Exists);
            if (commonDirectory is not null)
            {
                var scan = options.LayerScan;
                commonLayers.Add(VirtualFileLayer.ScanDirectory(
                    commonDirectory,
                    $"{candidate.Metadata.Id}:common:directory",
                    candidate.Metadata.Id,
                    new DirectoryScanOptions
                    {
                        MaximumFiles = scan.MaximumFiles,
                        MaximumDepth = scan.MaximumDepth,
                        MaximumRelativePathLength = scan.MaximumRelativePathLength,
                        IgnoreRulesets = true,
                    }));
            }
            layers.InsertRange(0, commonLayers);
        }

        layers.AddRange(candidate.Layers);
        mapped = layers.Count == candidate.Layers.Count ? candidate : new ModCandidate(candidate.Metadata, layers);
        missingResource = null;
        return true;
    }
}
