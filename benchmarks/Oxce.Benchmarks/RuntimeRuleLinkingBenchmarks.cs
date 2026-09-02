using BenchmarkDotNet.Attributes;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class RuntimeRuleLinkingBenchmarks
{
    private RuntimeContent _content = null!;
    private RuleHandle<CountryRuleFamily> _country;

    [GlobalSetup]
    public void Setup()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "runtime-rule-linking");
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(ModDiscovery.ScanDirectory(fixture).Mods),
            [new ModActivation("runtime-master", true), new ModActivation("runtime-addon", true)],
            "runtime-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        _content = ContentSnapshotBuilder.Build(plan).Content;
        _country = _content.RuntimeRules.Countries.GetRequired("COUNTRY");
    }

    [Benchmark(Baseline = true)]
    public int CompatibilityModelStringLookup()
    {
        _content.Catalog.CampaignStart.Countries.TryGet("COUNTRY", out var rule);
        return rule!.Value.FundingCap;
    }

    [Benchmark]
    public int RuntimeModelStringLookup()
    {
        var handle = _content.RuntimeRules.Countries.GetRequired("COUNTRY");
        return _content.RuntimeRules.Countries[handle].Value.FundingCap;
    }

    [Benchmark]
    public int LinkedRuntimeAccess() => _content.RuntimeRules.Countries[_country].Value.FundingCap;

    [Benchmark]
    public string LinkedRelationshipAccess()
    {
        var country = _content.RuntimeRules.Countries[_country].Value;
        return _content.RuntimeRules.Events.GetExternalId(country.SignedPactEvent!.Value);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
