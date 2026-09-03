using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oxce.Core.Diagnostics;
using Oxce.Extensions.Abstractions;

namespace Oxce.Extensions;

public sealed class ManagedExtensionLoadOptions
{
    public int MaximumExtensions { get; init; } = 256;
    public long MaximumManifestBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumExtensions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumManifestBytes);
    }
}

public sealed record LoadedManagedExtension(
    ExtensionIdentity Identity,
    string Directory,
    bool IsEnabled);

public sealed class ManagedExtensionHost : IDisposable
{
    private readonly IDiagnosticSink _diagnostics;
    private readonly List<ExtensionInstance> _extensions;
    private readonly Dictionary<string, ExtensionStateRecord> _preservedState = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    private ManagedExtensionHost(IDiagnosticSink diagnostics, List<ExtensionInstance> extensions)
    {
        _diagnostics = diagnostics;
        _extensions = extensions;
    }

    public IReadOnlyList<LoadedManagedExtension> Extensions => _extensions
        .Select(static extension => new LoadedManagedExtension(
            extension.Identity, extension.Directory, extension.IsEnabled))
        .ToArray();

    public static ManagedExtensionHost LoadFromDirectory(
        string rootDirectory,
        IDiagnosticSink diagnostics,
        ManagedExtensionLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(diagnostics);
        options ??= new ManagedExtensionLoadOptions();
        options.Validate();
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) return new ManagedExtensionHost(diagnostics, []);

        var directories = Directory.GetDirectories(root)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (directories.Length > options.MaximumExtensions)
            throw new InvalidDataException(
                $"Extension root contains {directories.Length} directories; the limit is {options.MaximumExtensions}.");

        var loaded = new List<ExtensionInstance>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(directory, "extension.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = ReadManifest(manifestPath, options.MaximumManifestBytes);
                ValidateId(manifest.Id);
                if (!ids.Add(manifest.Id))
                    throw new InvalidDataException($"Duplicate extension ID '{manifest.Id}'.");
                var identity = new ExtensionIdentity(manifest.Id, ParseVersion(manifest.Version, "extension"));
                var range = new ExtensionApiRange(
                    ExtensionApiVersion.Parse(manifest.MinimumApiVersion),
                    ExtensionApiVersion.Parse(manifest.MaximumApiVersionExclusive));
                range.Validate();
                if (!range.Contains(ManagedExtensionApi.Current))
                    throw new InvalidDataException(
                        $"Extension '{manifest.Id}' supports API [{range.Minimum}, {range.MaximumExclusive}), " +
                        $"but this host provides {ManagedExtensionApi.Current}.");
                var assemblyPath = ResolveContainedPath(directory, manifest.EntryAssembly);
                if (!File.Exists(assemblyPath))
                    throw new FileNotFoundException("Extension entry assembly was not found.", assemblyPath);

                var loadContext = new ExtensionAssemblyLoadContext(assemblyPath);
                using var assemblyStream = File.OpenRead(assemblyPath);
                var assembly = loadContext.LoadFromStream(assemblyStream);
                var abstractionsName = typeof(IManagedExtension).Assembly.GetName();
                var forbiddenReference = assembly.GetReferencedAssemblies().FirstOrDefault(reference =>
                    reference.Name?.StartsWith("Oxce.", StringComparison.Ordinal) == true &&
                    !AssemblyName.ReferenceMatchesDefinition(reference, abstractionsName));
                if (forbiddenReference is not null)
                    throw new InvalidDataException(
                        $"Extension '{manifest.Id}' references host implementation assembly " +
                        $"'{forbiddenReference.Name}'. Only Oxce.Extensions.Abstractions may be referenced.");
                var type = assembly.GetType(manifest.EntryType, throwOnError: true, ignoreCase: false)!;
                if (!type.IsClass || type.IsAbstract || !typeof(IManagedExtension).IsAssignableFrom(type))
                    throw new InvalidDataException(
                        $"Entry type '{manifest.EntryType}' does not implement {nameof(IManagedExtension)}.");
                if (type.GetConstructor(Type.EmptyTypes) is null)
                    throw new InvalidDataException($"Entry type '{manifest.EntryType}' has no public parameterless constructor.");
                var instance = (IManagedExtension)Activator.CreateInstance(type)!;
                var extension = new ExtensionInstance(identity, directory, loadContext, instance);
                var context = new ExtensionContext(identity, diagnostics);
                try
                {
                    instance.Initialize(context, cancellationToken);
                    loaded.Add(extension);
                    Report(diagnostics, "EXT0001", DiagnosticSeverity.Information,
                        $"Loaded managed extension '{identity.Id}' version {identity.Version}.", identity.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    extension.Disable();
                    ReportFailure(diagnostics, "EXT1006", "initialize", identity.Id, exception);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedLoadFailure(exception))
            {
                ReportFailure(diagnostics, "EXT1001", "load", Path.GetFileName(directory), exception);
            }
        }
        return new ManagedExtensionHost(diagnostics, loaded);
    }

    public CampaignExtensionSession AttachCampaign(
        Oxce.Gameplay.Campaigns.ICampaignQuery queries,
        Oxce.Gameplay.Campaigns.ICampaignCommandTarget commands,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return new CampaignExtensionSession(this, queries, commands, cancellationToken);
    }

    public ExtensionStateDocument CaptureState()
    {
        ThrowIfDisposed();
        var records = new Dictionary<string, ExtensionStateRecord>(_preservedState, StringComparer.Ordinal);
        foreach (var extension in _extensions.Where(static candidate => candidate.IsEnabled))
        {
            if (extension.Instance is not IManagedExtensionState provider) continue;
            try
            {
                var snapshot = provider.CaptureState();
                var record = new ExtensionStateRecord(
                    extension.Identity.Id,
                    extension.Identity.Version,
                    snapshot.SchemaVersion,
                    snapshot.RequiredForContinuation,
                    snapshot.Data);
                ExtensionStateValidator.Validate(record);
                records[record.ExtensionId] = record;
            }
            catch (Exception exception)
            {
                Disable(extension, "capture state", exception);
                throw new InvalidOperationException(
                    $"Managed extension '{extension.Identity.Id}' could not capture save state.",
                    exception);
            }
        }
        return new ExtensionStateDocument(records.Values);
    }

    public bool RestoreState(
        ExtensionStateDocument document,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        _preservedState.Clear();
        var success = true;
        foreach (var record in document.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtensionStateValidator.Validate(record);
            var extension = _extensions.FirstOrDefault(candidate =>
                candidate.IsEnabled && string.Equals(candidate.Identity.Id, record.ExtensionId, StringComparison.Ordinal));
            if (extension?.Instance is IManagedExtensionState provider)
            {
                try
                {
                    provider.RestoreState(
                        new ExtensionStateSnapshot(
                            record.SchemaVersion, record.RequiredForContinuation, record.Data),
                        cancellationToken);
                    continue;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Disable(extension, "restore state", exception);
                    success = false;
                }
            }

            _preservedState.Add(record.ExtensionId, record);
            if (record.RequiredForContinuation)
            {
                success = false;
                Report(_diagnostics, "EXT1010", DiagnosticSeverity.Error,
                    $"Required state for managed extension '{record.ExtensionId}' could not be restored.",
                    record.ExtensionId);
            }
            else
            {
                Report(_diagnostics, "EXT0010", DiagnosticSeverity.Warning,
                    $"Preserving optional state for unavailable managed extension '{record.ExtensionId}'.",
                    record.ExtensionId);
            }
        }
        return success;
    }

    internal IReadOnlyList<ExtensionInstance> EnabledCampaignExtensions() => _extensions
        .Where(static extension => extension.IsEnabled && extension.Instance is ICampaignExtension)
        .ToArray();

    internal void Disable(ExtensionInstance extension, string operation, Exception exception)
    {
        extension.Disable();
        ReportFailure(_diagnostics, "EXT1007", operation, extension.Identity.Id, exception);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        foreach (var extension in _extensions.AsEnumerable().Reverse())
        {
            try
            {
                extension.Instance.Shutdown(CancellationToken.None);
            }
            catch (Exception exception)
            {
                ReportFailure(_diagnostics, "EXT1008", "shutdown", extension.Identity.Id, exception);
            }
            extension.Disable();
        }
        _lifetime.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static ExtensionManifest ReadManifest(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length > maximumBytes)
            throw new InvalidDataException($"Extension manifest exceeds the {maximumBytes}-byte limit.");
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(stream, ManifestJsonOptions)
            ?? throw new InvalidDataException("Extension manifest is empty.");
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported extension manifest schema {manifest.SchemaVersion}.");
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.EntryAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.EntryType);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.MinimumApiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.MaximumApiVersionExclusive);
        return manifest;
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static Version ParseVersion(string value, string kind)
    {
        if (!Version.TryParse(value, out var version) || version.Major < 0 || version.Minor < 0)
            throw new InvalidDataException($"The {kind} version '{value}' is invalid.");
        return version;
    }

    private static void ValidateId(string id)
    {
        if (id.Length > 128 || id.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new InvalidDataException(
                "Extension IDs must be at most 128 ASCII letters, digits, dots, hyphens, or underscores.");
    }

    private static string ResolveContainedPath(string directory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Extension entry assemblies must use relative paths.");
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(relativePath, directory);
        if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Extension entry assembly escapes its extension directory.");
        return result;
    }

    private static bool IsExpectedLoadFailure(Exception exception) => exception is
        ArgumentException or BadImageFormatException or FileNotFoundException or FileLoadException or
        IOException or InvalidDataException or JsonException or NotSupportedException or
        ReflectionTypeLoadException or TargetInvocationException or TypeLoadException or
        UnauthorizedAccessException;

    private static void ReportFailure(
        IDiagnosticSink diagnostics,
        string code,
        string operation,
        string id,
        Exception exception) => Report(
            diagnostics,
            code,
            DiagnosticSeverity.Error,
            $"Managed extension '{id}' failed to {operation}: {exception.GetType().Name}: {exception.Message}",
            id);

    private static void Report(
        IDiagnosticSink diagnostics,
        string code,
        DiagnosticSeverity severity,
        string message,
        string id) => diagnostics.Report(new DiagnosticEvent(
            code, severity, message, context: new DiagnosticContext(RelatedId: id)));

    internal sealed class ExtensionInstance(
        ExtensionIdentity identity,
        string directory,
        AssemblyLoadContext loadContext,
        IManagedExtension instance)
    {
        public ExtensionIdentity Identity { get; } = identity;
        public string Directory { get; } = directory;
        public AssemblyLoadContext LoadContext { get; } = loadContext;
        public IManagedExtension Instance { get; } = instance;
        public bool IsEnabled { get; private set; } = true;
        public void Disable() => IsEnabled = false;
    }

    private sealed class ExtensionContext(ExtensionIdentity identity, IDiagnosticSink diagnostics)
        : IExtensionContext
    {
        public ExtensionApiVersion ApiVersion => ManagedExtensionApi.Current;
        public ExtensionIdentity Identity { get; } = identity;
        public IExtensionDiagnosticSink Diagnostics { get; } = new ExtensionDiagnosticAdapter(diagnostics, identity.Id);
    }

    private sealed class ExtensionDiagnosticAdapter(IDiagnosticSink destination, string id) : IExtensionDiagnosticSink
    {
        public void Report(ExtensionDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            destination.Report(new DiagnosticEvent(
                diagnostic.Code,
                (DiagnosticSeverity)diagnostic.Severity,
                diagnostic.Message,
                context: new DiagnosticContext(RelatedId: id)));
        }
    }

    private sealed class ExtensionAssemblyLoadContext(string entryAssemblyPath)
        : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(entryAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (AssemblyName.ReferenceMatchesDefinition(
                    assemblyName, typeof(IManagedExtension).Assembly.GetName()))
                return typeof(IManagedExtension).Assembly;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? 0 : LoadUnmanagedDllFromPath(path);
        }
    }

    private sealed class ExtensionManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("version")]
        public string Version { get; init; } = "";

        [JsonPropertyName("entryAssembly")]
        public string EntryAssembly { get; init; } = "";

        [JsonPropertyName("entryType")]
        public string EntryType { get; init; } = "";

        [JsonPropertyName("minimumApiVersion")]
        public string MinimumApiVersion { get; init; } = "";

        [JsonPropertyName("maximumApiVersionExclusive")]
        public string MaximumApiVersionExclusive { get; init; } = "";
    }
}
