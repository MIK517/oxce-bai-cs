using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CampaignSaveRegressionFixtureTests
{
    [Fact]
    public void MalformedCountryCollectionFailsBeforeCampaignPublication()
    {
        var yaml = ReadFixture();
        var start = yaml.IndexOf("countries:", StringComparison.Ordinal);
        var end = yaml.IndexOf("regions:", start, StringComparison.Ordinal);
        yaml = yaml[..start] + "countries: malformed\n" + yaml[end..];
        var error = Assert.Throws<InvalidDataException>(() => OxceSaveAdapter.Load(
            yaml, "malformed.sav", LoadContent(), new SplitMix64RandomSource(0), Options()));
        Assert.Equal("OXCE save 'countries' must be a sequence.", error.Message);
    }

    [Fact]
    public void UnsupportedMonthBoundaryStopsWithoutChangingCalendarAndSurvivesReload()
    {
        var content = LoadContent();
        var loaded = OxceSaveAdapter.Load(ReadFixture(), "legacy-soldier.sav", content,
            new SplitMix64RandomSource(0), Options());
        var result = loaded.Campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Single(result.Events.OfType<CampaignActionBlocked>());
        var yaml = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        var restored = OxceSaveAdapter.Load(yaml, "advanced.sav", content,
            new SplitMix64RandomSource(0), Options()).Campaign.Capture();
        Assert.Equal(30, restored.DaysPassed);
        Assert.Equal(-1, restored.MonthsPassed);
        Assert.Equal(new CampaignTime(1, 31, 1, 1999, 23, 59, 55), restored.Time);
    }

    [Fact]
    public void SoldierSidecarFollowsTransitReorderAndArrivalAcrossRepeatedRewrites()
    {
        var content = LoadContent();
        var loaded = OxceSaveAdapter.Load(ReadFixture(), "mobile.sav", content, new SplitMix64RandomSource(0), Options());
        var snapshot = loaded.Campaign.Capture();
        var first = snapshot.Bases[0];
        var soldier = Assert.Single(first.Soldiers);
        var second = first with { Id = 2, Name = "Beta", Soldiers = [], Transfers = [new(1, 6, CampaignTransferKind.Soldier, soldier.RuleId, 1, soldier)] };
        snapshot = snapshot with { Bases = [second, first with { Soldiers = [] }] };
        var yaml = OxceSaveAdapter.EmitLoadedCampaign(snapshot, loaded.Source);
        Assert.Contains("name: Legacy Soldier", yaml, StringComparison.Ordinal);
        loaded = OxceSaveAdapter.Load(yaml, "transit.sav", content, new SplitMix64RandomSource(0), Options());
        var transit = loaded.Campaign.Capture();
        var incoming = Assert.Single(transit.Bases[0].Transfers);
        Assert.Equal(soldier, incoming.Soldier);
        snapshot = transit with { Bases = [transit.Bases[1], transit.Bases[0] with { Soldiers = [incoming.Soldier!], Transfers = [] }] };
        for (var cycle = 0; cycle < 3; cycle++)
        {
            yaml = OxceSaveAdapter.EmitLoadedCampaign(snapshot, loaded.Source);
            loaded = OxceSaveAdapter.Load(yaml, "arrived.sav", content, new SplitMix64RandomSource(0), Options());
            snapshot = loaded.Campaign.Capture();
            Assert.Contains("name: Legacy Soldier", yaml, StringComparison.Ordinal);
            Assert.Contains("slot: STR_RIGHT_HAND", yaml, StringComparison.Ordinal);
            Assert.Empty(snapshot.Bases.SelectMany(b => b.Transfers));
            Assert.Single(snapshot.Bases.SelectMany(b => b.Soldiers));
        }
    }

    [Fact]
    public void RecreatedSoldierCannotInheritDeletedSoldiersOpaqueFields()
    {
        var content = LoadContent();
        var loaded = OxceSaveAdapter.Load(ReadFixture(), "recreated.sav", content, new SplitMix64RandomSource(0), Options());
        var snapshot = loaded.Campaign.Capture();
        var original = snapshot.Bases[0];
        var oldSoldier = Assert.Single(original.Soldiers);
        snapshot = snapshot with { Bases = [original with { Soldiers = [new(oldSoldier.RuleId, oldSoldier.Id)] }] };
        var yaml = OxceSaveAdapter.EmitLoadedCampaign(snapshot, loaded.Source);
        Assert.DoesNotContain("Legacy Soldier", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("equipmentLayout", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateOwnershipAcrossBaseAndTransitFailsBeforePublication()
    {
        var content = LoadContent();
        var loaded = OxceSaveAdapter.Load(ReadFixture(), "duplicate.sav", content, new SplitMix64RandomSource(0), Options());
        var snapshot = loaded.Campaign.Capture();
        var first = snapshot.Bases[0];
        var soldier = Assert.Single(first.Soldiers);
        var invalid = snapshot with { Bases = [first with { Transfers = [new(1, 6, CampaignTransferKind.Soldier, soldier.RuleId, 1, soldier)] }] };
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(invalid, content, new SplitMix64RandomSource(0)));
        Assert.Throws<InvalidDataException>(() => OxceSaveAdapter.EmitLoadedCampaign(invalid, loaded.Source));
    }

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
