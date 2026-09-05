using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Phase3;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Api;
using Oxce.Scripting.Events;
using Oxce.Scripting.Globals;

namespace Oxce.Mods.Bootstrap;

public enum CompiledContentCacheStatus
{
    Disabled,
    Miss,
    Hit,
    Rejected,
}

public sealed record CompiledContentCacheOptions
{
    public const long DefaultMaximumCompressedBytes = 512L * 1024 * 1024;
    public const long DefaultMaximumExpandedBytes = 512L * 1024 * 1024;

    public bool Enabled { get; init; } = true;
    public string? DirectoryPath { get; init; }
    public long MaximumCompressedBytes { get; init; } = DefaultMaximumCompressedBytes;
    public long MaximumExpandedBytes { get; init; } = DefaultMaximumExpandedBytes;

    internal void Validate()
    {
        if (DirectoryPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(DirectoryPath);
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumExpandedBytes);
    }

    internal CompiledContentCacheOptions Resolve(string installationRoot) => this with
    {
        DirectoryPath = Enabled
            ? Path.GetFullPath(DirectoryPath ??
                Path.Combine(installationRoot, "user", "cache", "compiled-content"))
            : null,
    };
}

internal sealed record CompiledContentCacheReadResult(
    RuntimeContent? Content,
    ContentCompatibilityData? CompatibilityData,
    IReadOnlyList<DiagnosticEvent> Diagnostics,
    int CompiledScriptCount,
    CompiledContentCacheStatus Status,
    string? RejectionReason = null)
{
    public static CompiledContentCacheReadResult Miss(CompiledContentCacheStatus status = CompiledContentCacheStatus.Miss) =>
        new(null, null, [], 0, status);

    public static CompiledContentCacheReadResult Rejected(string reason) =>
        new(null, null, [], 0, CompiledContentCacheStatus.Rejected, reason);
}

internal static class CompiledContentCache
{
    internal const int FormatVersion = 1;
    internal const int CompilerRevision = 3;
    private const string FileName = "content-v1.json.gz";
    private const int CacheKeyLength = 64;
    private static ReadOnlySpan<byte> HeaderMagic => "OXCECC1\n"u8;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string? ComputeKey(
        InstallationLoadRequest request,
        ModLoadPlan plan,
        ContentSnapshotOptions contentOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(contentOptions);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new HashWriter(hash);
        writer.String("OXCE compiled content cache");
        writer.Int32(FormatVersion);
        writer.Int32(CompilerRevision);
        writer.Int32(VirtualPath.CanonicalizationVersion);
        writer.Int32(RulesetCatalogNormalizer.SchemaVersion);
        writer.Int32(Phase3ContentManifestNormalizer.SchemaVersion);
        writer.Int32(ReferenceScriptApiCatalog.SchemaVersion);
        writer.String(typeof(ContentSnapshot).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        writer.String(typeof(Oxce.Scripting.Compilation.ScriptProgram).Assembly.ManifestModule.ModuleVersionId
            .ToString("N"));
        writer.String(typeof(YamlNode).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        writer.String(request.EngineIdentity.Name);
        writer.String(request.EngineIdentity.Version);
        writer.Int32(IntPtr.Size);
        writer.Int32(BitConverter.IsLittleEndian ? 1 : 0);
        writer.String(RuntimeInformation.ProcessArchitecture.ToString());
        writer.String(request.MasterId);
        writer.Strings(request.ActiveMods);
        writer.Int32(contentOptions.MaximumScripts);
        writer.Int32(contentOptions.MaximumDiagnostics);
        writer.Int32(contentOptions.ResourceResolution.MaximumDescriptors);
        writer.Int32(contentOptions.ResourceResolution.MaximumDimension);
        writer.Pairs(contentOptions.ResourceResolution.SharedSpriteCounts);
        writer.Pairs(contentOptions.ResourceResolution.SharedSoundCounts);
        writer.Int32(plan.Groups.Count);
        foreach (var group in plan.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.String(group.Mod.Metadata.Id);
            writer.String(group.Mod.Metadata.Version.ToString());
            writer.String(group.Mod.Metadata.MasterId);
            writer.String(group.Mod.Metadata.ResourceConfigFile);
            writer.String(group.Mod.Metadata.RequiredExtendedEngine);
            writer.String(group.Mod.Metadata.RequiredExtendedVersion);
            writer.Int32(group.Mod.Metadata.IsMaster ? 1 : 0);
            writer.Int32(group.Mod.Metadata.ReservedSpace);
            writer.Strings(group.Mod.Metadata.ExternalResourceDirectories);
            writer.Int32(group.Rulesets.Count);
            foreach (var ruleset in group.Rulesets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.String(ruleset.Provenance.LayerId);
                writer.String(ruleset.Provenance.ModId ?? string.Empty);
                writer.String(ruleset.CanonicalPath);
                writer.String(ruleset.SourcePath);
                using var input = ruleset.OpenRead();
                writer.Stream(input, cancellationToken);
            }
            writer.Int32(group.Mod.Layers.Count);
            foreach (var layer in group.Mod.Layers)
            {
                writer.String(layer.Provenance.LayerId);
                writer.String(layer.Provenance.ModId ?? string.Empty);
                writer.String(layer.Provenance.Origin);
                var entries = layer.Entries
                    .OrderBy(static entry => entry.CanonicalPath, StringComparer.Ordinal)
                    .ToArray();
                writer.Int32(entries.Length);
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.String(entry.CanonicalPath);
                    writer.String(entry.SourcePath);
                }
            }
        }
        // A conservative pre-composition inventory: configured soundDefs can suppress
        // CAT reads, but cannot introduce other asset inputs. Payload bytes beyond the
        // header are decoded lazily and do not affect the compiled graph.
        var files = plan.CreateVirtualFileCatalog();
        try
        {
            foreach (var (setId, path) in SharedResourceInputs.Sprites)
            {
                if (contentOptions.ResourceResolution.SharedSpriteCounts.ContainsKey(setId)) continue;
                files.TryGet(path, out var entry);
                ResourceInput(setId, entry);
            }
            foreach (var (setId, preferred, fallback) in SharedResourceInputs.Sounds)
            {
                if (contentOptions.ResourceResolution.SharedSoundCounts.ContainsKey(setId)) continue;
                ResourceInput(setId, SharedResourceInputs.FindSound(files, preferred, fallback));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Do not let an optional cache probe reject an otherwise valid build (for
            // example a CAT ignored by resource-config soundDefs). Fresh resolution
            // remains authoritative and rejects unreadable inputs when actually used.
            return null;
        }
        return Convert.ToHexString(hash.GetHashAndReset());

        void ResourceInput(string setId, VirtualFileEntry? entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.String(setId);
            writer.Int32(entry is null ? 0 : 1);
            if (entry is null) return;
            writer.String(entry.CanonicalPath);
            writer.String(entry.SourcePath);
            writer.String(entry.Provenance.LayerId);
            var header = SharedResourceInputs.ReadHeader(entry);
            writer.Int64(header.Length);
            writer.Int32(unchecked((int)header.FirstWord));
        }
    }

    public static CompiledContentCacheReadResult TryRead(
        string key,
        CompiledContentCacheOptions options,
        ContentSnapshotOptions contentOptions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contentOptions);
        if (options.DirectoryPath is null)
        {
            return CompiledContentCacheReadResult.Miss(CompiledContentCacheStatus.Disabled);
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(Path.GetFullPath(options.DirectoryPath), FileName);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return CompiledContentCacheReadResult.Miss();
            }
            if (info.Length <= 0 || info.Length > options.MaximumCompressedBytes)
            {
                return CompiledContentCacheReadResult.Rejected("Cache file size is outside the configured limits.");
            }
            using var input = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            if (!ReadHeader(input, key))
            {
                return CompiledContentCacheReadResult.Rejected("Cache format or input identity does not match.");
            }
            using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false);
            using var bounded = new MaximumReadStream(gzip, options.MaximumExpandedBytes);
            var envelope = JsonSerializer.Deserialize<CompiledContentCacheEnvelope>(bounded, SerializerOptions);
            cancellationToken.ThrowIfCancellationRequested();
            if (envelope is null || envelope.FormatVersion != FormatVersion ||
                !string.Equals(envelope.Key, key, StringComparison.Ordinal))
            {
                return CompiledContentCacheReadResult.Rejected("Cache format or input identity does not match.");
            }
            if (envelope.Diagnostics.Any(static diagnostic =>
                    diagnostic.Severity >= DiagnosticSeverity.Error))
            {
                return CompiledContentCacheReadResult.Rejected("Cache contains build-error diagnostics.");
            }
            var restored = envelope.Content.Restore(contentOptions, cancellationToken);
            return new CompiledContentCacheReadResult(
                restored.Content,
                restored.CompatibilityData,
                envelope.Diagnostics,
                envelope.CompiledScriptCount,
                CompiledContentCacheStatus.Hit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableReadFailure(exception))
        {
            return CompiledContentCacheReadResult.Rejected(exception.Message);
        }
    }

    public static void TryWrite(
        string key,
        ContentSnapshot snapshot,
        CompiledContentCacheOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        if (options.DirectoryPath is null)
        {
            return;
        }
        var directory = Path.GetFullPath(options.DirectoryPath);
        var path = Path.Combine(directory, FileName);
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var envelope = new CompiledContentCacheEnvelope(
                FormatVersion,
                key,
                CachedRuntimeContent.Capture(snapshot),
                snapshot.Diagnostics,
                snapshot.CompiledScriptCount);
            using (var output = new FileStream(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                       FileOptions.SequentialScan))
            {
                WriteHeader(output, key);
                using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
                using var bounded = new MaximumWriteStream(gzip, options.MaximumExpandedBytes);
                JsonSerializer.Serialize(bounded, envelope, SerializerOptions);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(temporaryPath).Length > options.MaximumCompressedBytes)
            {
                return;
            }
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            NotSupportedException or InvalidOperationException or ArgumentException or OverflowException)
        {
            // The cache is optional. A failed publication never invalidates fresh runtime content.
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The cache is optional and a stale uniquely named temporary file is harmless.
                }
            }
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = CompiledContentCacheJsonContext.Default,
        };
        options.Converters.Add(new YamlMappingNodeJsonConverter());
        options.Converters.Add(new YamlNodeJsonConverter());
        options.Converters.Add(new ScriptOperandJsonConverter());
        options.Converters.Add(new ScriptTypeRefJsonConverter());
        options.Converters.Add(new ScriptBindingIdJsonConverter());
        options.Converters.Add(new ScriptOperationIdJsonConverter());
        options.Converters.Add(new ScriptTagTypeIdJsonConverter());
        options.Converters.Add(new ScriptInstructionJsonConverter());
        options.Converters.Add(new ScriptBindingDeclarationJsonConverter());
        options.Converters.Add(new ScriptProgramJsonConverter());
        return options;
    }

    private static bool IsRecoverableReadFailure(Exception exception) => exception is not
        OutOfMemoryException and not StackOverflowException and not AccessViolationException and
        not OperationCanceledException;

    private static bool ReadHeader(Stream input, string key)
    {
        Span<byte> header = stackalloc byte[HeaderMagic.Length + CacheKeyLength + 1];
        input.ReadExactly(header);
        return header[..HeaderMagic.Length].SequenceEqual(HeaderMagic) &&
            header[^1] == (byte)'\n' &&
            Encoding.ASCII.GetString(header.Slice(HeaderMagic.Length, CacheKeyLength)) == key;
    }

    private static void WriteHeader(Stream output, string key)
    {
        if (key.Length != CacheKeyLength)
        {
            throw new ArgumentException("Compiled content cache key has an invalid length.", nameof(key));
        }
        output.Write(HeaderMagic);
        Span<byte> encodedKey = stackalloc byte[CacheKeyLength];
        if (Encoding.ASCII.GetBytes(key, encodedKey) != CacheKeyLength)
        {
            throw new ArgumentException("Compiled content cache key has an invalid encoding.", nameof(key));
        }
        output.Write(encodedKey);
        output.WriteByte((byte)'\n');
    }

    private sealed class HashWriter(IncrementalHash hash)
    {
        private readonly byte[] _integer = new byte[sizeof(long)];

        public void Int32(int value)
        {
            BitConverter.TryWriteBytes(_integer, value);
            hash.AppendData(_integer, 0, sizeof(int));
        }

        public void Int64(long value)
        {
            BitConverter.TryWriteBytes(_integer, value);
            hash.AppendData(_integer);
        }

        public void String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Int32(bytes.Length);
            hash.AppendData(bytes);
        }

        public void Strings(IEnumerable<string> values)
        {
            var materialized = values.ToArray();
            Int32(materialized.Length);
            foreach (var value in materialized) String(value);
        }

        public void Pairs(IReadOnlyDictionary<string, int> values)
        {
            Int32(values.Count);
            foreach (var pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                String(pair.Key);
                Int32(pair.Value);
            }
        }

        public void Stream(Stream input, CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long length = 0;
            using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    contentHash.AppendData(buffer, 0, read);
                    length = checked(length + read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            BitConverter.TryWriteBytes(_integer, length);
            hash.AppendData(_integer);
            hash.AppendData(contentHash.GetHashAndReset());
        }
    }

    private sealed class MaximumReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long _read;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int count)
        {
            _read = checked(_read + count);
            if (_read > maximumBytes)
            {
                throw new InvalidDataException($"Compiled content cache exceeds the {maximumBytes}-byte limit.");
            }
            return count;
        }
    }

    private sealed class MaximumWriteStream(Stream inner, long maximumBytes) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _written = checked(_written + buffer.Length);
            if (_written > maximumBytes)
            {
                throw new InvalidDataException($"Compiled content cache exceeds the {maximumBytes}-byte limit.");
            }
            inner.Write(buffer);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed record CompiledContentCacheEnvelope(
    int FormatVersion,
    string Key,
    CachedRuntimeContent Content,
    IReadOnlyList<DiagnosticEvent> Diagnostics,
    int CompiledScriptCount);

internal sealed record CachedRuntimeContent(
    Phase3ContentCatalog Catalog,
    ContentLoadCapabilities Capabilities,
    int ParsedFileCount,
    CachedScriptTagCatalog Tags,
    IReadOnlyList<ContentScriptArtifact> Scripts,
    IReadOnlyList<CachedScriptEventPlan> EventPlans,
    IReadOnlyList<ContentInitialScriptValue> InitialValues,
    IReadOnlyList<CachedResourceDescriptor> Resources,
    IReadOnlyList<CachedResourceIndex> ResourceIndexes)
{
    public static CachedRuntimeContent Capture(ContentSnapshot snapshot) => new(
        snapshot.CompatibilityData.Catalog,
        snapshot.Content.Capabilities,
        snapshot.Content.ParsedFileCount,
        CachedScriptTagCatalog.Capture(snapshot.Content.Tags),
        snapshot.Content.Scripts,
        snapshot.Content.EventPlans.Select(CachedScriptEventPlan.Capture).ToArray(),
        snapshot.Content.InitialValues,
        snapshot.Content.Resources.Descriptors.Select(static descriptor => new CachedResourceDescriptor(
            descriptor.Id,
            descriptor.Kind,
            descriptor.CanonicalPath,
            descriptor.SourcePath,
            descriptor.Provenance,
            descriptor.LoadPolicy,
            descriptor.RuntimeIndex,
            descriptor.Width,
            descriptor.Height,
            descriptor.OwnerSection,
            descriptor.OwnerId)).ToArray(),
        snapshot.Content.Resources.Indexes.Select(static index => new CachedResourceIndex(
            index.Kind,
            index.SetId,
            index.ModId,
            index.DeclaredIndex,
            index.RuntimeIndex,
            index.Handle.Index)).ToArray());

    public CachedRuntimeContentRestore Restore(
        ContentSnapshotOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (Scripts.Count > options.MaximumScripts)
        {
            throw new InvalidDataException("Compiled content cache exceeds the configured script limit.");
        }
        if (Resources.Count > options.ResourceResolution.MaximumDescriptors)
        {
            throw new InvalidDataException("Compiled content cache exceeds the configured resource limit.");
        }
        if (!Capabilities.Has(ContentLoadStage.RuntimeLinked) ||
            !Catalog.Capabilities.Has(ContentLoadStage.Linked))
        {
            throw new InvalidDataException("Compiled content cache does not contain linked runtime content.");
        }
        var generation = ContentGenerationId.Next();
        var descriptors = Resources.Select((descriptor, index) => new ResolvedResourceDescriptor(
            new ResourceHandle(generation, index, descriptor.Kind),
            descriptor.Id,
            descriptor.Kind,
            descriptor.CanonicalPath,
            descriptor.SourcePath,
            descriptor.Provenance,
            descriptor.LoadPolicy,
            descriptor.RuntimeIndex,
            descriptor.Width,
            descriptor.Height,
            descriptor.OwnerSection,
            descriptor.OwnerId)).ToArray();
        var indexes = ResourceIndexes.Select(index =>
        {
            if ((uint)index.DescriptorIndex >= (uint)descriptors.Length ||
                descriptors[index.DescriptorIndex].Kind != index.Kind)
            {
                throw new InvalidDataException("Compiled resource index refers outside its descriptor table.");
            }
            return new ResolvedResourceIndex(
                index.Kind,
                index.SetId,
                index.ModId,
                index.DeclaredIndex,
                index.RuntimeIndex,
                descriptors[index.DescriptorIndex].Handle);
        }).ToArray();
        var resources = new ResolvedResourceCatalog(generation, descriptors, indexes);
        var runtimeRules = RuntimeRuleLinker.Link(
            Catalog,
            resources,
            Scripts,
            options: new RuntimeRuleLinkOptions { CancellationToken = cancellationToken });
        if (!runtimeRules.IsValid)
        {
            throw new InvalidDataException("Compiled content cache failed runtime-rule relinking.");
        }
        return new CachedRuntimeContentRestore(
            new RuntimeContent(
                Capabilities,
                ParsedFileCount,
                Tags.Restore(),
                Scripts,
                EventPlans.Select(static plan => plan.Restore()).ToArray(),
                InitialValues,
                resources,
                runtimeRules.Catalog),
            new ContentCompatibilityData(Catalog, runtimeRules.Compatibility));
    }
}

internal sealed record CachedRuntimeContentRestore(
    RuntimeContent Content,
    ContentCompatibilityData CompatibilityData);

internal sealed record CachedScriptTagCatalog(
    IReadOnlyList<ScriptTagTypeDefinition> Types,
    IReadOnlyList<string> ValueTypes,
    IReadOnlyList<ScriptTagDefinition> Tags)
{
    public static CachedScriptTagCatalog Capture(ScriptTagCatalog catalog) =>
        new(catalog.Types, catalog.ValueTypes, catalog.Tags);

    public ScriptTagCatalog Restore()
    {
        var builder = new ScriptTagCatalogBuilder();
        foreach (var type in Types) builder.AddType(type);
        foreach (var valueType in ValueTypes)
        {
            if (!string.Equals(valueType, "int", StringComparison.Ordinal)) builder.AddValueType(valueType);
        }
        foreach (var tag in Tags)
        {
            var restored = builder.AddTag(tag.OwnerType, tag.Name, tag.ValueType, tag.SourceFile);
            if (restored.Index != tag.Index)
            {
                throw new InvalidDataException("Compiled script-tag indexes are not dense and owner ordered.");
            }
        }
        return builder.Build();
    }
}

internal sealed record CachedScriptEventPlan(
    string ParserName,
    IReadOnlyList<ScriptGlobalEvent> Before,
    IReadOnlyList<ScriptGlobalEvent> After)
{
    public static CachedScriptEventPlan Capture(ContentScriptEventPlan plan) =>
        new(plan.ParserName, plan.Plan.Before, plan.Plan.After);

    public ContentScriptEventPlan Restore()
    {
        var mutations = Before.Concat(After).Select(item => new ScriptEventMutation(
            ScriptEventMutationKind.Append,
            item.Name,
            item.Offset,
            item.Program,
            item.Program.ParserName,
            1));
        var result = ScriptEventComposer.Compose(mutations);
        if (!result.Succeeded)
        {
            throw new InvalidDataException("Compiled script-event plan is invalid.");
        }
        return new ContentScriptEventPlan(ParserName, result.Plan!);
    }
}

internal sealed record CachedResourceDescriptor(
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

internal sealed record CachedResourceIndex(
    ResourceKind Kind,
    string SetId,
    string ModId,
    int DeclaredIndex,
    int RuntimeIndex,
    int DescriptorIndex);

internal sealed class YamlNodeJsonConverter : JsonConverter<YamlNode>
{
    public override YamlNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadNode(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, YamlNode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)value.Kind);
        writer.WritePropertyName("span");
        JsonSerializer.Serialize(writer, value.Span, options);
        if (value.Tag is null) writer.WriteNull("tag"); else writer.WriteString("tag", value.Tag);
        if (value.Anchor is null) writer.WriteNull("anchor"); else writer.WriteString("anchor", value.Anchor);
        switch (value)
        {
            case YamlNullNode nullNode:
                writer.WriteString("spelling", nullNode.Spelling);
                break;
            case YamlScalarNode scalar:
                writer.WriteString("value", scalar.Value);
                writer.WriteNumber("style", (int)scalar.Style);
                break;
            case YamlSequenceNode sequence:
                writer.WriteStartArray("items");
                foreach (var item in sequence.Items) Write(writer, item, options);
                writer.WriteEndArray();
                break;
            case YamlMappingNode mapping:
                writer.WriteStartArray("entries");
                foreach (var entry in mapping.Entries)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("key");
                    Write(writer, entry.Key, options);
                    writer.WritePropertyName("value");
                    Write(writer, entry.Value, options);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
            default:
                throw new JsonException($"Unsupported YAML node type '{value.GetType().Name}'.");
        }
        writer.WriteEndObject();
    }

    private static YamlNode ReadNode(JsonElement element)
    {
        var kind = (YamlNodeKind)element.GetProperty("kind").GetInt32();
        var span = element.GetProperty("span").Deserialize(CompiledContentCacheJsonContext.Default.SourceSpan);
        var tag = element.GetProperty("tag").GetString();
        var anchor = element.GetProperty("anchor").GetString();
        return kind switch
        {
            YamlNodeKind.Null => new YamlNullNode(span, element.GetProperty("spelling").GetString()!, tag, anchor),
            YamlNodeKind.Scalar => new YamlScalarNode(
                span,
                element.GetProperty("value").GetString()!,
                (YamlScalarStyle)element.GetProperty("style").GetInt32(),
                tag,
                anchor),
            YamlNodeKind.Sequence => new YamlSequenceNode(
                span,
                element.GetProperty("items").EnumerateArray().Select(ReadNode),
                tag,
                anchor),
            YamlNodeKind.Mapping => new YamlMappingNode(
                span,
                element.GetProperty("entries").EnumerateArray().Select(entry => new YamlMappingEntry(
                    ReadNode(entry.GetProperty("key")),
                    ReadNode(entry.GetProperty("value")))),
                tag,
                anchor),
            _ => throw new JsonException($"Unknown YAML node kind '{kind}'."),
        };
    }
}

internal sealed class YamlMappingNodeJsonConverter : JsonConverter<YamlMappingNode>
{
    private static readonly YamlNodeJsonConverter Converter = new();

    public override YamlMappingNode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        Converter.Read(ref reader, typeof(YamlNode), options) as YamlMappingNode ??
        throw new JsonException("Expected a YAML mapping node.");

    public override void Write(Utf8JsonWriter writer, YamlMappingNode value, JsonSerializerOptions options) =>
        Converter.Write(writer, value, options);
}

internal sealed class ScriptOperandJsonConverter : JsonConverter<Oxce.Scripting.Compilation.ScriptOperand>
{
    public override Oxce.Scripting.Compilation.ScriptOperand Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = (Oxce.Scripting.Compilation.ScriptOperandKind)root.GetProperty("kind").GetInt32();
        var scalar = root.GetProperty("scalar").GetInt32();
        return kind switch
        {
            Oxce.Scripting.Compilation.ScriptOperandKind.Register =>
                Oxce.Scripting.Compilation.ScriptOperand.Register(scalar),
            Oxce.Scripting.Compilation.ScriptOperandKind.Scalar =>
                Oxce.Scripting.Compilation.ScriptOperand.IntegerValue(scalar),
            Oxce.Scripting.Compilation.ScriptOperandKind.Text =>
                Oxce.Scripting.Compilation.ScriptOperand.TextValue(root.GetProperty("text").GetString()!),
            Oxce.Scripting.Compilation.ScriptOperandKind.Label =>
                Oxce.Scripting.Compilation.ScriptOperand.Label(scalar),
            Oxce.Scripting.Compilation.ScriptOperandKind.Binding =>
                Oxce.Scripting.Compilation.ScriptOperand.Binding(scalar),
            _ => throw new JsonException($"Unknown script operand kind '{kind}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        Oxce.Scripting.Compilation.ScriptOperand value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)value.Kind);
        writer.WriteNumber("scalar", value.Scalar);
        if (value.Text is null) writer.WriteNull("text"); else writer.WriteString("text", value.Text);
        writer.WriteEndObject();
    }
}

internal sealed class ScriptTypeRefJsonConverter : JsonConverter<Oxce.Scripting.Types.ScriptTypeRef>
{
    public override Oxce.Scripting.Types.ScriptTypeRef Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new Oxce.Scripting.Types.ScriptTypeRef(
            new Oxce.Scripting.Types.ScriptTypeId(root.GetProperty("id").GetUInt16()),
            (Oxce.Scripting.Types.ScriptTypeModifier)root.GetProperty("modifiers").GetByte());
    }

    public override void Write(
        Utf8JsonWriter writer,
        Oxce.Scripting.Types.ScriptTypeRef value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", value.Id.Value);
        writer.WriteNumber("modifiers", (byte)value.Modifiers);
        writer.WriteEndObject();
    }
}

internal sealed class ScriptBindingIdJsonConverter : JsonConverter<Oxce.Scripting.Api.ScriptBindingId>
{
    public override Oxce.Scripting.Api.ScriptBindingId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetInt32());

    public override void Write(
        Utf8JsonWriter writer, Oxce.Scripting.Api.ScriptBindingId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

internal sealed class ScriptOperationIdJsonConverter : JsonConverter<Oxce.Scripting.Binding.ScriptOperationId>
{
    public override Oxce.Scripting.Binding.ScriptOperationId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetInt32());

    public override void Write(
        Utf8JsonWriter writer, Oxce.Scripting.Binding.ScriptOperationId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

internal sealed class ScriptTagTypeIdJsonConverter : JsonConverter<ScriptTagTypeId>
{
    public override ScriptTagTypeId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetUInt16());

    public override void Write(Utf8JsonWriter writer, ScriptTagTypeId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

internal sealed class ScriptProgramJsonConverter : JsonConverter<Oxce.Scripting.Compilation.ScriptProgram>
{
    public override Oxce.Scripting.Compilation.ScriptProgram Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new Oxce.Scripting.Compilation.ScriptProgram(
            root.GetProperty("parserName").GetString()!,
            root.GetProperty("instructions").Deserialize<
                IReadOnlyList<Oxce.Scripting.Compilation.ScriptInstruction>>(options)!,
            root.GetProperty("outputs").Deserialize<
                IReadOnlyList<Oxce.Scripting.Types.ScriptTypeRef>>(options)!,
            root.GetProperty("registerBytes").GetInt32(),
            root.GetProperty("registers").Deserialize<
                IReadOnlyList<Oxce.Scripting.Compilation.ScriptRegisterDefinition>>(options)!,
            root.GetProperty("bindings").Deserialize<
                IReadOnlyList<Oxce.Scripting.Api.ScriptBindingDeclaration>>(options)!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        Oxce.Scripting.Compilation.ScriptProgram value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("parserName", value.ParserName);
        writer.WritePropertyName("instructions");
        JsonSerializer.Serialize(writer, value.Instructions, options);
        writer.WritePropertyName("outputs");
        JsonSerializer.Serialize(writer, value.Outputs, options);
        writer.WriteNumber("registerBytes", value.RegisterBytes);
        writer.WritePropertyName("registers");
        JsonSerializer.Serialize(writer, value.Registers, options);
        writer.WritePropertyName("bindings");
        JsonSerializer.Serialize(writer, value.Bindings, options);
        writer.WriteEndObject();
    }
}

internal sealed class ScriptInstructionJsonConverter :
    JsonConverter<Oxce.Scripting.Compilation.ScriptInstruction>
{
    public override Oxce.Scripting.Compilation.ScriptInstruction Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new Oxce.Scripting.Compilation.ScriptInstruction(
            root.GetProperty("operation").Deserialize<Oxce.Scripting.Binding.ScriptOperationId>(options),
            root.GetProperty("operands").Deserialize<
                IReadOnlyList<Oxce.Scripting.Compilation.ScriptOperand>>(options)!,
            root.GetProperty("source").Deserialize<SourceSpan>(options));
    }

    public override void Write(
        Utf8JsonWriter writer,
        Oxce.Scripting.Compilation.ScriptInstruction value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("operation");
        JsonSerializer.Serialize(writer, value.Operation, options);
        writer.WritePropertyName("operands");
        JsonSerializer.Serialize(writer, value.Operands, options);
        writer.WritePropertyName("source");
        JsonSerializer.Serialize(writer, value.Source, options);
        writer.WriteEndObject();
    }
}

internal sealed class ScriptBindingDeclarationJsonConverter :
    JsonConverter<Oxce.Scripting.Api.ScriptBindingDeclaration>
{
    public override Oxce.Scripting.Api.ScriptBindingDeclaration Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new Oxce.Scripting.Api.ScriptBindingDeclaration(
            root.GetProperty("id").Deserialize<Oxce.Scripting.Api.ScriptBindingId>(options),
            root.GetProperty("name").GetString()!,
            root.GetProperty("parameters").Deserialize<
                IReadOnlyList<Oxce.Scripting.Api.ScriptBindingParameter>>(options)!,
            root.GetProperty("parserGroups").Deserialize<IReadOnlyList<string>>(options)!,
            root.GetProperty("reference").Deserialize<Oxce.Scripting.Api.ScriptReferenceLocation>(options)!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        Oxce.Scripting.Api.ScriptBindingDeclaration value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("id");
        JsonSerializer.Serialize(writer, value.Id, options);
        writer.WriteString("name", value.Name);
        writer.WritePropertyName("parameters");
        JsonSerializer.Serialize(writer, value.Parameters, options);
        writer.WritePropertyName("parserGroups");
        JsonSerializer.Serialize(writer, value.ParserGroups, options);
        writer.WritePropertyName("reference");
        JsonSerializer.Serialize(writer, value.Reference, options);
        writer.WriteEndObject();
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CompiledContentCacheEnvelope))]
[JsonSerializable(typeof(SourceSpan))]
internal sealed partial class CompiledContentCacheJsonContext : JsonSerializerContext;
