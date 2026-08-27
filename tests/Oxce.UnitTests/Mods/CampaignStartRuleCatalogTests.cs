using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.CampaignStart;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class CampaignStartRuleCatalogTests
{
    [Fact]
    public void LoadsGeographyAndFacilityMergeSemantics()
    {
        const string yaml = """
            countries:
              - type: COUNTRY
                refNode:
                  fundingBase: 100
                  areas: [[10, 20, 30, -30]]
                fundingCap: 500
                labelLon: 180
                provideBaseFunc: [FUNC_A]
                customCountryValue: 7
              - update: COUNTRY
                areas: [[350, 10, -20, 20]]
                provideBaseFunc: !add [FUNC_B]
            extraGlobeLabels:
              - type: LABEL
                labelLat: -45
                zoomLevel: 2
            regions:
              - type: REGION
                areas: [[0, 30, -10, 10]]
                missionWeights: {MISSION_A: 5, MISSION_B: 2}
                missionZones: [[[1, 1, 2, 2, 4, CITY]]]
              - update: REGION
                deleteOldAreas: true
                areas: [[30, 60, -20, 20]]
                missionWeights: {MISSION_A: 0, MISSION_C: 3}
            facilities:
              - type: DELETED
              - type: FACILITY
                refNode:
                  size: 2
                  requires: [RESEARCH_A]
                  buildCostItems: {ITEM_A: {build: 2}}
                requires: !add [RESEARCH_B]
                spriteShape: 5
                mapName: MAP_A
                buildCostItems: {ITEM_A: {refund: 1}, ITEM_B: {build: 0, refund: 0}}
                storageTiles: [[-1, -1, -1]]
              - delete: DELETED
              - type: FACILITY_C
                mapName: MAP_C
            """;
        using var fixture = new TemporaryCampaignMod(("fixture.rul", yaml));
        var diagnostics = new DiagnosticCollector();

        var content = CampaignStartRuleCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        var country = Assert.Single(content.Countries.Rules).Value;
        Assert.Equal(100, country.FundingBase);
        Assert.Equal(500, country.FundingCap);
        Assert.Equal(Math.PI, country.LabelLongitude, 12);
        Assert.Equal(2, country.Areas.Count);
        Assert.True(country.Areas[0].LatitudeMinimum < country.Areas[0].LatitudeMaximum);
        Assert.Equal(["FUNC_A", "FUNC_B"], country.ProvidedBaseFunctions);
        Assert.Single(Assert.Single(content.Countries.Rules).DeferredProperties,
            property => property.Key == "customCountryValue");

        var label = Assert.Single(content.GlobeLabels.Rules).Value;
        Assert.Equal(-Math.PI / 4, label.LabelLatitude, 12);
        Assert.Equal(2, label.ZoomLevel);

        var region = Assert.Single(content.Regions.Rules).Value;
        Assert.Single(region.Areas);
        Assert.False(region.MissionWeights.ContainsKey("MISSION_A"));
        Assert.Equal(2ul, region.MissionWeights["MISSION_B"]);
        Assert.Equal(3ul, region.MissionWeights["MISSION_C"]);
        var city = Assert.Single(Assert.Single(region.MissionZones).Areas);
        Assert.True(city.IsPoint);
        Assert.Equal("CITY", city.Name);

        Assert.Equal(["FACILITY", "FACILITY_C"], content.Facilities.Rules.Select(rule => rule.Id));
        var facility = content.Facilities.Rules[0].Value;
        Assert.Equal(200, facility.ListOrder);
        Assert.Equal((2, 2), (facility.SizeX, facility.SizeY));
        Assert.Equal(["RESEARCH_A", "RESEARCH_B"], facility.Requires);
        Assert.Equal(new FacilityItemCost(2, 1), facility.BuildCostItems["ITEM_A"]);
        Assert.False(facility.BuildCostItems.ContainsKey("ITEM_B"));
        Assert.Equal(5, facility.SpriteShape.Index);
        Assert.Equal(300, content.Facilities.Rules[1].Value.ListOrder);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
    }

    [Fact]
    public void ComposesStartingBaseTemplatesAndGlobalDefaultsInFileOrder()
    {
        const string baseRules = """
            startingBase:
              facilities: [A]
              crafts: [C]
            startingTime: {year: 2000, hour: 8}
            startingDifficulty: 1
            initialFunding: 6000
            transferCosts: {globalCostMult: 3}
            baseNamesFirst: [Alpha]
            facilities: [{type: A, mapName: MAP_A}]
            """;
        const string patchRules = """
            startingBase:
              facilities: [B]
            startingBaseBeginner:
              facilities: [BEGINNER]
            startingTime: {minute: 30}
            transferCosts: {globalCostDiv: 2}
            baseNamesFirst: !add [Beta]
            facilities: [{type: B, mapName: MAP_B}]
            """;
        using var fixture = new TemporaryCampaignMod(("20-base.rul", baseRules), ("10-patch.rul", patchRules));

        var settings = CampaignStartRuleCatalog.Load(CreatePlan(fixture.Root)).Settings;

        var defaultBase = settings.GetStartingBase(StartingBaseVariant.Default)!;
        Assert.Equal("B", YamlValueReader.ReadString(((YamlSequenceNode)defaultBase.Entries[0].Value).Items[0]));
        Assert.True(defaultBase.TryGet("crafts", out _));
        var beginner = settings.GetStartingBase(StartingBaseVariant.Beginner)!;
        Assert.Equal("BEGINNER", YamlValueReader.ReadString(((YamlSequenceNode)beginner.Entries[0].Value).Items[0]));
        Assert.Same(defaultBase, settings.GetStartingBase(StartingBaseVariant.Genius));
        Assert.Equal(new CampaignStartTime(6, 1, 1, 2000, 8, 30, 0), settings.StartingTime);
        Assert.Equal(1, settings.StartingDifficulty);
        Assert.Equal(6000, settings.InitialFunding);
        Assert.Equal((3, 2), (settings.GlobalTransferCostMultiplier, settings.GlobalTransferCostDivisor));
        Assert.Equal(["Alpha", "Beta"], settings.BaseNamesFirst);
    }

    [Fact]
    public void ReportsInvalidInternalFacilityRelationships()
    {
        const string yaml = """
            destroyedFacility: MISSING_GLOBAL
            facilities:
              - type: BROKEN
                size: 2
                destroyedFacility: SMALL
                leavesBehindOnSell: [MISSING]
                storageTiles: [[30, 0, 0]]
              - type: SMALL
                mapName: MAP_SMALL
            """;
        using var fixture = new TemporaryCampaignMod(("fixture.rul", yaml));
        var content = CampaignStartRuleCatalog.Load(CreatePlan(fixture.Root));
        var diagnostics = new DiagnosticCollector();

        var result = content.ValidateInternalRelationships(diagnostics);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Property == "destroyedFacility" && issue.RuleId == "BROKEN");
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingRuleReference);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.InvalidRuleRelationship);
        Assert.False(content.Capabilities.Has(ContentLoadStage.Linked));
    }

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        return ModLoadPlanner.Create(catalog, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private sealed class TemporaryCampaignMod : IDisposable
    {
        public TemporaryCampaignMod(params (string Name, string Yaml)[] rulesets)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-campaign-start-test-{Guid.NewGuid():N}");
            var mod = Path.Combine(Root, "fixture");
            var rulesetDirectory = Path.Combine(mod, "Ruleset");
            Directory.CreateDirectory(rulesetDirectory);
            File.WriteAllText(Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\nreservedSpace: 1000\n");
            foreach (var ruleset in rulesets)
                File.WriteAllText(Path.Combine(rulesetDirectory, ruleset.Name), ruleset.Yaml);
        }
        public string Root { get; }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
