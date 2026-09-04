using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Mods.Bootstrap;

public enum InstallationLoadStage
{
    Discovery,
    Planning,
    CacheLookup,
    Parsing,
    Composition,
    TypeAndLink,
    ResourceResolution,
    ScriptCompilation,
    RuntimeRuleLinking,
    Completed,
}

public enum InstallationLoadFailureKind
{
    Cancelled,
    InvalidRequest,
    Discovery,
    Planning,
    ContentBuild,
}

public readonly record struct InstallationLoadProgress(InstallationLoadStage Stage);

public sealed record InstallationLoadFailure(
    InstallationLoadFailureKind Kind,
    InstallationLoadStage Stage,
    string Message);

public sealed class InstallationLoadRequest
{
    public InstallationLoadRequest(
        string installationRoot,
        string masterId,
        IEnumerable<string> activeMods,
        ModEngineIdentity engineIdentity)
    {
        InstallationRoot = installationRoot;
        MasterId = masterId;
        ActiveMods = Array.AsReadOnly(activeMods?.ToArray() ?? throw new ArgumentNullException(nameof(activeMods)));
        EngineIdentity = engineIdentity;
    }

    public string InstallationRoot { get; }
    public string MasterId { get; }
    public IReadOnlyList<string> ActiveMods { get; }
    public ModEngineIdentity EngineIdentity { get; }

    public static InstallationLoadRequest ForMasterAndAddOn(
        string installationRoot,
        string masterId,
        string addOnId,
        ModEngineIdentity engineIdentity) => new(
            installationRoot,
            masterId,
            addOnId == "-" ? [masterId] : [masterId, addOnId],
            engineIdentity);

    internal string ValidateAndGetRoot()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(InstallationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(MasterId);
        ArgumentNullException.ThrowIfNull(EngineIdentity);
        if (ActiveMods.Count == 0 || !ActiveMods.Contains(MasterId, StringComparer.Ordinal))
            throw new ArgumentException("Active mods must contain the selected master.", nameof(ActiveMods));
        if (ActiveMods.Any(string.IsNullOrWhiteSpace) ||
            ActiveMods.Distinct(StringComparer.Ordinal).Count() != ActiveMods.Count)
            throw new ArgumentException("Active mod IDs must be distinct and non-empty.", nameof(ActiveMods));
        return Path.GetFullPath(InstallationRoot);
    }
}

public sealed class InstallationContentLoadOptions
{
    public int MaximumDiagnostics { get; init; } = 100_000;
    public bool RetainCompatibilityData { get; init; }
    public ContentSnapshotOptions Content { get; init; } = new();
    public CompiledContentCacheOptions Cache { get; init; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDiagnostics);
        ArgumentNullException.ThrowIfNull(Content);
        ArgumentNullException.ThrowIfNull(Cache);
        Content.Validate();
        Cache.Validate();
    }
}

public sealed record InstallationPlanResult(
    ModLoadPlan? Plan,
    IReadOnlyList<DiagnosticEvent> Diagnostics,
    int ReportedDiagnosticCount,
    int DroppedDiagnosticCount,
    InstallationLoadFailure? Failure)
{
    public bool IsSuccess => Plan is not null && Failure is null;

    public string DescribeFailure(int maximumDiagnostics = 25) =>
        InstallationFailureDescription.Format(Failure, Diagnostics, maximumDiagnostics);
}

public sealed record InstallationContentLoadResult(
    RuntimeContent? Content,
    ContentCompatibilityData? CompatibilityData,
    ContentBuildMeasurements? Measurements,
    IReadOnlyList<DiagnosticEvent> Diagnostics,
    int ReportedDiagnosticCount,
    int DroppedDiagnosticCount,
    InstallationLoadFailure? Failure,
    CompiledContentCacheStatus CacheStatus = CompiledContentCacheStatus.Disabled,
    string? CacheRejectionReason = null)
{
    public bool IsSuccess => Content is not null && Failure is null;

    public string DescribeFailure(int maximumDiagnostics = 25) =>
        InstallationFailureDescription.Format(Failure, Diagnostics, maximumDiagnostics);
}

public static class InstallationPlanBuilder
{
    public static InstallationPlanResult Create(
        InstallationLoadRequest request,
        IDiagnosticSink? diagnostics = null,
        int maximumDiagnostics = 100_000,
        IProgress<InstallationLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDiagnostics);
        var collector = new DiagnosticCollector(maximumDiagnostics);
        IDiagnosticSink sink = diagnostics is null
            ? collector
            : new ForwardingDiagnosticSink(diagnostics, collector);
        var stage = InstallationLoadStage.Discovery;
        try
        {
            var root = request.ValidateAndGetRoot();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallationLoadProgress(stage));
            var options = new ModDiscoveryOptions
            {
                ExternalResourceRoots = [root],
                CancellationToken = cancellationToken,
            };
            var standard = ModDiscovery.ScanDirectory(
                Path.Combine(root, "standard"), sink, options);
            var user = ModDiscovery.ScanDirectory(
                Path.Combine(root, "user", "mods"), sink, options);
            if (collector.HasSeverityAtLeast(DiagnosticSeverity.Error))
                return Failure(InstallationLoadFailureKind.Discovery, stage,
                    "Installation discovery reported one or more errors.");

            stage = InstallationLoadStage.Planning;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallationLoadProgress(stage));
            var catalog = ModCatalog.Create(standard.Mods.Concat(user.Mods), sink);
            var plan = ModLoadPlanner.Create(
                catalog,
                request.ActiveMods.Select(static id => new ModActivation(id, true)),
                request.MasterId,
                request.EngineIdentity,
                sink);
            if (!plan.IsValid || collector.HasSeverityAtLeast(DiagnosticSeverity.Error))
                return Failure(InstallationLoadFailureKind.Planning, stage,
                    "The selected mod set did not produce a valid load plan.");
            return new InstallationPlanResult(
                plan, collector.Snapshot(), collector.ReportedCount, collector.DroppedCount, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(InstallationLoadFailureKind.Cancelled, stage, "Installation loading was cancelled.");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            var kind = exception is ArgumentException && stage == InstallationLoadStage.Discovery
                ? InstallationLoadFailureKind.InvalidRequest
                : stage == InstallationLoadStage.Discovery
                    ? InstallationLoadFailureKind.Discovery
                    : InstallationLoadFailureKind.Planning;
            return Failure(kind, stage, exception.Message);
        }

        InstallationPlanResult Failure(
            InstallationLoadFailureKind kind,
            InstallationLoadStage failedStage,
            string message) => new(
                null,
                collector.Snapshot(),
                collector.ReportedCount,
                collector.DroppedCount,
                new InstallationLoadFailure(kind, failedStage, message));
    }

    private static bool IsExpectedFailure(Exception exception) => exception is
        ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or YamlFormatException;

    private sealed class ForwardingDiagnosticSink(IDiagnosticSink destination, IDiagnosticSink collector)
        : IDiagnosticSink
    {
        public void Report(DiagnosticEvent diagnostic)
        {
            destination.Report(diagnostic);
            collector.Report(diagnostic);
        }
    }
}

public static class InstallationContentLoader
{
    public static InstallationContentLoadResult Load(
        InstallationLoadRequest request,
        InstallationContentLoadOptions? options = null,
        IProgress<InstallationLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= new InstallationContentLoadOptions();
        options.Validate();
        var collector = new DiagnosticCollector(options.MaximumDiagnostics);
        var stage = InstallationLoadStage.Discovery;
        var relay = new ContentProgressRelay(progress, value => stage = value);
        var plan = InstallationPlanBuilder.Create(
            request,
            collector,
            options.MaximumDiagnostics,
            relay,
            cancellationToken);
        if (!plan.IsSuccess)
            return Failure(plan.Failure!);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            stage = InstallationLoadStage.CacheLookup;
            progress?.Report(new InstallationLoadProgress(stage));
            var contentOptions = options.Content with
            {
                RetainAuditArtifact = false,
                CancellationToken = cancellationToken,
                Progress = relay,
            };
            var cacheOptions = options.Cache.Resolve(request.ValidateAndGetRoot());
            var cacheKey = cacheOptions.DirectoryPath is null
                ? null
                : CompiledContentCache.ComputeKey(request, plan.Plan!, contentOptions, cancellationToken);
            var cached = cacheKey is null
                ? CompiledContentCacheReadResult.Miss(CompiledContentCacheStatus.Disabled)
                : CompiledContentCache.TryRead(
                    cacheKey, cacheOptions, contentOptions, cancellationToken);
            if (cached.Content is not null)
            {
                foreach (var diagnostic in cached.Diagnostics)
                {
                    collector.Report(diagnostic);
                }
                stage = InstallationLoadStage.Completed;
                progress?.Report(new InstallationLoadProgress(stage));
                return new InstallationContentLoadResult(
                    cached.Content,
                    options.RetainCompatibilityData ? cached.CompatibilityData : null,
                    ContentBuildMeasurements.Empty,
                    collector.Snapshot(),
                    collector.ReportedCount,
                    collector.DroppedCount,
                    null,
                    cached.Status,
                    cached.RejectionReason);
            }
            var snapshot = ContentSnapshotBuilder.Build(plan.Plan!, collector, options: contentOptions);
            if (!snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked) ||
                collector.HasSeverityAtLeast(DiagnosticSeverity.Error))
            {
                return new InstallationContentLoadResult(
                    null,
                    null,
                    snapshot.Measurements,
                    collector.Snapshot(),
                    collector.ReportedCount,
                    collector.DroppedCount,
                    new InstallationLoadFailure(
                        InstallationLoadFailureKind.ContentBuild,
                        stage,
                        "Installation content did not reach the runtime-linked stage."),
                    cached.Status,
                    cached.RejectionReason);
            }

            if (cacheKey is not null)
            {
                CompiledContentCache.TryWrite(cacheKey, snapshot, cacheOptions, cancellationToken);
            }
            stage = InstallationLoadStage.Completed;
            progress?.Report(new InstallationLoadProgress(stage));
            return new InstallationContentLoadResult(
                snapshot.Content,
                options.RetainCompatibilityData ? snapshot.CompatibilityData : null,
                snapshot.Measurements,
                collector.Snapshot(),
                collector.ReportedCount,
                collector.DroppedCount,
                null,
                cached.Status,
                cached.RejectionReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(new InstallationLoadFailure(
                InstallationLoadFailureKind.Cancelled, stage, "Installation loading was cancelled."));
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or
            YamlFormatException)
        {
            return Failure(new InstallationLoadFailure(
                InstallationLoadFailureKind.ContentBuild, stage, exception.Message));
        }

        InstallationContentLoadResult Failure(InstallationLoadFailure failure) => new(
            null,
            null,
            null,
            collector.Snapshot(),
            collector.ReportedCount,
            collector.DroppedCount,
            failure,
            CompiledContentCacheStatus.Disabled);
    }

    private sealed class ContentProgressRelay(
        IProgress<InstallationLoadProgress>? destination,
        Action<InstallationLoadStage> stageChanged) :
        IProgress<InstallationLoadProgress>, IProgress<ContentBuildProgress>
    {
        public void Report(InstallationLoadProgress value)
        {
            stageChanged(value.Stage);
            destination?.Report(value);
        }

        public void Report(ContentBuildProgress value)
        {
            if (value.Stage == ContentBuildProgressStage.Completed) return;
            var stage = value.Stage switch
            {
                ContentBuildProgressStage.Parsing => InstallationLoadStage.Parsing,
                ContentBuildProgressStage.Composition => InstallationLoadStage.Composition,
                ContentBuildProgressStage.TypeAndLink => InstallationLoadStage.TypeAndLink,
                ContentBuildProgressStage.ResourceResolution => InstallationLoadStage.ResourceResolution,
                ContentBuildProgressStage.ScriptCompilation => InstallationLoadStage.ScriptCompilation,
                ContentBuildProgressStage.RuntimeRuleLinking => InstallationLoadStage.RuntimeRuleLinking,
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
            Report(new InstallationLoadProgress(stage));
        }
    }
}

internal static class InstallationFailureDescription
{
    public static string Format(
        InstallationLoadFailure? failure,
        IReadOnlyList<DiagnosticEvent> diagnostics,
        int maximumDiagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDiagnostics);
        var lines = new List<string>();
        if (failure is not null) lines.Add($"{failure.Kind} during {failure.Stage}: {failure.Message}");
        lines.AddRange(diagnostics
            .Where(static item => item.Severity >= DiagnosticSeverity.Error)
            .Take(maximumDiagnostics)
            .Select(static item => $"{item.Code}: {item.Message}"));
        return string.Join(Environment.NewLine, lines);
    }
}
