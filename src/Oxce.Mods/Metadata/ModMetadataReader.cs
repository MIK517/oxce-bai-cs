using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Metadata;

public static class ModMetadataReader
{
    public static ModMetadata ReadFile(
        string metadataPath,
        string modPath,
        IDiagnosticSink? diagnostics = null,
        YamlReadOptions? yamlOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modPath);
        diagnostics ??= NullDiagnosticSink.Instance;
        var documents = YamlCompatibilityReader.ParseFile(metadataPath, yamlOptions);
        YamlCompatibilityReader.ReportLegacyEncoding(documents, diagnostics);
        if (documents.Documents.Count == 0 || documents.Documents[0].Root is not YamlMappingNode mapping)
        {
            SourceSpan? source = documents.Documents.Count == 0 ? null : documents.Documents[0].Root.Span;
            throw new YamlFormatException(
                "Mod metadata root must be a mapping.",
                source ?? new SourceSpan(metadataPath, new SourcePosition(1, 1, 0), new SourcePosition(1, 1, 0)));
        }

        return Read(mapping, modPath, diagnostics);
    }

    public static ModMetadata Read(
        Stream metadata,
        string sourceName,
        string modPath,
        IDiagnosticSink? diagnostics = null,
        YamlReadOptions? yamlOptions = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modPath);
        diagnostics ??= NullDiagnosticSink.Instance;
        var documents = YamlCompatibilityReader.Parse(metadata, sourceName, yamlOptions);
        YamlCompatibilityReader.ReportLegacyEncoding(documents, diagnostics);
        if (documents.Documents.Count == 0 || documents.Documents[0].Root is not YamlMappingNode mapping)
        {
            SourceSpan? source = documents.Documents.Count == 0 ? null : documents.Documents[0].Root.Span;
            throw new YamlFormatException(
                "Mod metadata root must be a mapping.",
                source ?? new SourceSpan(sourceName, new SourcePosition(1, 1, 0), new SourcePosition(1, 1, 0)));
        }

        return Read(mapping, modPath, diagnostics);
    }

    public static ModMetadata Read(YamlMappingNode mapping, string modPath, IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentException.ThrowIfNullOrWhiteSpace(modPath);
        diagnostics ??= NullDiagnosticSink.Instance;
        var fallbackName = Path.GetFileName(Path.TrimEndingDirectorySeparator(modPath));
        var id = mapping.ReadString("id", fallbackName);
        var isMaster = ReadBoolean(mapping, "isMaster", false);
        var master = isMaster ? string.Empty : "xcom1";
        master = mapping.ReadString("master", master);
        if (master == "*")
        {
            master = string.Empty;
        }

        var version = ParseVersion(mapping.ReadString("version", "1.0"), id, mapping, "version", diagnostics);
        ModVersion? requiredMasterVersion = null;
        if (mapping.TryGet("requiredMasterModVersion", out var requiredNode))
        {
            if (master.Length == 0)
            {
                Report(
                    diagnostics,
                    ModDiagnosticCodes.RequiredVersionWithoutMaster,
                    DiagnosticSeverity.Warning,
                    $"Mod '{id}' without a master cannot require a master version.",
                    requiredNode!.Span,
                    id);
            }
            else
            {
                requiredMasterVersion = ParseVersion(
                    YamlValueReader.ReadString(requiredNode!), id, mapping, "requiredMasterModVersion", diagnostics);
            }
        }

        var requiredExtendedVersion = mapping.ReadString("requiredExtendedVersion", string.Empty);
        var requiredExtendedEngine = requiredExtendedVersion.Length == 0 ? string.Empty : "Extended";
        requiredExtendedEngine = mapping.ReadString("requiredExtendedEngine", requiredExtendedEngine);
        var externalResources = Array.Empty<string>();
        if (isMaster && master.Length == 0 && mapping.TryGet("loadResources", out var resourcesNode))
        {
            externalResources = YamlValueReader.ReadSequence(resourcesNode!, YamlValueReader.ReadString).ToArray();
        }

        return new ModMetadata
        {
            Path = Path.GetFullPath(modPath),
            Id = id,
            Name = mapping.ReadString("name", fallbackName),
            Description = mapping.ReadString("description", "No description."),
            Author = mapping.ReadString("author", "unknown author"),
            MasterId = master,
            Version = version,
            VersionDisplay = mapping.ReadString("versionDisplay", version.Text),
            IsMaster = isMaster,
            ReservedSpace = Math.Clamp(ReadInt32(mapping, "reservedSpace", 1), 1, 100),
            RequiredExtendedEngine = requiredExtendedEngine,
            RequiredExtendedVersion = requiredExtendedVersion,
            ResourceConfigFile = mapping.ReadString("resourceConfig", string.Empty),
            ExternalResourceDirectories = Array.AsReadOnly(externalResources),
            RequiredMasterVersion = requiredMasterVersion,
        };
    }

    private static ModVersion ParseVersion(
        string text,
        string modId,
        YamlMappingNode mapping,
        string key,
        IDiagnosticSink diagnostics)
    {
        var version = ModVersion.Parse(text);
        if (!version.IsValid)
        {
            mapping.TryGet(key, out var node);
            Report(
                diagnostics,
                ModDiagnosticCodes.InvalidVersion,
                DiagnosticSeverity.Warning,
                $"Invalid version number in mod '{modId}': {version.Error}.",
                node?.Span ?? mapping.Span,
                modId);
        }

        return version;
    }

    private static bool ReadBoolean(YamlMappingNode mapping, string key, bool defaultValue) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : defaultValue;

    private static int ReadInt32(YamlMappingNode mapping, string key, int defaultValue) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : defaultValue;

    private static void Report(
        IDiagnosticSink diagnostics,
        string code,
        DiagnosticSeverity severity,
        string message,
        SourceSpan source,
        string modId) => diagnostics.Report(new DiagnosticEvent(
            code,
            severity,
            message,
            source,
            new DiagnosticContext(ModId: modId)));
}
