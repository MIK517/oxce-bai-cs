using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;
using Oxce.Mods.Metadata;

namespace Oxce.Mods.Discovery;

public static class ModDiscovery
{
    public static ModDiscoveryResult ScanDirectory(
        string modsDirectory,
        IDiagnosticSink? diagnostics = null,
        ModDiscoveryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        diagnostics ??= NullDiagnosticSink.Instance;
        options ??= new ModDiscoveryOptions();
        options.Validate();
        var root = Path.GetFullPath(modsDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Mod directory '{root}' was not found.");
        }

        var archives = Directory.EnumerateFiles(root)
            .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (archives.Length > options.MaximumArchives)
        {
            throw new InvalidDataException(
                $"Mod directory '{root}' exceeds the {options.MaximumArchives}-archive limit.");
        }

        var directories = Directory.EnumerateDirectories(root)
            .Where(static path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var mods = new List<ModCandidate>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;

        // The reference scans archives before directories, so a duplicate directory mod cannot
        // replace an already-discovered ZIP mod with the same ID.
        foreach (var archivePath in archives)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                var archive = ZipArchiveIndex.Read(archivePath, options.ArchiveScan);
                if (archive.RejectedEntryCount != 0)
                {
                    Report(
                        diagnostics,
                        ModDiagnosticCodes.UnsafeArchiveEntry,
                        DiagnosticSeverity.Warning,
                        $"Ignored {archive.RejectedEntryCount} unsafe or unsupported path(s) in ZIP archive '{Path.GetFileName(archivePath)}'.",
                        archivePath,
                        Path.GetFileNameWithoutExtension(archivePath));
                }

                var prefixes = FindModPrefixes(archive);
                if (prefixes.Count == 0)
                {
                    Report(
                        diagnostics,
                        ModDiagnosticCodes.MissingMetadata,
                        DiagnosticSeverity.Warning,
                        $"No top-level metadata.yml was found in ZIP archive '{Path.GetFileName(archivePath)}'.",
                        archivePath,
                        Path.GetFileNameWithoutExtension(archivePath));
                    ++rejected;
                    continue;
                }

                foreach (var prefix in prefixes)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var candidate = ReadArchiveCandidate(archive, prefix, diagnostics, options);
                        if (!TryAdd(candidate, ids, mods, diagnostics))
                        {
                            ++rejected;
                        }
                    }
                    catch (Exception exception) when (IsDiscoveryFailure(exception))
                    {
                        var modName = prefix.Length == 0 ? Path.GetFileName(archivePath) : prefix;
                        Report(
                            diagnostics,
                            ModDiagnosticCodes.InvalidMetadata,
                            DiagnosticSeverity.Error,
                            $"Could not load metadata for ZIP mod '{modName}': {exception.Message}",
                            $"{archivePath}!/{(prefix.Length == 0 ? string.Empty : prefix + "/")}metadata.yml",
                            modName);
                        ++rejected;
                    }
                }
            }
            catch (Exception exception) when (IsDiscoveryFailure(exception))
            {
                Report(
                    diagnostics,
                    ModDiagnosticCodes.InvalidArchive,
                    DiagnosticSeverity.Error,
                    $"Could not load mod ZIP '{Path.GetFileName(archivePath)}': {exception.Message}",
                    archivePath,
                    Path.GetFileNameWithoutExtension(archivePath));
                ++rejected;
            }
        }

        foreach (var directory in directories)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var fallbackId = Path.GetFileName(directory);
            var metadataPath = FindMetadataPath(directory);
            if (metadataPath is null)
            {
                Report(
                    diagnostics,
                    ModDiagnosticCodes.MissingMetadata,
                    DiagnosticSeverity.Warning,
                    $"No metadata.yml was found in mod directory '{fallbackId}'.",
                    directory,
                    fallbackId);
                ++rejected;
                continue;
            }

            try
            {
                var metadata = ModMetadataReader.ReadFile(
                    metadataPath,
                    directory,
                    diagnostics,
                    options.MetadataYaml);
                var layer = VirtualFileLayer.ScanDirectory(
                    directory,
                    fallbackId,
                    metadata.Id,
                    WithCancellation(options.LayerScan, options.CancellationToken));
                if (!TryAdd(new ModCandidate(metadata, layer), ids, mods, diagnostics))
                {
                    ++rejected;
                }
            }
            catch (Exception exception) when (IsDiscoveryFailure(exception))
            {
                var source = exception is YamlFormatException yamlException
                    ? yamlException.Span
                    : FileStart(metadataPath);
                diagnostics.Report(new DiagnosticEvent(
                    ModDiagnosticCodes.InvalidMetadata,
                    DiagnosticSeverity.Error,
                    $"Could not load metadata for mod directory '{fallbackId}': {exception.Message}",
                    source,
                    new DiagnosticContext(LayerId: fallbackId, ModId: fallbackId)));
                ++rejected;
            }
        }

        EnsureModLimit(mods.Count, root, options.MaximumMods);

        var resolved = new List<ModCandidate>(mods.Count);
        var resourceRoots = options.ExternalResourceRoots.Select(Path.GetFullPath).ToArray();
        foreach (var candidate in mods)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (ExternalResourceMapper.TryMap(candidate, resourceRoots, options, out var mapped, out var missingResource))
                {
                    resolved.Add(mapped!);
                    continue;
                }

                Report(
                    diagnostics,
                    ModDiagnosticCodes.MissingExternalResource,
                    DiagnosticSeverity.Warning,
                    $"Mod '{candidate.Metadata.Id}' requires external resource '{missingResource}', but no usable directory or ZIP was found.",
                    candidate.Metadata.Path,
                    candidate.Metadata.Id);
                ++rejected;
            }
            catch (Exception exception) when (IsDiscoveryFailure(exception) || exception is ArgumentException)
            {
                Report(
                    diagnostics,
                    ModDiagnosticCodes.MissingExternalResource,
                    DiagnosticSeverity.Error,
                    $"Could not map external resources for mod '{candidate.Metadata.Id}': {exception.Message}",
                    candidate.Metadata.Path,
                    candidate.Metadata.Id);
                ++rejected;
            }
        }

        return new ModDiscoveryResult(resolved, rejected);
    }

    private static DirectoryScanOptions WithCancellation(
        DirectoryScanOptions source,
        CancellationToken cancellationToken) => new()
        {
            MaximumFiles = source.MaximumFiles,
            MaximumDepth = source.MaximumDepth,
            MaximumRelativePathLength = source.MaximumRelativePathLength,
            IgnoreRulesets = source.IgnoreRulesets,
            CancellationToken = cancellationToken,
        };

    private static ModCandidate ReadArchiveCandidate(
        ZipArchiveIndex archive,
        string prefix,
        IDiagnosticSink diagnostics,
        ModDiscoveryOptions options)
    {
        var metadataPath = prefix.Length == 0
            ? "metadata.yml"
            : $"{VirtualPath.NormalizeDirectory(prefix)}/metadata.yml";
        var descriptor = archive.Entries.Last(entry => string.Equals(
            entry.CanonicalPath,
            metadataPath,
            StringComparison.Ordinal));
        var sourcePath = $"{archive.ArchivePath}!/{descriptor.EntryName.Replace('\\', '/')}";
        var metadataSource = VirtualFileSource.FromZip(
            "metadata.yml",
            sourcePath,
            archive.ArchivePath,
            descriptor.Index,
            descriptor.EntryName);
        var fallbackId = prefix.Length == 0
            ? Path.GetFileNameWithoutExtension(archive.ArchivePath)
            : prefix;
        var modPath = prefix.Length == 0
            ? archive.ArchivePath
            : Path.Combine(archive.ArchivePath, prefix);
        using var metadataStream = metadataSource.OpenRead();
        var metadata = ModMetadataReader.Read(
            metadataStream,
            sourcePath,
            modPath,
            diagnostics,
            options.MetadataYaml);
        var layer = archive.CreateLayer(
            prefix,
            new VirtualFileProvenance($"zip:{Path.GetFileName(archive.ArchivePath)}:{fallbackId}", metadata.Id, modPath));
        if (layer.Entries.Count() + layer.Rulesets.Count <= 1)
        {
            throw new InvalidDataException($"ZIP mod '{fallbackId}' does not contain any content beyond metadata.yml.");
        }

        return new ModCandidate(metadata, layer);
    }

    private static List<string> FindModPrefixes(ZipArchiveIndex archive)
    {
        if (archive.Entries.Any(entry => string.Equals(entry.CanonicalPath, "metadata.yml", StringComparison.Ordinal)))
        {
            return [string.Empty];
        }

        var prefixes = new List<string>();
        foreach (var prefix in archive.TopLevelDirectories)
        {
            var canonicalPrefix = VirtualPath.NormalizeDirectory(prefix);
            if (!archive.Entries.Any(entry => string.Equals(
                entry.CanonicalPath,
                $"{canonicalPrefix}/metadata.yml",
                StringComparison.Ordinal)))
            {
                continue;
            }

            prefixes.Add(prefix);
        }

        return prefixes;
    }

    private static bool TryAdd(
        ModCandidate candidate,
        HashSet<string> ids,
        List<ModCandidate> mods,
        IDiagnosticSink diagnostics)
    {
        if (ids.Add(candidate.Metadata.Id))
        {
            mods.Add(candidate);
            return true;
        }

        Report(
            diagnostics,
            ModDiagnosticCodes.DuplicateId,
            DiagnosticSeverity.Error,
            $"Mod ID '{candidate.Metadata.Id}' is already mapped; skipping '{candidate.Metadata.Path}'.",
            candidate.Metadata.Path,
            candidate.Metadata.Id);
        return false;
    }

    private static string? FindMetadataPath(string directory) => Directory.EnumerateFiles(directory)
        .Where(path => string.Equals(Path.GetFileName(path), "metadata.yml", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .LastOrDefault();

    private static void EnsureModLimit(int count, string root, int maximum)
    {
        if (count > maximum)
        {
            throw new InvalidDataException($"Mod directory '{root}' exceeds the {maximum}-mod limit.");
        }
    }

    private static bool IsDiscoveryFailure(Exception exception) =>
        exception is YamlFormatException or InvalidDataException or IOException or UnauthorizedAccessException;

    private static void Report(
        IDiagnosticSink diagnostics,
        string code,
        DiagnosticSeverity severity,
        string message,
        string sourcePath,
        string modId) => diagnostics.Report(new DiagnosticEvent(
            code,
            severity,
            message,
            FileStart(sourcePath),
            new DiagnosticContext(LayerId: modId, ModId: modId)));

    private static SourceSpan FileStart(string path) => new(
        path,
        new SourcePosition(1, 1, 0),
        new SourcePosition(1, 1, 0));
}
