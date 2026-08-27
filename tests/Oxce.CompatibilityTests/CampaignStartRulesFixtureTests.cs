using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.CampaignStart;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CampaignStartRulesFixtureTests
{
    [Fact]
    public void CampaignStartRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "campaign-start-rules");
        var expectedPath = Path.Combine(root, "fixtures", "expected", "mods", "campaign-start-rules.expected.json");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var catalog = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(catalog, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"), diagnostics);
        var actual = CampaignStartRuleCatalog.Load(plan, diagnostics);
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));

        var expectedCountry = expected.RootElement.GetProperty("country");
        var country = Assert.Single(actual.Countries.Rules).Value;
        Assert.Equal(expectedCountry.GetProperty("areaCount").GetInt32(), country.Areas.Count);
        Assert.Equal(expectedCountry.GetProperty("fundingBase").GetInt32(), country.FundingBase);
        Assert.Equal(expectedCountry.GetProperty("fundingCap").GetInt32(), country.FundingCap);
        Assert.Equal(expectedCountry.GetProperty("labelLongitude").GetDouble(), country.LabelLongitude, 12);

        var expectedFacilities = expected.RootElement.GetProperty("facilities").EnumerateArray().ToArray();
        Assert.Equal(expectedFacilities.Length, actual.Facilities.Rules.Count);
        for (var index = 0; index < expectedFacilities.Length; index++)
        {
            var expectedFacility = expectedFacilities[index];
            var facility = actual.Facilities.Rules[index];
            Assert.Equal(expectedFacility[0].GetString(), facility.Id);
            Assert.Equal(expectedFacility[1].GetInt32(), facility.Value.ListOrder);
            Assert.Equal(expectedFacility[2].GetInt32(), facility.Value.SizeX);
            Assert.Equal(expectedFacility[3].GetInt32(), facility.Value.SizeY);
        }

        var expectedRegion = expected.RootElement.GetProperty("region");
        var region = Assert.Single(actual.Regions.Rules).Value;
        Assert.Equal(expectedRegion.GetProperty("areaCount").GetInt32(), region.Areas.Count);
        foreach (var property in expectedRegion.GetProperty("missionWeights").EnumerateObject())
            Assert.Equal(property.Value.GetUInt64(), region.MissionWeights[property.Name]);

        var expectedSettings = expected.RootElement.GetProperty("settings");
        var baseTemplate = actual.Settings.GetStartingBase(StartingBaseVariant.Default)!;
        Assert.Equal(expectedSettings.GetProperty("baseFacility").GetString(), First(baseTemplate, "facilities"));
        Assert.Equal(expectedSettings.GetProperty("baseCraft").GetString(), First(baseTemplate, "crafts"));
        Assert.Equal(expectedSettings.GetProperty("initialFunding").GetInt32(), actual.Settings.InitialFunding);
        Assert.Equal(expectedSettings.GetProperty("startingTime").EnumerateArray().Select(item => item.GetInt32()),
            new[] { actual.Settings.StartingTime.Weekday, actual.Settings.StartingTime.Day,
                actual.Settings.StartingTime.Month, actual.Settings.StartingTime.Year, actual.Settings.StartingTime.Hour,
                actual.Settings.StartingTime.Minute, actual.Settings.StartingTime.Second });
        Assert.Equal(expectedSettings.GetProperty("transferCosts").EnumerateArray().Select(item => item.GetInt32()),
            new[] { actual.Settings.GlobalTransferCostMultiplier, actual.Settings.GlobalTransferCostDivisor });
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    private static string First(YamlMappingNode mapping, string key)
    {
        Assert.True(mapping.TryGet(key, out var node));
        return YamlValueReader.ReadString(Assert.Single(Assert.IsType<YamlSequenceNode>(node).Items));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
