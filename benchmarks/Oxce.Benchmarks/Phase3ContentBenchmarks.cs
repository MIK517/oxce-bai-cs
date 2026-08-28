using BenchmarkDotNet.Attributes;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Phase3;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class Phase3ContentBenchmarks
{
    private string _fixtureRoot = null!;
    private ModLoadPlan _plan = null!;
    private RulesetCatalogNormalizationOptions _normalization = null!;

    [GlobalSetup]
    public void Setup()
    {
        var root = FindRepositoryRoot();
        _fixtureRoot = Path.Combine(root, "fixtures", "public", "mods", "mission-event-rules");
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
    }

    [Benchmark]
    public byte[] LoadValidateAndNormalizeManifest()
    {
        var catalog = Phase3ContentCatalog.Load(_plan);
        return Phase3ContentManifestNormalizer.NormalizeToUtf8Json(_plan, catalog, _normalization);
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
