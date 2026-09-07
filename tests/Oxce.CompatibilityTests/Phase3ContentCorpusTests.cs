using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Phase3;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class Phase3ContentCorpusTests
{
    [Fact]
    public void EveryPublicModFixtureLoadsThroughAggregateCatalogAndProducesManifest()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var fixtures = Path.Combine(root, "fixtures", "public", "mods");
        var loaded = 0;

        foreach (var fixture in Directory.EnumerateDirectories(fixtures).Order(StringComparer.Ordinal))
        {
            var fixtureCount = LoadCorpusRoot(fixture, externalResourceRoot: null);
            Assert.True(fixtureCount > 0, $"Public fixture '{Path.GetFileName(fixture)}' has no loadable master.");
            loaded += fixtureCount;
        }

        Assert.True(loaded > 0);
    }

    [Fact]
    public void AvailablePrivateModCorpusLoadsThroughAggregateCatalogAndProducesManifest()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var mods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(Directory.Exists(mods), "Private mod corpus is not available in this checkout.");
        var resourceRoot = Directory.GetParent(mods)?.FullName
            ?? throw new DirectoryNotFoundException("Private mod corpus has no resource root.");

        var synthetic = Path.Combine(Path.GetTempPath(), "oxce-private-master-" + Guid.NewGuid().ToString("N"));
        try
        {
            var master = Path.Combine(synthetic, "xcom1");
            Directory.CreateDirectory(master);
            File.WriteAllText(Path.Combine(master, "metadata.yml"),
                "id: xcom1\nname: Synthetic corpus master\nversion: 1.0\nisMaster: true\n");
            Assert.True(LoadCorpusRoot(mods, resourceRoot, synthetic) >= 2,
                "The private corpus must exercise both the synthetic xcom1 master and its native master.");
        }
        finally
        {
            Directory.Delete(synthetic, recursive: true);
        }
    }

    [Fact]
    public void FinalModPackUpdatesStandardFirestormArticleWithoutRepeatingItsType()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var installation = Path.Combine(root, "artifacts", "private-install");
        var privateMods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(
            File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")) &&
            Directory.Exists(Path.Combine(privateMods, "Final Mod Pack")),
            "The private installation and Final Mod Pack corpus are not staged.");
        var discoveryOptions = new ModDiscoveryOptions
        {
            ExternalResourceRoots = [installation, Path.Combine(root, "fixtures", "private")],
        };
        var standardMaster = ModDiscovery.ScanDirectory(
                Path.Combine(installation, "standard"),
                options: discoveryOptions)
            .Mods.Where(mod => mod.Metadata.Id == "xcom1");
        var addOns = ModDiscovery.ScanDirectory(privateMods, options: discoveryOptions).Mods;
        var catalog = ModCatalog.Create(standardMaster.Concat(addOns));
        var plan = ModLoadPlanner.Create(
            catalog,
            [new("xcom1", true), new("final-mod-pack", true)],
            "xcom1",
            new("Extended", "8.6.1.0"));
        Assert.True(plan.IsValid);

        var snapshot = ContentSnapshotBuilder.Build(plan);

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.Linked), string.Join(
            Environment.NewLine,
            snapshot.Diagnostics.Where(item => item.Severity >= DiagnosticSeverity.Error)
                .Take(25).Select(item => item.Message)));
        var article = snapshot.CompatibilityData.Catalog.MissionEvents.Ufopaedia["STR_FIRESTORM"];
        Assert.Equal(1, article.TypeId);
        Assert.Equal("final-mod-pack", article.LastUpdateSource.ModId);
        Assert.Contains("rect_stats", article.StructuredProperties);
        Assert.Contains("rect_text", article.StructuredProperties);
        Assert.DoesNotContain(snapshot.Diagnostics, item =>
            item.Code == ModDiagnosticCodes.SkippedUfopaediaArticle &&
            item.Context.RuleId == "STR_FIRESTORM");
    }

    private static int LoadCorpusRoot(
        string modsRoot,
        string? externalResourceRoot,
        string? supplementalModsRoot = null)
    {
        var discoveryOptions = externalResourceRoot is null
            ? null
            : new ModDiscoveryOptions { ExternalResourceRoots = [externalResourceRoot] };
        var discovery = ModDiscovery.ScanDirectory(modsRoot, options: discoveryOptions);
        var candidates = discovery.Mods.AsEnumerable();
        if (supplementalModsRoot is not null)
        {
            candidates = candidates.Concat(ModDiscovery.ScanDirectory(supplementalModsRoot).Mods);
        }

        var modCatalog = ModCatalog.Create(candidates);
        var masters = modCatalog.Mods.Values.Where(mod => mod.Metadata.IsMaster).ToArray();
        foreach (var master in masters)
        {
            var activations = modCatalog.Mods.Values
                .Where(mod => string.Equals(mod.Metadata.Id, master.Metadata.Id, StringComparison.Ordinal) ||
                    (!mod.Metadata.IsMaster && mod.Metadata.CanActivate(master.Metadata.Id) &&
                     (mod.Metadata.RequiredMasterVersion is null ||
                      master.Metadata.Version.Satisfies(mod.Metadata.RequiredMasterVersion))))
                .OrderBy(mod => mod.Metadata.Id, StringComparer.Ordinal)
                .Select(mod => new ModActivation(mod.Metadata.Id, true));
            var plan = ModLoadPlanner.Create(
                modCatalog,
                activations,
                master.Metadata.Id,
                new ModEngineIdentity("Extended", "8.6.1.0"));
            Assert.True(plan.IsValid);
            var snapshot = ContentSnapshotBuilder.Build(
                plan,
                options: new ContentSnapshotOptions { RetainAuditArtifact = true });
            using var auditArtifact = Assert.IsType<ContentAuditArtifact>(snapshot.AuditArtifact);
            var content = snapshot.Content;
            var catalog = snapshot.CompatibilityData.Catalog;
            Assert.True(catalog.Capabilities.Has(ContentLoadStage.Typed));
            var errors = snapshot.Diagnostics.Where(static item =>
                item.Severity >= DiagnosticSeverity.Error).ToArray();
            var expectation = ExpectedOutcome(Path.GetFileName(modsRoot), master.Metadata.Id);
            Assert.Equal(expectation.CatalogStages, catalog.Capabilities.Stages);
            Assert.Equal(expectation.SnapshotStages, snapshot.Capabilities.Stages);
            Assert.Equal(
                expectation.ErrorCounts.OrderBy(static item => item.Key),
                errors.GroupBy(static item => item.Code)
                    .ToDictionary(static group => group.Key, static group => group.Count())
                    .OrderBy(static item => item.Key));
            var manifest = Phase3ContentManifestNormalizer.NormalizeToUtf8Json(
                snapshot,
                auditArtifact,
                new RulesetCatalogNormalizationOptions
                {
                    NormalizeSourceName = source => Path.GetRelativePath(modsRoot, source).Replace('\\', '/'),
                });
            using var document = JsonDocument.Parse(manifest);
            Assert.Equal(Phase3ContentManifestNormalizer.SchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
        }

        return masters.Length;
    }

    private static CorpusOutcome ExpectedOutcome(string corpusName, string masterId) =>
        (corpusName, masterId) switch
        {
            ("campaign-start-rules", "fixture") => new(
                ContentLoadStage.Composed | ContentLoadStage.Typed | ContentLoadStage.Linked,
                ContentLoadStage.Composed | ContentLoadStage.Typed | ContentLoadStage.Linked |
                    ContentLoadStage.ResourcesResolved | ContentLoadStage.ScriptsCompiled,
                new Dictionary<string, int> { [ModDiagnosticCodes.InvalidRuntimeRuleLink] = 2 }),
            ("rule-operations", "fixture-master") => new(
                ContentLoadStage.Composed | ContentLoadStage.Typed,
                ContentLoadStage.Composed | ContentLoadStage.Typed,
                new Dictionary<string, int>
                {
                    [ModDiagnosticCodes.DuplicateNewRule] = 1,
                    [ModDiagnosticCodes.MissingOverrideRule] = 1,
                }),
            ("mods", "40k") => new(
                ContentLoadStage.Composed | ContentLoadStage.Typed,
                ContentLoadStage.Composed | ContentLoadStage.Typed,
                new Dictionary<string, int>
                {
                    [ModDiagnosticCodes.MissingRuleReference] = 220,
                    [ModDiagnosticCodes.InvalidRuleRelationship] = 62,
                }),
            ("mods", "xcom1") => new(
                ContentLoadStage.Composed | ContentLoadStage.Typed,
                ContentLoadStage.Composed | ContentLoadStage.Typed,
                new Dictionary<string, int>
                {
                    [ModDiagnosticCodes.MissingRuleReference] = 653,
                    [ModDiagnosticCodes.InvalidRuleRelationship] = 3,
                    [ModDiagnosticCodes.InvalidRuntimeRuleLink] = 13,
                }),
            _ => new(
                ContentLoadStage.Composed | ContentLoadStage.Typed | ContentLoadStage.Linked,
                ContentLoadStage.Composed | ContentLoadStage.Typed | ContentLoadStage.Linked |
                    ContentLoadStage.ResourcesResolved | ContentLoadStage.ScriptsCompiled |
                    ContentLoadStage.RuntimeLinked,
                new Dictionary<string, int>()),
        };

    private sealed record CorpusOutcome(
        ContentLoadStage CatalogStages,
        ContentLoadStage SnapshotStages,
        IReadOnlyDictionary<string, int> ErrorCounts);

}
