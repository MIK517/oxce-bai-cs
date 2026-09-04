using System.IO.Compression;
using System.Runtime.CompilerServices;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Runtime;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ContentSnapshotTests
{
    [Fact]
    public void EmptyGraphCompilesReferenceDefaultsAndReachesScriptsCompiled()
    {
        using var fixture = new TemporaryMod("{}");

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled), Diagnostics(snapshot));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
        Assert.Equal(7, snapshot.Scripts.Count(script => script.Scope == ContentScriptScope.Default));
        Assert.Equal(1, snapshot.Content.ParsedFileCount);
        Assert.Equal(snapshot.Content.Resources.Generation, snapshot.Content.RuntimeRules.Generation);
        Assert.True(snapshot.Measurements.RuntimeRuleLinking.AllocatedBytes > 0);
        Assert.Null(snapshot.AuditArtifact);
    }

    [Fact]
    public void PreservedDocumentsCompileTagsEventsRuleScriptsStatsAndInitialValues()
    {
        const string rules = """
            extended:
              tags:
                RuleMod:
                  GLOBAL_POWER: int
                RuleItem:
                  POWER: int
              globals:
                GLOBAL_POWER: 3
              scripts:
                newTurnItem:
                  - new: fixture-event
                    offset: 1.25
                    code: return;
            items:
              - type: ITEM
                damageBonus:
                  strength: 0.25
                  healthCurrent: [0.1, 0.2]
                tags:
                  POWER: 7
                scripts:
                  newTurnItem: return;
                  selectItemSprite: add sprite_index RuleList.current; return sprite_index;
            """;
        using var fixture = new TemporaryMod(rules);

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled), Diagnostics(snapshot));
        Assert.Collection(snapshot.Tags.Tags, static _ => { }, static _ => { });
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.GlobalEvent &&
            script.ParserName == "newTurnItem");
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.Rule &&
            script.OwnerId == "ITEM" && script.ParserName == "newTurnItem");
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.Rule &&
            script.OwnerId == "ITEM" && script.ParserName == "selectItemSprite");
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.StatBonus &&
            script.OwnerId == "ITEM" && script.ParserName == "damageBonusBonusStats");
        Assert.Single(snapshot.EventPlans);
        Assert.Collection(snapshot.InitialValues, static _ => { }, static _ => { });
        var value = Assert.Single(snapshot.InitialValues, item => item.OwnerId == "ITEM");
        Assert.Equal("Tag.POWER", value.TagName);
        Assert.Equal(7, value.Value);
        Assert.Contains(snapshot.InitialValues,
            item => item.OwnerSection == "extended" && item.TagName == "Tag.GLOBAL_POWER" && item.Value == 3);
    }

    [Fact]
    public void ScriptErrorsAndDroppedDiagnosticsCannotPublishFalseSuccess()
    {
        const string rules = """
            items:
              - type: FIRST
                scripts: {newTurnItem: "missing_operation; return;"}
              - type: SECOND
                scripts: {newTurnItem: "also_missing; return;"}
            """;
        using var fixture = new TemporaryMod(rules);

        var snapshot = ContentSnapshotBuilder.Build(
            CreatePlan(fixture.Root),
            options: new ContentSnapshotOptions { MaximumDiagnostics = 1 });

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
        Assert.True(snapshot.ReportedDiagnosticCount > 1);
        Assert.Single(snapshot.Diagnostics);
        Assert.True(snapshot.DroppedDiagnosticCount > 0);
    }

    [Fact]
    public void MissingTagsFileFailsIntentionally()
    {
        using var fixture = new TemporaryMod("extended: {tagsFile: Ruleset/missing.rul}");

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == ModDiagnosticCodes.InvalidScriptContent &&
            diagnostic.Message.Contains("Unknown file name", StringComparison.Ordinal));
    }

    [Fact]
    public void MultiModScopesPreserveFileVisibilityAndAuditOwnershipIsExplicit()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "content-ownership");
        var discovery = ModDiscovery.ScanDirectory(fixture);
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("ownership-master", true), new ModActivation("ownership-addon", true)],
            "ownership-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));

        var runtimeOnly = ContentSnapshotBuilder.Build(plan);

        Assert.Null(runtimeOnly.AuditArtifact);
        Assert.Equal(4, runtimeOnly.Content.ParsedFileCount);
        Assert.Equal(4, runtimeOnly.SourceScopeCount);
        Assert.Equal(2, runtimeOnly.ApiScopeCount);
        Assert.Equal(runtimeOnly.ApiScopeCount + 1, runtimeOnly.TagCatalogBuildCount);
        Assert.True(runtimeOnly.Measurements.Parse.AllocatedBytes > 0);
        Assert.True(runtimeOnly.Measurements.ScriptCompilation.AllocatedBytes > 0);
        Assert.Contains(runtimeOnly.InitialValues,
            value => value.OwnerId == "MASTER_ONLY" && value.TagName == "Tag.MASTER_POWER" && value.Value == 0);
        Assert.Contains(runtimeOnly.InitialValues,
            value => value.OwnerId == "ADDON_ONLY" && value.TagName == "Tag.ADDON_POWER" && value.Value == 1);
        Assert.Equal(0, ExecuteSpriteScript(runtimeOnly, "MASTER_ONLY"));
        Assert.Equal(1, ExecuteSpriteScript(runtimeOnly, "ADDON_ONLY"));
        Assert.True(runtimeOnly.CompatibilityData.Catalog.Items.Items.TryGet("SHARED_ITEM", out var shared));
        Assert.Contains(shared!.CompatibilityData.DeferredProperties,
            property => property.Key == "customCompatibilityPayload");

        var audited = ContentSnapshotBuilder.Build(
            plan,
            options: new ContentSnapshotOptions { RetainAuditArtifact = true });
        var audit = Assert.IsType<ContentAuditArtifact>(audited.AuditArtifact);
        Assert.Equal(4, audit.Documents.ParsedFileCount);
        Assert.NotEmpty(audit.ComposedRules.Sections);
        audit.Dispose();
        Assert.True(audit.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => audit.Documents);
        Assert.True(audited.Content.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
    }

    [Fact]
    public void ScriptScopesUseDocumentIdentityWhenArchivePathsDifferOnlyByCase()
    {
        using var fixture = new TemporaryArchiveMod(
            """
            items:
              - type: EARLY
                scripts:
                  selectItemSprite: add sprite_index Tag.LATE; return sprite_index;
            """,
            """
            extended:
              tags:
                RuleItem:
                  LATE: int
            """);

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.Equal(2, snapshot.Content.ParsedFileCount);
        Assert.Equal(2, snapshot.SourceScopeCount);
        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
        Assert.Contains(snapshot.Diagnostics, static diagnostic =>
            diagnostic.Code == ScriptDiagnosticCodes.UnknownSymbol &&
            diagnostic.Message.Contains("Tag.LATE", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleasedAuditGraphsAreNotReachableFromRuntimeContent()
    {
        using var fixture = new TemporaryMod("items: [{type: ITEM, customPayload: {value: 1}}]");
        var (runtime, compatibility, documents, composed) = BuildRuntimeAndReleaseAudit(CreatePlan(fixture.Root));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(compatibility.IsAlive);
        Assert.False(documents.IsAlive);
        Assert.False(composed.IsAlive);
        Assert.True(runtime.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
        GC.KeepAlive(runtime);
    }

    private static string Diagnostics(ContentSnapshot snapshot) => string.Join(
        Environment.NewLine,
        snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message));

    private static int ExecuteSpriteScript(ContentSnapshot snapshot, string ownerId)
    {
        var artifact = Assert.Single(snapshot.Scripts,
            script => script.OwnerId == ownerId && script.ParserName == "selectItemSprite");
        var result = ScriptVm.Execute(
            artifact.Program,
            new Dictionary<string, int> { ["sprite_index"] = 0 });
        Assert.True(result.Succeeded);
        return result.Outputs["sprite_index"];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        RuntimeContent Runtime,
        WeakReference Compatibility,
        WeakReference Documents,
        WeakReference Composed)
        BuildRuntimeAndReleaseAudit(ModLoadPlan plan)
    {
        var snapshot = ContentSnapshotBuilder.Build(
            plan,
            options: new ContentSnapshotOptions { RetainAuditArtifact = true });
        var audit = snapshot.AuditArtifact!;
        var compatibility = new WeakReference(snapshot.CompatibilityData);
        var documents = new WeakReference(audit.Documents);
        var composed = new WeakReference(audit.ComposedRules);
        audit.Dispose();
        return (snapshot.Content, compatibility, documents, composed);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        return ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private sealed class TemporaryMod : IDisposable
    {
        public TemporaryMod(string rules)
        {
            Root = Path.Combine(Path.GetTempPath(), "oxce-content-snapshot-" + Guid.NewGuid().ToString("N"));
            var mod = Path.Combine(Root, "fixture");
            Directory.CreateDirectory(Path.Combine(mod, "Ruleset"));
            File.WriteAllText(Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\n");
            File.WriteAllText(Path.Combine(mod, "Ruleset", "fixture.rul"), rules);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class TemporaryArchiveMod : IDisposable
    {
        public TemporaryArchiveMod(string firstRules, string secondRules)
        {
            Root = Path.Combine(Path.GetTempPath(), "oxce-content-archive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            using var archive = ZipFile.Open(Path.Combine(Root, "fixture.zip"), ZipArchiveMode.Create);
            Write(archive, "metadata.yml", "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\n");
            Write(archive, "Ruleset/a.rul", firstRules);
            Write(archive, "Ruleset/A.rul", secondRules);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static void Write(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
