using BenchmarkDotNet.Attributes;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Phase3;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class Phase3ContentBenchmarks
{
    private string _fixtureRoot = null!;
    private ModLoadPlan _plan = null!;
    private RulesetDocumentCatalog _documents = null!;
    private ContentBuildSession _build = null!;
    private RulesetCatalogNormalizationOptions _normalization = null!;

    [GlobalSetup]
    public void Setup()
    {
        var root = FindRepositoryRoot();
        _fixtureRoot = Path.Combine(root, "fixtures", "public", "mods", "script-content");
        var discovery = ModDiscovery.ScanDirectory(_fixtureRoot);
        _plan = ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        _normalization = new RulesetCatalogNormalizationOptions
        {
            NormalizeSourceName = source => Path.GetRelativePath(_fixtureRoot, source).Replace('\\', '/'),
        };
        _documents = RulesetDocumentCatalog.Parse(_plan);
        _build = Phase3ContentCatalog.Build(_plan);
    }

    [Benchmark(Baseline = true)]
    public byte[] LoadValidateAndNormalizeManifest()
    {
        var build = Phase3ContentCatalog.Build(_plan);
        return Phase3ContentManifestNormalizer.NormalizeToUtf8Json(build, _normalization);
    }

    [Benchmark]
    public RulesetDocumentCatalog ParseRulesetDocuments() =>
        RulesetDocumentCatalog.Parse(_plan);

    [Benchmark]
    public UnresolvedRuleCatalog ComposeParsedDocuments() =>
        RulesetComposer.Compose(_documents);

    [Benchmark]
    public ContentBuildSession LoadAndValidate() =>
        Phase3ContentCatalog.Build(_plan);

    [Benchmark]
    public ContentSnapshot LoadValidateAndCompileScripts() =>
        ContentSnapshotBuilder.Build(_plan);

    [Benchmark]
    public ContentSnapshot LoadValidateCompileScriptsAndRetainAudit() =>
        ContentSnapshotBuilder.Build(
            _plan,
            options: new ContentSnapshotOptions { RetainAuditArtifact = true });

    [Benchmark]
    public byte[] NormalizePrebuiltManifest() =>
        Phase3ContentManifestNormalizer.NormalizeToUtf8Json(_build, _normalization);

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
