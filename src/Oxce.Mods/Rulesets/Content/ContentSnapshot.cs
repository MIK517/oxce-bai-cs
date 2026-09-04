using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;
using Oxce.Mods.Files;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.Phase3;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting;
using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Events;
using Oxce.Scripting.Globals;

namespace Oxce.Mods.Rulesets.Content;

public sealed record ContentSnapshotOptions
{
    public int MaximumScripts { get; init; } = 100_000;
    public int MaximumDiagnostics { get; init; } = DiagnosticCollector.DefaultMaximumDiagnostics;
    public bool RetainAuditArtifact { get; init; }
    public ResourceResolutionOptions ResourceResolution { get; init; } = new();
    public CancellationToken CancellationToken { get; init; }
    public IProgress<ContentBuildProgress>? Progress { get; init; }

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumScripts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDiagnostics);
        ResourceResolution.Validate();
    }
}

public enum ContentScriptScope
{
    Default,
    GlobalEvent,
    Rule,
    StatBonus,
}

public sealed record ContentScriptArtifact(
    ContentScriptScope Scope,
    string OwnerSection,
    string OwnerId,
    string ParserName,
    string SourcePath,
    int SourceLine,
    ScriptProgram Program);

public sealed record ContentScriptEventPlan(string ParserName, ScriptEventPlan Plan);

public sealed record ContentInitialScriptValue(
    string OwnerSection,
    string OwnerId,
    string OwnerType,
    string TagName,
    string ValueType,
    int Value,
    string SourcePath,
    int SourceLine);

public sealed class RuntimeContent
{
    internal RuntimeContent(
        Phase3ContentCatalog catalog,
        ContentLoadCapabilities capabilities,
        int parsedFileCount,
        ScriptTagCatalog tags,
        IEnumerable<ContentScriptArtifact> scripts,
        IEnumerable<ContentScriptEventPlan> eventPlans,
        IEnumerable<ContentInitialScriptValue> initialValues,
        ResolvedResourceCatalog resources,
        RuntimeRuleCatalog runtimeRules)
    {
        Catalog = catalog;
        Capabilities = capabilities;
        ParsedFileCount = parsedFileCount;
        Tags = tags;
        Scripts = Array.AsReadOnly(scripts.ToArray());
        EventPlans = Array.AsReadOnly(eventPlans.ToArray());
        InitialValues = Array.AsReadOnly(initialValues.ToArray());
        Resources = resources;
        RuntimeRules = runtimeRules;
    }

    public Phase3ContentCatalog Catalog { get; }
    public ContentLoadCapabilities Capabilities { get; }
    public int ParsedFileCount { get; }
    public ScriptTagCatalog Tags { get; }
    public IReadOnlyList<ContentScriptArtifact> Scripts { get; }
    public IReadOnlyList<ContentScriptEventPlan> EventPlans { get; }
    public IReadOnlyList<ContentInitialScriptValue> InitialValues { get; }
    public ResolvedResourceCatalog Resources { get; }
    public RuntimeRuleCatalog RuntimeRules { get; }
}

public sealed class ContentAuditArtifact : IDisposable
{
    private RulesetDocumentCatalog? _documents;
    private UnresolvedRuleCatalog? _composedRules;

    internal ContentAuditArtifact(ContentBuildSession session)
    {
        _documents = session.Documents;
        _composedRules = session.ComposedRules;
    }

    public RulesetDocumentCatalog Documents => _documents ??
        throw new ObjectDisposedException(nameof(ContentAuditArtifact));
    public UnresolvedRuleCatalog ComposedRules => _composedRules ??
        throw new ObjectDisposedException(nameof(ContentAuditArtifact));
    public bool IsDisposed => _documents is null;

    public void Dispose()
    {
        _documents = null;
        _composedRules = null;
    }
}

public sealed class ContentSnapshot
{
    internal ContentSnapshot(
        RuntimeContent content,
        IEnumerable<DiagnosticEvent> diagnostics,
        int reportedDiagnosticCount,
        int droppedDiagnosticCount,
        int compiledScriptCount,
        ContentBuildMeasurements measurements,
        ContentAuditArtifact? auditArtifact,
        int sourceScopeCount,
        int apiScopeCount)
    {
        Content = content;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        ReportedDiagnosticCount = reportedDiagnosticCount;
        DroppedDiagnosticCount = droppedDiagnosticCount;
        CompiledScriptCount = compiledScriptCount;
        Measurements = measurements;
        AuditArtifact = auditArtifact;
        SourceScopeCount = sourceScopeCount;
        ApiScopeCount = apiScopeCount;
    }

    public RuntimeContent Content { get; }
    public ContentLoadCapabilities Capabilities => Content.Capabilities;
    public ScriptTagCatalog Tags => Content.Tags;
    public IReadOnlyList<ContentScriptArtifact> Scripts => Content.Scripts;
    public IReadOnlyList<ContentScriptEventPlan> EventPlans => Content.EventPlans;
    public IReadOnlyList<ContentInitialScriptValue> InitialValues => Content.InitialValues;
    public IReadOnlyList<DiagnosticEvent> Diagnostics { get; }
    public int ReportedDiagnosticCount { get; }
    public int DroppedDiagnosticCount { get; }
    public int CompiledScriptCount { get; }
    public ContentBuildMeasurements Measurements { get; }
    public ContentAuditArtifact? AuditArtifact { get; }
    public int SourceScopeCount { get; }
    public int ApiScopeCount { get; }
}

public static class ContentSnapshotBuilder
{
    public static ContentSnapshot Build(
        ModLoadPlan plan,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null,
        TypedRuleLoadOptions? typedOptions = null,
        ContentSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new ContentSnapshotOptions();
        options.Validate();
        var collector = new DiagnosticCollector(options.MaximumDiagnostics);
        IDiagnosticSink sink = diagnostics is null
            ? collector
            : new ForwardingDiagnosticSink(diagnostics, collector);
        options.CancellationToken.ThrowIfCancellationRequested();
        compositionOptions = (compositionOptions ?? new RulesetCompositionOptions()) with
        {
            CancellationToken = options.CancellationToken,
        };
        typedOptions = (typedOptions ?? new TypedRuleLoadOptions()) with
        {
            CancellationToken = options.CancellationToken,
        };
        var session = Phase3ContentCatalog.Build(
            plan,
            sink,
            compositionOptions,
            typedOptions,
            options.Progress);
        var capabilities = session.Catalog.Capabilities;
        options.CancellationToken.ThrowIfCancellationRequested();
        options.Progress?.Report(new ContentBuildProgress(ContentBuildProgressStage.ResourceResolution));
        var resourceOptions = options.ResourceResolution with
        {
            CancellationToken = options.CancellationToken,
        };
        var resourceAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var resourceTimer = Stopwatch.StartNew();
        var resourceResolution = capabilities.Has(ContentLoadStage.Linked)
            ? ResourceDescriptorResolver.Resolve(
                plan,
                session.Catalog,
                sink,
                resourceOptions)
            : new ResourceResolutionResult(
                new ResolvedResourceCatalog(ContentGenerationId.Next(), []),
                Array.Empty<ResolvedResourceIssue>());
        resourceTimer.Stop();
        var resourceMeasurement = new ContentBuildStageMeasurement(
            resourceTimer.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - resourceAllocationStart);
        if (capabilities.Has(ContentLoadStage.Linked) && resourceResolution.IsValid)
        {
            capabilities = capabilities.AdvanceTo(ContentLoadStage.ResourcesResolved);
        }
        options.CancellationToken.ThrowIfCancellationRequested();
        options.Progress?.Report(new ContentBuildProgress(ContentBuildProgressStage.ScriptCompilation));
        var compiler = new ContentScriptCompiler(plan, session, sink, options);
        var scriptAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var scriptTimer = Stopwatch.StartNew();
        compiler.Compile();
        scriptTimer.Stop();
        var scriptMeasurement = new ContentBuildStageMeasurement(
            scriptTimer.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - scriptAllocationStart);

        if (capabilities.Has(ContentLoadStage.Linked) &&
            !collector.HasSeverityAtLeast(DiagnosticSeverity.Error))
        {
            capabilities = capabilities.AdvanceTo(ContentLoadStage.ScriptsCompiled);
        }
        options.CancellationToken.ThrowIfCancellationRequested();
        options.Progress?.Report(new ContentBuildProgress(ContentBuildProgressStage.RuntimeRuleLinking));
        var runtimeLinkAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var runtimeLinkTimer = Stopwatch.StartNew();
        var runtimeLink = RuntimeRuleLinker.Link(
            session.Catalog,
            resourceResolution.Catalog,
            compiler.Scripts,
            sink,
            new RuntimeRuleLinkOptions { CancellationToken = options.CancellationToken });
        runtimeLinkTimer.Stop();
        var runtimeLinkMeasurement = new ContentBuildStageMeasurement(
            runtimeLinkTimer.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - runtimeLinkAllocationStart);
        if (capabilities.Has(ContentLoadStage.ResourcesResolved) &&
            capabilities.Has(ContentLoadStage.ScriptsCompiled) && runtimeLink.IsValid)
        {
            capabilities = capabilities.AdvanceTo(ContentLoadStage.RuntimeLinked);
        }
        var runtime = new RuntimeContent(
            session.Catalog,
            capabilities,
            session.ParsedFileCount,
            compiler.Tags,
            compiler.Scripts,
            compiler.EventPlans,
            compiler.InitialValues,
            resourceResolution.Catalog,
            runtimeLink.Catalog);
        options.CancellationToken.ThrowIfCancellationRequested();
        options.Progress?.Report(new ContentBuildProgress(ContentBuildProgressStage.Completed));
        return new ContentSnapshot(
            runtime,
            collector.Snapshot(),
            collector.ReportedCount,
            collector.DroppedCount,
            compiler.CompiledScriptCount,
            session.Measurements.WithResourceResolution(resourceMeasurement).WithScriptCompilation(scriptMeasurement)
                .WithRuntimeRuleLinking(runtimeLinkMeasurement),
            options.RetainAuditArtifact ? new ContentAuditArtifact(session) : null,
            compiler.SourceScopeCount,
            compiler.ApiScopeCount);
    }

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

internal sealed class ContentScriptCompiler
{
    private static readonly ScriptReferenceLocation GeneratedReference = new("src/Engine/Script.cpp", 3906);
    private static readonly ReadOnlyDictionary<string, string> SectionGroups =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["countries"] = "country",
            ["items"] = "item",
            ["armors"] = "unit",
            ["skills"] = "skill",
            ["soldiers"] = "soldier",
            ["units"] = "unit",
            ["crafts"] = "craft",
            ["ufos"] = "ufo",
        });
    private static readonly ReadOnlyDictionary<string, string> RuleTagOwners =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["countries"] = "RuleCountry",
            ["items"] = "RuleItem",
            ["armors"] = "RuleArmor",
            ["skills"] = "RuleSkill",
            ["soldiers"] = "RuleSoldier",
            ["crafts"] = "RuleCraft",
            ["ufos"] = "RuleUfo",
            ["research"] = "RuleResearch",
            ["soldierBonuses"] = "RuleSoldierBonus",
            ["extended"] = "RuleMod",
        });
    private static readonly string[] StatTerms =
    [
        "flatOne", "flatHundred", "strength", "psi", "psiSkill", "psiStrength", "throwing",
        "bravery", "firing", "health", "mana", "tu", "reactions", "stamina", "melee",
        "strengthMelee", "strengthThrowing", "firingReactions", "strengthScaled", "psiScaled",
        "psiSkillScaled", "psiStrengthScaled", "throwingScaled", "braveryScaled", "firingScaled",
        "healthScaled", "manaScaled", "tuScaled", "reactionsScaled", "staminaScaled", "meleeScaled",
        "strengthMeleeScaled", "strengthThrowingScaled", "firingReactionsScaled", "rank", "rankUnified",
        "fatalWounds", "healthCurrent", "manaCurrent", "tuCurrent", "energyCurrent", "moraleCurrent",
        "stunCurrent", "healthNormalized", "manaNormalized", "tuNormalized", "energyNormalized",
        "moraleNormalized", "stunNormalized", "energyRegen",
    ];
    private static readonly string[] EventMutationMarkers =
        ["delete", "new", "update", "override", "ignore"];
    private static readonly IReadOnlyDictionary<string, string> ItemStatParsers =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["damageBonus"] = "damageBonusBonusStats",
            ["meleeBonus"] = "meleeBonusBonusStats",
            ["accuracyMultiplier"] = "accuracyMultiplierBonusStats",
            ["meleeMultiplier"] = "meleeMultiplierBonusStats",
            ["throwMultiplier"] = "throwMultiplierBonusStats",
            ["closeQuartersMultiplier"] = "closeQuartersMultiplierBonusStats",
        });
    private static readonly IReadOnlyDictionary<string, string> ArmorStatParsers =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["psiDefence"] = "psiDefenceBonusStats",
            ["meleeDodge"] = "meleeDodgeBonusStats",
        });
    private static readonly IReadOnlyDictionary<string, string> ArmorRecoveryParsers =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["time"] = "timeRecoveryBonusStats",
            ["energy"] = "energyRecoveryBonusStats",
            ["morale"] = "moraleRecoveryBonusStats",
            ["health"] = "healthRecoveryBonusStats",
            ["mana"] = "manaRecoveryBonusStats",
            ["stun"] = "stunRecoveryBonusStats",
        });
    private static readonly IReadOnlyDictionary<string, string> SoldierRecoveryParsers =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["time"] = "timeSoldierRecoveryBonusStats",
            ["energy"] = "energySoldierRecoveryBonusStats",
            ["morale"] = "moraleSoldierRecoveryBonusStats",
            ["health"] = "healthSoldierRecoveryBonusStats",
            ["mana"] = "manaSoldierRecoveryBonusStats",
            ["stun"] = "stunSoldierRecoveryBonusStats",
        });

    private readonly ModLoadPlan _plan;
    private readonly ContentBuildSession _content;
    private readonly IDiagnosticSink _diagnostics;
    private readonly ContentSnapshotOptions _options;
    private readonly ScriptApiCatalog _baseApi = ReferenceScriptApiCatalog.Instance;
    private readonly ScriptTagCatalogBuilder _tagBuilder = new();
    private readonly Dictionary<string, ScriptTagDefinition> _tagsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ScriptApiCatalog> _apiByDocument = [];
    private readonly Dictionary<(int TagCount, string ModId), ScriptApiCatalog> _apiScopes = [];
    private readonly Dictionary<string, List<ScriptEventMutation>> _eventMutations = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Section, string Id, string Parser), ContentScriptArtifact> _ruleScripts = [];
    private readonly List<ContentScriptArtifact> _otherScripts = [];
    private readonly List<ContentScriptEventPlan> _eventPlans = [];
    private readonly List<ContentInitialScriptValue> _initialValues = [];
    private int _compiledScriptCount;

    public ContentScriptCompiler(
        ModLoadPlan plan,
        ContentBuildSession content,
        IDiagnosticSink diagnostics,
        ContentSnapshotOptions options)
    {
        _plan = plan;
        _content = content;
        _diagnostics = diagnostics;
        _options = options;
        InitializeTagTypes();
    }

    public ScriptTagCatalog Tags { get; private set; } = null!;
    public IReadOnlyList<ContentScriptArtifact> Scripts => _otherScripts
        .Concat(_ruleScripts.Values)
        .OrderBy(static script => script.Scope)
        .ThenBy(static script => script.OwnerSection, StringComparer.Ordinal)
        .ThenBy(static script => script.OwnerId, StringComparer.Ordinal)
        .ThenBy(static script => script.ParserName, StringComparer.Ordinal)
        .ToArray();
    public IReadOnlyList<ContentScriptEventPlan> EventPlans => _eventPlans;
    public IReadOnlyList<ContentInitialScriptValue> InitialValues => _initialValues;
    public int CompiledScriptCount => _compiledScriptCount;
    public int SourceScopeCount => _apiByDocument.Count;
    public int ApiScopeCount => _apiScopes.Count;

    public void Compile()
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        CompileDocuments();
        _options.CancellationToken.ThrowIfCancellationRequested();
        Tags = _tagBuilder.Build();
        var finalApi = CreateApi(Tags, _plan.Groups[^1].Mod.Metadata.Id);
        CompileDefaults(finalApi);
        _options.CancellationToken.ThrowIfCancellationRequested();
        CompileRules(finalApi);
        _options.CancellationToken.ThrowIfCancellationRequested();
        ComposeEvents();
    }

    private void InitializeTagTypes()
    {
        foreach (var type in _baseApi.Types.Where(static type => type.Name.EndsWith(".Tag", StringComparison.Ordinal)))
        {
            _tagBuilder.AddType(new ScriptTagTypeDefinition(
                new ScriptTagTypeId(type.Id.Value),
                type.Name[..^".Tag".Length],
                ushort.MaxValue));
        }
        _tagBuilder.AddValueType("RuleList");
    }

    private void CompileDocuments()
    {
        foreach (var document in _content.Documents.Documents)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            foreach (var extendedNode in document.Root.GetAll("extended"))
            {
                _options.CancellationToken.ThrowIfCancellationRequested();
                if (extendedNode is not YamlMappingNode extended)
                {
                    Invalid("The 'extended' node must be a mapping.", extendedNode, document.File.SourcePath);
                    continue;
                }
                if (extended.TryGet("tagsFile", out var tagsFile))
                {
                    LoadTagsFile(document, tagsFile!);
                }
                if (extended.TryGet("tags", out var tags))
                {
                    LoadTags(tags!, document.File.SourcePath);
                }
                var api = CreateApi(_tagBuilder.Build(), document.Mod.Metadata.Id);
                _apiByDocument[document.DocumentId] = api;
                if (extended.TryGet("globals", out var globals))
                {
                    CompileInitialValues("extended", "globals", globals!, api, document.File.SourcePath);
                }
                if (extended.TryGet("scripts", out var scripts))
                {
                    CompileGlobalScripts(scripts!, api, document.File.SourcePath);
                }
            }
            _apiByDocument.TryAdd(
                document.DocumentId,
                CreateApi(_tagBuilder.Build(), document.Mod.Metadata.Id));
        }
    }

    private void LoadTagsFile(RulesetDocument document, YamlNode node)
    {
        string requested;
        try
        {
            requested = VirtualPath.NormalizeFile(YamlValueReader.ReadString(node));
        }
        catch (Exception exception) when (exception is ArgumentException or YamlFormatException)
        {
            Invalid($"Invalid 'tagsFile': {exception.Message}", node, document.File.SourcePath);
            return;
        }
        var referenced = _content.Documents.Documents.LastOrDefault(candidate =>
            string.Equals(candidate.Mod.Metadata.Id, document.Mod.Metadata.Id, StringComparison.Ordinal) &&
            string.Equals(candidate.File.CanonicalPath, requested, StringComparison.Ordinal));
        if (referenced is null)
        {
            Invalid($"Unknown file name for 'tagsFile': '{requested}'.", node, document.File.SourcePath);
            return;
        }
        foreach (var extendedNode in referenced.Root.GetAll("extended"))
        {
            if (extendedNode is YamlMappingNode extended && extended.TryGet("tags", out var tags))
            {
                LoadTags(tags!, document.File.SourcePath);
            }
        }
    }

    private void LoadTags(YamlNode node, string sourcePath)
    {
        if (node is not YamlMappingNode owners)
        {
            Invalid("The 'extended.tags' node must be a mapping.", node, sourcePath);
            return;
        }
        foreach (var ownerEntry in owners.Entries)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            var owner = ownerEntry.ScalarKey;
            if (owner is null || !_baseApi.TryGetType(owner + ".Tag", out var ownerType))
            {
                // The reference loader enumerates registered owners and therefore ignores
                // unrelated/unknown mappings under extended.tags.
                continue;
            }
            if (ownerEntry.Value is not YamlMappingNode tags)
            {
                Invalid($"Script tags for registered owner '{owner}' must be a mapping.",
                    ownerEntry.Value, sourcePath);
                continue;
            }
            foreach (var entry in tags.Entries)
            {
                var name = entry.ScalarKey;
                if (name is null || entry.Value is not YamlScalarNode)
                {
                    Invalid("Script tag names and value types must be scalars.", entry.Value, sourcePath);
                    continue;
                }
                var valueType = YamlValueReader.ReadString(entry.Value);
                var qualified = "Tag." + name;
                if (_tagsByName.TryGetValue(qualified, out var existing))
                {
                    if (existing.OwnerType.Value != ownerType!.Id.Value ||
                        !string.Equals(existing.ValueType, valueType, StringComparison.Ordinal))
                    {
                        Invalid($"Script tag '{qualified}' is redefined with an incompatible owner or value type.",
                            entry.Value, sourcePath);
                    }
                    continue;
                }
                try
                {
                    var tag = _tagBuilder.AddTag(
                        new ScriptTagTypeId(ownerType!.Id.Value), name, valueType, sourcePath);
                    _tagsByName.Add(qualified, tag);
                }
                catch (ArgumentException exception)
                {
                    Invalid(exception.Message, entry.Value, sourcePath);
                }
            }
        }
    }

    private ScriptApiCatalog CreateApi(ScriptTagCatalog tags, string currentModId)
    {
        var key = (tags.Tags.Count, currentModId);
        if (_apiScopes.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var parserNames = _baseApi.Parsers.Select(static parser => parser.Name).ToArray();
        var constants = new List<ScriptConstantDeclaration>();
        var names = _baseApi.Constants.Select(static constant => constant.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var tag in tags.Tags)
        {
            if (!tags.TryGetType(tag.OwnerType, out var owner) ||
                !_baseApi.TryGetType(owner!.Name + ".Tag", out var type) ||
                !names.Add(tag.Name))
            {
                continue;
            }
            constants.Add(new ScriptConstantDeclaration(
                tag.Name,
                tag.Index,
                parserNames,
                new ScriptReferenceLocation(tag.SourceFile, 1))
            {
                Type = new Oxce.Scripting.Types.ScriptTypeRef(type!.Id),
            });
        }
        for (var index = 0; index < _plan.Groups.Count; index++)
        {
            var name = "RuleList." + _plan.Groups[index].Mod.Metadata.Id;
            if (names.Add(name))
            {
                constants.Add(new ScriptConstantDeclaration(
                    name, index, parserNames, GeneratedReference));
            }
        }
        var currentIndex = _plan.Groups.Select(static group => group.Mod.Metadata.Id)
            .ToList().FindIndex(id => string.Equals(id, currentModId, StringComparison.Ordinal));
        constants.Add(new ScriptConstantDeclaration(
            "RuleList.master", 0, parserNames, GeneratedReference));
        constants.Add(new ScriptConstantDeclaration(
            "RuleList.current", currentIndex, parserNames, GeneratedReference));
        var scope = _baseApi.CreateScope(constants);
        _apiScopes.Add(key, scope);
        return scope;
    }

    private void CompileGlobalScripts(YamlNode node, ScriptApiCatalog api, string sourcePath)
    {
        if (node is not YamlMappingNode scripts)
        {
            Invalid("The 'extended.scripts' node must be a mapping.", node, sourcePath);
            return;
        }
        foreach (var entry in scripts.Entries)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            var parserName = entry.ScalarKey;
            if (parserName is null || parserName.EndsWith('#') ||
                !api.TryGetParser(parserName, out var parser) || !parser!.SupportsEvents)
            {
                if (parserName is not null && !parserName.EndsWith('#'))
                {
                    Invalid($"Unknown global event script parser '{parserName}'.", entry.Value, sourcePath);
                }
                continue;
            }
            if (entry.Value is not YamlSequenceNode events)
            {
                Invalid($"Global scripts for '{parserName}' must be a sequence.", entry.Value, sourcePath);
                continue;
            }
            foreach (var item in events.Items)
            {
                if (item is not YamlMappingNode mutation)
                {
                    Invalid($"Global script event '{parserName}' must be a mapping.", item, sourcePath);
                    continue;
                }
                CompileGlobalMutation(parserName, mutation, api, sourcePath);
            }
        }
    }

    private void CompileGlobalMutation(
        string parserName,
        YamlMappingNode node,
        ScriptApiCatalog api,
        string sourcePath)
    {
        var markers = EventMutationMarkers
            .Where(key => node.TryGet(key, out _)).ToArray();
        if (markers.Length > 1)
        {
            Invalid($"Global script event '{parserName}' has conflicting mutation markers.", node, sourcePath);
            return;
        }
        var marker = markers.SingleOrDefault();
        var kind = marker switch
        {
            "delete" => ScriptEventMutationKind.Delete,
            "new" => ScriptEventMutationKind.New,
            "update" => ScriptEventMutationKind.Update,
            "override" => ScriptEventMutationKind.Override,
            "ignore" => ScriptEventMutationKind.Ignore,
            _ => ScriptEventMutationKind.Append,
        };
        var name = marker is null ? string.Empty : YamlValueReader.ReadString(node.Entries
            .First(entry => string.Equals(entry.ScalarKey, marker, StringComparison.Ordinal)).Value);
        ScriptProgram? program = null;
        if (kind != ScriptEventMutationKind.Delete)
        {
            var code = node.TryGet("code", out var codeNode) ? YamlValueReader.ReadString(codeNode!) : string.Empty;
            program = CompileSource(
                ContentScriptScope.GlobalEvent, "extended", name, parserName, code, api, codeNode ?? node, sourcePath)
                ?.Program;
        }
        var offset = node.TryGet("offset", out var offsetNode)
            ? ScriptEventMutation.ScaleOffset(YamlValueReader.ReadDouble(offsetNode!))
            : 0;
        var mutation = new ScriptEventMutation(kind, name, offset, program, sourcePath, node.Span.Start.Line);
        if (!_eventMutations.TryGetValue(parserName, out var list))
        {
            list = [];
            _eventMutations.Add(parserName, list);
        }
        list.Add(mutation);
    }

    private void CompileDefaults(ScriptApiCatalog api)
    {
        foreach (var pair in DefaultScripts)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            CompileSource(ContentScriptScope.Default, "defaults", string.Empty, pair.Key, pair.Value.Value,
                api, null, pair.Value.ReferencePath);
        }
    }

    private void CompileRules(ScriptApiCatalog finalApi)
    {
        foreach (var section in _content.ComposedRules.Sections)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            foreach (var rule in section.Rules)
            {
                _options.CancellationToken.ThrowIfCancellationRequested();
                foreach (var operation in rule.Operations)
                {
                    _options.CancellationToken.ThrowIfCancellationRequested();
                    var api = operation.Source.DocumentId is { } documentId &&
                        _apiByDocument.TryGetValue(documentId, out var documentApi)
                        ? documentApi
                        : finalApi;
                    CompileRuleMapping(section.Definition.Name, rule.Id, operation.Node, api, operation.Source.SourcePath);
                }
            }
        }
    }

    private void CompileRuleMapping(
        string section,
        string ruleId,
        YamlMappingNode node,
        ScriptApiCatalog api,
        string sourcePath)
    {
        if (node.TryGet("refNode", out var parent) && parent is YamlMappingNode parentMapping)
        {
            CompileRuleMapping(section, ruleId, parentMapping, api, sourcePath);
        }
        if (node.TryGet("scripts", out var scripts))
        {
            CompileRuleScripts(section, ruleId, scripts!, api, sourcePath);
        }
        CompileStatBonuses(section, ruleId, node, api, sourcePath);
        if (node.TryGet("tags", out var tags))
        {
            CompileInitialValues(section, ruleId, tags!, api, sourcePath);
        }
    }

    private void CompileRuleScripts(
        string section,
        string ruleId,
        YamlNode node,
        ScriptApiCatalog api,
        string sourcePath)
    {
        if (node is not YamlMappingNode scripts || !SectionGroups.TryGetValue(section, out var group))
        {
            Invalid($"Rule '{section}/{ruleId}' has scripts outside a registered script-owning section.",
                node, sourcePath, section, ruleId);
            return;
        }
        foreach (var entry in scripts.Entries)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            var parserName = entry.ScalarKey;
            if (parserName is null || parserName.EndsWith('#'))
            {
                continue;
            }
            if (!api.TryGetParser(parserName, out var parser) ||
                !string.Equals(parser!.Group, group, StringComparison.Ordinal))
            {
                Invalid($"Unknown script parser '{parserName}' for rule section '{section}'.",
                    entry.Value, sourcePath, section, ruleId);
                continue;
            }
            if (entry.Value is YamlNullNode)
            {
                _ruleScripts.Remove((section, ruleId, parserName));
                continue;
            }
            if (entry.Value is not YamlScalarNode)
            {
                Invalid($"Rule script '{parserName}' must be scalar source code.",
                    entry.Value, sourcePath, section, ruleId);
                continue;
            }
            var artifact = CompileSource(ContentScriptScope.Rule, section, ruleId, parserName,
                YamlValueReader.ReadString(entry.Value), api, entry.Value, sourcePath);
            if (artifact is not null)
            {
                _ruleScripts[(section, ruleId, parserName)] = artifact;
            }
        }
    }

    private void CompileStatBonuses(
        string section,
        string ruleId,
        YamlMappingNode node,
        ScriptApiCatalog api,
        string sourcePath)
    {
        var direct = section switch
        {
            "items" => ItemStatParsers,
            "armors" => ArmorStatParsers,
            _ => null,
        };
        if (direct is not null)
        {
            foreach (var pair in direct)
            {
                if (node.TryGet(pair.Key, out var stat))
                {
                    CompileStatBonus(section, ruleId, pair.Value, stat!, api, sourcePath);
                }
            }
        }
        var recovery = section switch
        {
            "armors" => ArmorRecoveryParsers,
            "soldierBonuses" => SoldierRecoveryParsers,
            _ => null,
        };
        if (recovery is null || !node.TryGet("recovery", out var recoveryNode) ||
            recoveryNode is not YamlMappingNode recoveryMapping)
        {
            return;
        }
        foreach (var pair in recovery)
        {
            if (recoveryMapping.TryGet(pair.Key, out var stat))
            {
                CompileStatBonus(section, ruleId, pair.Value, stat!, api, sourcePath);
            }
        }
    }

    private void CompileStatBonus(
        string section,
        string ruleId,
        string parserName,
        YamlNode node,
        ScriptApiCatalog api,
        string sourcePath)
    {
        string source;
        if (node is YamlScalarNode)
        {
            source = YamlValueReader.ReadString(node);
        }
        else if (node is YamlMappingNode mapping)
        {
            source = GenerateStatBonus(mapping, sourcePath, section, ruleId);
        }
        else
        {
            Invalid($"Stat bonus '{parserName}' must be scalar code or a coefficient mapping.",
                node, sourcePath, section, ruleId);
            return;
        }
        var artifact = CompileSource(ContentScriptScope.StatBonus, section, ruleId, parserName,
            source, api, node, sourcePath);
        if (artifact is not null)
        {
            _ruleScripts[(section, ruleId, parserName)] = artifact;
        }
    }

    private string GenerateStatBonus(YamlMappingNode mapping, string sourcePath, string section, string ruleId)
    {
        var values = mapping.Entries.ToDictionary(
            entry => entry.ScalarKey ?? string.Empty,
            static entry => entry.Value,
            StringComparer.Ordinal);
        var script = new StringBuilder();
        if (values.Count != 0)
        {
            script.AppendLine("mul bonus 1000;");
        }
        foreach (var term in StatTerms)
        {
            if (!values.Remove(term, out var node))
            {
                continue;
            }
            var coefficients = node is YamlSequenceNode sequence
                ? sequence.Items.Take(4).Select(ScaleCoefficient).ToArray()
                : [ScaleCoefficient(node)];
            script.Append("unit.").Append(term).Append("BonusStats bonus");
            for (var index = 0; index < 4; index++)
            {
                script.Append(' ').Append(index < coefficients.Length ? coefficients[index] : 0);
            }
            script.AppendLine(";");
        }
        foreach (var unknown in values)
        {
            Invalid($"Unknown stat multiplier term '{unknown.Key}'.", unknown.Value,
                sourcePath, section, ruleId);
        }
        if (mapping.Entries.Count != 0)
        {
            script.AppendLine("if ge bonus 0; add bonus 500; else; sub bonus 500; end;");
            script.AppendLine("div bonus 1000;");
        }
        script.Append("return bonus;");
        return script.ToString();

        static int ScaleCoefficient(YamlNode value) =>
            unchecked((int)(YamlValueReader.ReadSingle(value) * 1_000_000f));
    }

    private void CompileInitialValues(
        string section,
        string ruleId,
        YamlNode node,
        ScriptApiCatalog api,
        string sourcePath)
    {
        if (!RuleTagOwners.TryGetValue(section, out var owner) || node is not YamlMappingNode values ||
            !TagsFor(api).TryGetValue(owner, out var ownerType))
        {
            Invalid($"Rule '{section}/{ruleId}' has tag values outside a registered script-value owner.",
                node, sourcePath, section, ruleId);
            return;
        }
        var tags = _tagBuilder.Build();
        foreach (var entry in values.Entries)
        {
            var name = entry.ScalarKey;
            var qualifiedName = "Tag." + name;
            if (name is null ||
                !api.Constants.Any(constant => string.Equals(constant.Name, qualifiedName, StringComparison.Ordinal)) ||
                !tags.TryGetTag(ownerType, qualifiedName, out var tag))
            {
                Invalid($"Rule '{section}/{ruleId}' references unknown script tag '{name}'.",
                    entry.Value, sourcePath, section, ruleId);
                continue;
            }
            var value = tag!.ValueType switch
            {
                "int" => YamlValueReader.ReadInt32(entry.Value),
                "RuleList" => ResolveRuleList(YamlValueReader.ReadString(entry.Value), api),
                _ => throw new InvalidOperationException($"Unsupported validated tag value type '{tag.ValueType}'."),
            };
            _initialValues.RemoveAll(item => item.OwnerSection == section && item.OwnerId == ruleId &&
                item.TagName == tag.Name);
            _initialValues.Add(new ContentInitialScriptValue(
                section, ruleId, owner, tag.Name, tag.ValueType, value,
                sourcePath, entry.Value.Span.Start.Line));
        }
    }

    private static Dictionary<string, ScriptTagTypeId> TagsFor(ScriptApiCatalog api) => api.Types
        .Where(static type => type.Name.EndsWith(".Tag", StringComparison.Ordinal))
        .ToDictionary(
            static type => type.Name[..^".Tag".Length],
            static type => new ScriptTagTypeId(type.Id.Value),
            StringComparer.Ordinal);

    private static int ResolveRuleList(string name, ScriptApiCatalog api)
    {
        var qualified = name.StartsWith("RuleList.", StringComparison.Ordinal) ? name : "RuleList." + name;
        var constant = api.Constants.SingleOrDefault(item => string.Equals(item.Name, qualified, StringComparison.Ordinal));
        return constant?.Value ?? throw new YamlFormatException($"Unknown RuleList value '{name}'.", default);
    }

    private ContentScriptArtifact? CompileSource(
        ContentScriptScope scope,
        string ownerSection,
        string ownerId,
        string parserName,
        string source,
        ScriptApiCatalog api,
        YamlNode? node,
        string sourcePath)
    {
        _compiledScriptCount = checked(_compiledScriptCount + 1);
        if (_compiledScriptCount > _options.MaximumScripts)
        {
            Invalid($"Script content exceeds the {_options.MaximumScripts}-script limit.",
                node, sourcePath, ownerSection, ownerId);
            return null;
        }
        ScriptParserDefinition definition;
        try
        {
            definition = ScriptParserDefinition.FromCatalog(parserName, api);
        }
        catch (ArgumentException)
        {
            Invalid($"Unknown script parser '{parserName}'.", node, sourcePath, ownerSection, ownerId);
            return null;
        }
        var result = ScriptCompiler.Compile(source, definition, sourcePath);
        foreach (var diagnostic in result.Diagnostics)
        {
            _diagnostics.Report(new DiagnosticEvent(
                diagnostic.Code,
                diagnostic.Severity,
                $"Script '{parserName}' for '{ownerSection}/{ownerId}' failed: {diagnostic.Message}",
                Shift(diagnostic.Source, node?.Span.Start.Line ?? 1),
                new DiagnosticContext(RuleType: ownerSection, RuleId: ownerId, RelatedId: parserName)));
        }
        if (!result.Succeeded)
        {
            return null;
        }
        var artifact = new ContentScriptArtifact(
            scope, ownerSection, ownerId, parserName, sourcePath,
            node?.Span.Start.Line ?? 1, result.Program!);
        if (scope is ContentScriptScope.Default or ContentScriptScope.GlobalEvent)
        {
            _otherScripts.Add(artifact);
        }
        return artifact;
    }

    private void ComposeEvents()
    {
        foreach (var pair in _eventMutations.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var result = ScriptEventComposer.Compose(pair.Value);
            foreach (var diagnostic in result.Diagnostics)
            {
                _diagnostics.Report(diagnostic);
            }
            if (result.Plan is not null)
            {
                _eventPlans.Add(new ContentScriptEventPlan(pair.Key, result.Plan));
            }
        }
    }

    private void Invalid(
        string message,
        YamlNode? node,
        string sourcePath,
        string? section = null,
        string? ruleId = null) => _diagnostics.Report(new DiagnosticEvent(
            ModDiagnosticCodes.InvalidScriptContent,
            DiagnosticSeverity.Error,
            message,
            node?.Span ?? new SourceSpan(sourcePath, new SourcePosition(1, 1, 0), new SourcePosition(1, 1, 0)),
            new DiagnosticContext(RuleType: section, RuleId: ruleId)));

    private static SourceSpan? Shift(SourceSpan? source, int startLine) => source is null ? null : new SourceSpan(
        source.Value.SourceName,
        source.Value.Start with { Line = checked(source.Value.Start.Line + startLine - 1) },
        source.Value.End with { Line = checked(source.Value.End.Line + startLine - 1) });

    private static readonly (string Key, string Value, string ReferencePath)[] DefaultScriptEntries =
    [
        ("recolorItemSprite", "add_shade new_pixel shade; return new_pixel;", "src/Savegame/BattleItem.cpp"),
        ("selectItemSprite", "add sprite_index sprite_offset; return sprite_index;", "src/Savegame/BattleItem.cpp"),
        ("vaporParticleAmmo", "var int temp; var int randMax; set randMax subvoxel_scale; muldiv randMax 3 2; random.randomRangeSymmetric temp randMax; subvoxel_offset.setX temp; random.randomRangeSymmetric temp randMax; subvoxel_offset.setY temp; random.randomRangeSymmetric temp randMax; subvoxel_offset.setZ temp; set temp 320; sub temp particle_density; subvoxel_velocity.setZ temp; return;", "src/Savegame/BattleItem.cpp"),
        ("tryPsiAttackItem", "var int r; random.randomRange r 0 55; add psi_attack_success attack_strength; add psi_attack_success r; sub psi_attack_success defense_strength; sub psi_attack_success distance_strength_reduction; return psi_attack_success;", "src/Savegame/BattleItem.cpp"),
        ("tryMeleeAttackItem", "var int r; random.randomRange r 0 99; sub melee_attack_success r; add melee_attack_success attack_strength; sub melee_attack_success defense_strength; add melee_attack_success defense_strength_penalty; return melee_attack_success;", "src/Savegame/BattleItem.cpp"),
        ("recolorUnitSprite", "unit.getRecolor new_pixel; add_burn_shade new_pixel burn shade; return new_pixel;", "src/Savegame/BattleUnit.cpp"),
        ("selectUnitSprite", "add sprite_index sprite_offset; return sprite_index;", "src/Savegame/BattleUnit.cpp"),
    ];
    private static readonly IReadOnlyDictionary<string, (string Value, string ReferencePath)> DefaultScripts =
        new ReadOnlyDictionary<string, (string Value, string ReferencePath)>(DefaultScriptEntries.ToDictionary(
            static entry => entry.Key,
            static entry => (entry.Value, entry.ReferencePath),
            StringComparer.Ordinal));
}
