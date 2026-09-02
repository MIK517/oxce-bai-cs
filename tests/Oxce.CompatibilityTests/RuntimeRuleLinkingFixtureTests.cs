using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.FixtureSupport;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class RuntimeRuleLinkingFixtureTests
{
    [Fact]
    public void StrategicRuntimeProjectionMatchesPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(
            Path.Combine(root, "fixtures", "manifests", "runtime-rule-linking.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "runtime-rule-linking");
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(ModDiscovery.ScanDirectory(fixture).Mods),
            [new ModActivation("runtime-master", true), new ModActivation("runtime-addon", true)],
            "runtime-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        var diagnostics = new DiagnosticCollector();
        var snapshot = ContentSnapshotBuilder.Build(plan, diagnostics);
        var rules = snapshot.Content.RuntimeRules;
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, manifest.Expected)));
        var rootExpected = expected.RootElement;

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Join(diagnostics));
        Assert.Equal(rootExpected.GetProperty("generationShared").GetBoolean(),
            rules.Generation == snapshot.Content.Resources.Generation);
        var country = rules.Countries[rules.Countries.GetRequired("COUNTRY")];
        Assert.Equal(rootExpected.GetProperty("country")[0].GetString(), country.Id);
        Assert.Equal(rootExpected.GetProperty("country")[1].GetInt32(), country.Value.FundingBase);
        Assert.Equal(rootExpected.GetProperty("country")[2].GetInt32(), country.Value.FundingCap);
        Assert.Equal(rootExpected.GetProperty("country")[3].GetString(),
            rules.Events.GetExternalId(country.Value.SignedPactEvent!.Value));
        Assert.Equal(rootExpected.GetProperty("country")[4].GetString(), country.LastUpdateSource.ModId);

        var facility = rules.Facilities[rules.Facilities.GetRequired("FACILITY")];
        var expectedFacility = rootExpected.GetProperty("facility");
        Assert.Equal(expectedFacility[0].GetString(), facility.Id);
        Assert.Equal(expectedFacility[1].GetInt32(), facility.Value.BuildCost);
        Assert.Equal(expectedFacility[2].GetInt32(), facility.Value.Storage);
        Assert.Equal(expectedFacility[3].GetString(),
            rules.Items.GetExternalId(Assert.Single(facility.Value.BuildCostItems).Item!.Value));
        Assert.Equal(expectedFacility[4].GetString(),
            rules.Facilities.GetExternalId(facility.Value.DestroyedFacility!.Value));
        Assert.Equal(expectedFacility[5].GetString(), facility.LastUpdateSource.ModId);
        Assert.Equal(expectedFacility[6].GetInt32(), facility.Value.SpriteShape.RuntimeIndex);
        Assert.True(facility.Value.SpriteShape.Override.HasValue);

        var region = rules.Regions[rules.Regions.GetRequired("REGION")].Value;
        var expectedRegion = rootExpected.GetProperty("region");
        Assert.Equal(expectedRegion[0].GetString(), rules.Regions.GetExternalId(rules.Regions.GetRequired("REGION")));
        Assert.Equal(expectedRegion[1].GetString(), rules.Regions.GetExternalId(region.MissionRegion!.Value));
        Assert.Equal(expectedRegion[2].GetInt32(), region.BaseCost);

        var craft = rules.Crafts[rules.Crafts.GetRequired("CRAFT")].Value;
        var expectedCraft = rootExpected.GetProperty("craft");
        Assert.Equal(expectedCraft[0].GetString(), rules.Crafts.GetExternalId(rules.Crafts.GetRequired("CRAFT")));
        Assert.Equal(expectedCraft[1].GetInt32(), craft.SoldierCapacity);
        Assert.Equal(expectedCraft[2].GetInt32(), craft.VehicleCapacity);
        Assert.Equal(expectedCraft[3].GetString(), rules.Items.GetExternalId(craft.RefuelItem!.Value));
        Assert.Equal("RESEARCH", Assert.Single(craft.Requirements).Id);

        var item = rules.Items[rules.Items.GetRequired("ITEM")].Value;
        var expectedItem = rootExpected.GetProperty("item");
        Assert.Equal(expectedItem[0].GetString(), rules.Items.GetExternalId(rules.Items.GetRequired("ITEM")));
        Assert.Equal(expectedItem[1].GetInt32(), item.CostBuy);
        Assert.Equal(expectedItem[2].GetInt32(), item.CostSell);
        Assert.Equal(expectedItem[3].GetDouble(), item.Size);
        Assert.Equal("RESEARCH", rules.Research.GetExternalId(Assert.Single(item.Requirements)));

        var soldier = rules.Soldiers[rules.Soldiers.GetRequired("SOLDIER")].Value;
        var expectedSoldier = rootExpected.GetProperty("soldier");
        Assert.Equal(expectedSoldier[0].GetString(), rules.Soldiers.GetExternalId(rules.Soldiers.GetRequired("SOLDIER")));
        Assert.Equal(expectedSoldier[1].GetString(), rules.Armors.GetExternalId(soldier.Armor));
        Assert.Equal(expectedSoldier[2].GetInt32(), soldier.CostBuy);
        Assert.Equal(expectedSoldier[3].GetString(), rules.Skills.GetExternalId(Assert.Single(soldier.Skills)));

        var template = Assert.Single(rules.Campaign.StartingBases);
        var expectedBase = rootExpected.GetProperty("startingBase");
        Assert.Equal(expectedBase[0].GetString(), rules.Facilities.GetExternalId(Assert.Single(template.Facilities).Rule));
        Assert.Equal(expectedBase[1].GetString(), rules.Crafts.GetExternalId(Assert.Single(template.Crafts).Rule));
        Assert.Equal(expectedBase[2].GetString(), rules.Soldiers.GetExternalId(Assert.Single(template.Soldiers).Rule));
        Assert.Equal(expectedBase[3].GetString(), rules.Items.GetExternalId(Assert.Single(template.Items).Rule));
        Assert.Equal(expectedBase[4].GetInt32(), Assert.Single(template.Items).Quantity);
        Assert.Equal(expectedBase[5].GetInt32(), Assert.Single(template.RandomSoldiers).Quantity);

        var time = rules.Campaign.StartingTime;
        Assert.Equal(rootExpected.GetProperty("startingTime").EnumerateArray().Select(static value => value.GetInt32()),
            new[] { time.Weekday, time.Day, time.Month, time.Year, time.Hour, time.Minute, time.Second });
    }

    private static string Join(DiagnosticCollector diagnostics) => string.Join(
        Environment.NewLine,
        diagnostics.Snapshot().Select(static diagnostic => diagnostic.Message));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
