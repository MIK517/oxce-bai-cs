using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CampaignSaveRegressionFixtureTests
{
    [Fact]
    public void LegacySoldierFieldsSurviveTwoRewriteCycles()
    {
        var content = LoadContent();
        var yaml = ReadFixture();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var loaded = OxceSaveAdapter.Load(yaml, "legacy-soldier.sav", content,
                new SplitMix64RandomSource(0), Options());
            yaml = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
            Assert.Contains("name: Legacy Soldier", yaml, StringComparison.Ordinal);
            Assert.Contains("tu: 60", yaml, StringComparison.Ordinal);
            Assert.Contains("slot: STR_RIGHT_HAND", yaml, StringComparison.Ordinal);
            Assert.Single(loaded.Campaign.Capture().Bases[0].Soldiers);
        }
    }

    private static string ReadFixture() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "fixtures", "public", "savegames", "legacy-soldier.sav"));

    private static OxceSaveLoadOptions Options() => new("runtime-master",
        new HashSet<string>(["runtime-master", "runtime-addon"], StringComparer.Ordinal));

    private static RuntimeContent LoadContent()
    {
        var fixture = Path.Combine(FindRepositoryRoot(), "fixtures", "public", "mods", "runtime-rule-linking");
        var plan = ModLoadPlanner.Create(ModCatalog.Create(ModDiscovery.ScanDirectory(fixture).Mods),
            [new ModActivation("runtime-master", true), new ModActivation("runtime-addon", true)],
            "runtime-master", new ModEngineIdentity("Extended", "8.6.1.0"));
        return ContentSnapshotBuilder.Build(plan).Content;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
