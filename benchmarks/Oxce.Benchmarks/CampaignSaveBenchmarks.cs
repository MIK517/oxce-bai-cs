using BenchmarkDotNet.Attributes;
using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class CampaignSaveBenchmarks
{
    private RuntimeContent _content = null!;
    private CampaignState _campaign = null!;
    private CampaignSnapshot _snapshot = null!;
    private OxceSaveDocument _source = null!;
    private string _yaml = null!;
    private OxceSaveLoadOptions _options = null!;

    [GlobalSetup]
    public void Setup()
    {
        var repository = FindRepositoryRoot();
        var installation = Path.Combine(repository, "artifacts", "private-install");
        var input = Path.Combine(repository, "fixtures", "private", "saves", "modded", "rosigma", "early",
            "Begining.sav");
        if (!File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")) || !File.Exists(input))
            throw new InvalidOperationException("Stage the owned installation and private saves before this benchmark.");
        var discoveryOptions = new ModDiscoveryOptions { ExternalResourceRoots = [installation] };
        var standard = ModDiscovery.ScanDirectory(Path.Combine(installation, "standard"), options: discoveryOptions);
        var user = ModDiscovery.ScanDirectory(Path.Combine(installation, "user", "mods"), options: discoveryOptions);
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(standard.Mods.Concat(user.Mods)),
            [new ModActivation("40k", true), new ModActivation("40k_ROSIGMA_edits", true)],
            "40k",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        _content = ContentSnapshotBuilder.Build(plan).Content;
        _options = new OxceSaveLoadOptions(
            "40k",
            new HashSet<string>(["40k", "40k_ROSIGMA_edits"], StringComparer.Ordinal),
            new YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
        var loaded = OxceSaveAdapter.LoadFile(input, _content, new SplitMix64RandomSource(1), _options);
        _campaign = loaded.Campaign;
        _source = loaded.Source;
        _snapshot = _campaign.Capture();
        _yaml = OxceSaveAdapter.EmitLoadedCampaign(_snapshot, _source);
    }

    [Benchmark]
    public CampaignSnapshot Capture() => _campaign.Capture();

    [Benchmark]
    public string Emit() => OxceSaveAdapter.EmitLoadedCampaign(_snapshot, _source);

    [Benchmark]
    public CampaignState ParseAndRestore() => OxceSaveAdapter.Load(
        _yaml, "benchmark.sav", _content, new SplitMix64RandomSource(1), _options).Campaign;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
