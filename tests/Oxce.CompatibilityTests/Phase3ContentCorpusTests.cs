using System.Text.Json;
using Oxce.Core.Diagnostics;
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
        var root = FindRepositoryRoot();
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
        var root = FindRepositoryRoot();
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
            Assert.True(LoadCorpusRoot(mods, resourceRoot, synthetic) > 0);
        }
        finally
        {
            Directory.Delete(synthetic, recursive: true);
        }
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
        var masters = discovery.Mods.Where(mod => mod.Metadata.IsMaster).ToArray();
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
            var scriptErrors = snapshot.Diagnostics.Where(static item =>
                item.Severity >= DiagnosticSeverity.Error &&
                (item.Code == ModDiagnosticCodes.InvalidScriptContent ||
                 item.Code.StartsWith("OXCE-SCR-", StringComparison.Ordinal))).ToArray();
            Assert.True(scriptErrors.Length == 0, string.Join(
                Environment.NewLine,
                scriptErrors.Take(25).Select(static item => $"{item.Code}: {item.Message}")));
            if (catalog.Capabilities.Has(ContentLoadStage.Linked))
            {
                Assert.True(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled), string.Join(
                Environment.NewLine,
                snapshot.Diagnostics.Where(static item => item.Severity >= DiagnosticSeverity.Error)
                    .Take(25).Select(static item => item.Message)));
            }
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
