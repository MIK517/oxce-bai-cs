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

        var directories = Directory.EnumerateDirectories(root)
            .Where(static path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (directories.Length > options.MaximumMods)
        {
            throw new InvalidDataException($"Mod directory '{root}' exceeds the {options.MaximumMods}-mod limit.");
        }

        var mods = new List<ModCandidate>(directories.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;
        foreach (var directory in directories)
        {
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
                    Path.Combine(directory, "metadata.yml"),
                    directory,
                    diagnostics,
                    options.MetadataYaml);
                if (!ids.Add(metadata.Id))
                {
                    Report(
                        diagnostics,
                        ModDiagnosticCodes.DuplicateId,
                        DiagnosticSeverity.Error,
                        $"Mod ID '{metadata.Id}' is already mapped; skipping '{fallbackId}'.",
                        metadataPath,
                        metadata.Id);
                    ++rejected;
                    continue;
                }

                var layer = VirtualFileLayer.ScanDirectory(
                    directory,
                    fallbackId,
                    metadata.Id,
                    options.LayerScan);
                mods.Add(new ModCandidate(metadata, layer));
            }
            catch (Exception exception) when (exception is YamlFormatException or IOException or UnauthorizedAccessException)
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

        return new ModDiscoveryResult(mods, rejected);
    }

    private static string? FindMetadataPath(string directory) => Directory.EnumerateFiles(directory)
        .Where(path => string.Equals(Path.GetFileName(path), "metadata.yml", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .LastOrDefault();

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
