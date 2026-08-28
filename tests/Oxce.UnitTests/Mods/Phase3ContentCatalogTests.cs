using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Phase3;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class Phase3ContentCatalogTests
{
    [Fact]
    public void EmptyContentGraphAdvancesThroughLinkedStage()
    {
        using var fixture = new TemporaryMod("{}");
        var diagnostics = new DiagnosticCollector();

        var catalog = Phase3ContentCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        Assert.True(catalog.Capabilities.Has(ContentLoadStage.Linked));
        Assert.True(catalog.Validation.IsValid);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    [Fact]
    public void MissingRuntimeReferenceAdvancesToLinkedStageWithWarning()
    {
        using var fixture = new TemporaryMod("events: [{name: EVENT, everyItemList: [MISSING]}]");
        var diagnostics = new DiagnosticCollector();

        var catalog = Phase3ContentCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        Assert.True(catalog.Capabilities.Has(ContentLoadStage.Typed));
        Assert.True(catalog.Capabilities.Has(ContentLoadStage.Linked));
        Assert.True(catalog.Validation.IsValid);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DeferredRuleReference);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    [Fact]
    public void ClosurePassValidatesReferencesOwnedByLaterFamilies()
    {
        using var fixture = new TemporaryMod("countries: [{type: COUNTRY, signedPactEvent: MISSING_EVENT}]");

        var catalog = Phase3ContentCatalog.Load(CreatePlan(fixture.Root));

        Assert.False(catalog.Capabilities.Has(ContentLoadStage.Linked));
        Assert.Contains(catalog.Validation.Closure.Issues,
            issue => issue.RuleId == "COUNTRY" && issue.Property == "signedPactEvent" &&
                issue.TargetSection == "events");
    }

    [Fact]
    public void ManifestIsDeterministicSourceNormalizedAndBounded()
    {
        using var fixture = new TemporaryMod("items: [{type: ITEM}]");
        var plan = CreatePlan(fixture.Root);
        var build = Phase3ContentCatalog.Build(plan);
        var catalog = build.Catalog;
        Assert.Equal(1, build.ParsedFileCount);
        var options = new RulesetCatalogNormalizationOptions
        {
            NormalizeSourceName = source => Path.GetRelativePath(fixture.Root, source).Replace('\\', '/'),
        };

        var first = Phase3ContentManifestNormalizer.NormalizeToJson(build, options);
        var second = Phase3ContentManifestNormalizer.NormalizeToJson(build, options);

        Assert.Equal(first, second);
        Assert.Contains("\"stage\": \"linked\"", first, StringComparison.Ordinal);
        Assert.Contains("\"path\": \"fixture/Ruleset/fixture.rul\"", first, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"items\"", first, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"ITEM\"", first, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => Phase3ContentManifestNormalizer.NormalizeToUtf8Json(
            build, options with { MaximumOutputBytes = 100 }));
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
            Root = Path.Combine(Path.GetTempPath(), "oxce-phase3-" + Guid.NewGuid().ToString("N"));
            var mod = Path.Combine(Root, "fixture");
            Directory.CreateDirectory(Path.Combine(mod, "Ruleset"));
            File.WriteAllText(Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\n");
            File.WriteAllText(Path.Combine(mod, "Ruleset", "fixture.rul"), rules);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
