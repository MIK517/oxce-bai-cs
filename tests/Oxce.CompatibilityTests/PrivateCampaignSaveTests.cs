using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateCampaignSaveTests
{
    [Fact]
    public void RepresentativeOwnedSavesLoadAndPreserveTheirStrategicSubset()
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var installation = Path.Combine(repository, "artifacts", "private-install");
        var saves = Path.Combine(repository, "fixtures", "private", "saves");
        Assert.SkipUnless(
            File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")) && Directory.Exists(saves),
            "The staged owned installation and private save corpus are not available in this checkout.");

        VerifyFamily(installation, saves, "vanilla-ufo", "xcom1", ["xcom1"],
            expectedFiles: 7, expectedBattleGames: 3, expectedDebugLogs: 6);
        VerifyFamily(installation, saves, "vanilla-tftd", "xcom2", ["xcom2"],
            expectedFiles: 5, expectedBattleGames: 2, expectedDebugLogs: 4);
        VerifyFamily(installation, saves, "modded/rosigma", "40k", ["40k", "40k_ROSIGMA_edits"],
            expectedFiles: 7, expectedBattleGames: 3, expectedDebugLogs: 7);
    }

    private static void VerifyFamily(
        string installation,
        string saves,
        string family,
        string masterId,
        string[] activeMods,
        int expectedFiles,
        int expectedBattleGames,
        int expectedDebugLogs)
    {
        var content = BuildContent(installation, masterId, activeMods);
        var options = new OxceSaveLoadOptions(masterId, activeMods.ToHashSet(StringComparer.Ordinal),
            new YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
        var familyRoot = Path.Combine(saves, family.Replace('/', Path.DirectorySeparatorChar));
        var files = Directory.EnumerateFiles(familyRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".asav", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedFiles, files.Length);

        var battleGames = 0;
        var debugLogs = 0;
        foreach (var path in files)
        {
            var original = File.ReadAllText(path);
            var loaded = OxceSaveAdapter.LoadFile(path, content, new SplitMix64RandomSource(1), options);
            var before = loaded.Campaign.Capture();
            var emitted = OxceSaveAdapter.EmitLoadedCampaign(before, loaded.Source);
            Assert.Contains("---", emitted, StringComparison.Ordinal);
            if (original.Contains("geoscapeDebugLog:", StringComparison.Ordinal))
            {
                debugLogs++;
                Assert.Contains("geoscapeDebugLog:", emitted, StringComparison.Ordinal);
            }
            if (original.Contains("battleGame:", StringComparison.Ordinal))
            {
                battleGames++;
                Assert.Contains("battleGame:", emitted, StringComparison.Ordinal);
            }
            var reloaded = OxceSaveAdapter.Load(
                emitted, Path.GetFileName(path), content, new SplitMix64RandomSource(2), options);
            Assert.Equivalent(before, reloaded.Campaign.Capture(), strict: true);
        }

        Assert.Equal(expectedBattleGames, battleGames);
        Assert.Equal(expectedDebugLogs, debugLogs);
    }

    private static RuntimeContent BuildContent(string installation, string masterId, string[] activeMods)
    {
        var diagnostics = new DiagnosticCollector(100_000);
        var discoveryOptions = new ModDiscoveryOptions { ExternalResourceRoots = [installation] };
        var standard = ModDiscovery.ScanDirectory(Path.Combine(installation, "standard"), diagnostics,
            discoveryOptions);
        var user = ModDiscovery.ScanDirectory(Path.Combine(installation, "user", "mods"), diagnostics,
            discoveryOptions);
        var catalog = ModCatalog.Create(standard.Mods.Concat(user.Mods), diagnostics);
        var plan = ModLoadPlanner.Create(
            catalog,
            activeMods.Select(static id => new ModActivation(id, true)),
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);
        var snapshot = ContentSnapshotBuilder.Build(plan, diagnostics);
        var errors = snapshot.Diagnostics.Where(static item => item.Severity >= DiagnosticSeverity.Error).ToArray();
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked),
            string.Join(Environment.NewLine, errors.Take(25).Select(static item => $"{item.Code}: {item.Message}")));
        return snapshot.Content;
    }

}
