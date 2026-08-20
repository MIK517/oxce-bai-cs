using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ModLoadingFixtureTests
{
    [Fact]
    public void SyntheticFixtureExpandsMasterChainAndOrdersRulesetsLikeReferenceLoader()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "load-order");
        var diagnostics = new DiagnosticCollector();

        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var catalog = ModCatalog.Create(discovery.Mods, diagnostics);
        var engine = new ModEngineIdentity("Extended", "8.6.1.0");
        var activationState = ModActivationReconciler.Reconcile(
            catalog,
            [
                new ModActivation("missing-from-disk", true),
                new ModActivation("addon", true),
                new ModActivation("other-master-addon", true),
            ],
            preferredMasterId: "expansion",
            diagnostics: diagnostics);
        var plan = ModLoadPlanner.Create(
            catalog,
            activationState,
            engine,
            diagnostics);

        Assert.Equal(0, discovery.RejectedCount);
        Assert.True(plan.IsValid);
        Assert.Equal(["xcom1", "expansion", "addon"], plan.Groups.Select(group => group.Mod.Metadata.Id));
        Assert.Equal(
            ["zulu.rul", "alpha.rul"],
            plan.Groups[0].Rulesets.Select(entry => Path.GetFileName(entry.SourcePath)));
        Assert.Equal(
            ["20-second.rul", "10-first.rul"],
            plan.Groups[2].Rulesets.Select(entry => Path.GetFileName(entry.SourcePath)));
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.InactiveForMaster);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingActivation);

        var virtualFiles = plan.CreateVirtualFileCatalog();
        Assert.Equal("addon", virtualFiles.GetRequired("metadata.yml").Provenance.ModId);

        var incompatiblePlan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("incompatible-addon", true)],
            "expansion",
            engine,
            diagnostics);
        Assert.False(incompatiblePlan.IsValid);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.RequiredMasterVersion);
    }

    [Fact]
    public void PrivateModCorpusMetadataScansWithoutUnhandledFailures()
    {
        var root = FindRepositoryRoot();
        var mods = Path.Combine(root, "fixtures", "private", "mods");
        Assert.SkipUnless(Directory.Exists(mods), "Private mod corpus is not available in this checkout.");
        var expected = Directory.EnumerateDirectories(mods)
            .Count(directory => File.Exists(Path.Combine(directory, "metadata.yml")));
        var diagnostics = new DiagnosticCollector();
        var resourceRoot = Directory.GetParent(mods)?.FullName
            ?? throw new DirectoryNotFoundException("Private mod corpus has no resource root.");

        var discovery = ModDiscovery.ScanDirectory(
            mods,
            diagnostics,
            new ModDiscoveryOptions { ExternalResourceRoots = [resourceRoot] });

        Assert.True(discovery.Mods.Count + discovery.RejectedCount >= expected);
        Assert.DoesNotContain(
            diagnostics.Snapshot(),
            item => item.Severity is DiagnosticSeverity.Critical);
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
