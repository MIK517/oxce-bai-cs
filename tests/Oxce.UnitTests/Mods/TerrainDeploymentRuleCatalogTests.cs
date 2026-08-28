using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.TerrainDeployment;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class TerrainDeploymentRuleCatalogTests
{
    [Fact]
    public void LoadsTerrainFamiliesAndSpecialSectionsWithReferenceMerges()
    {
        const string baseYaml = """
            mapScripts:
              - type: MAP
                commands:
                  - type: addCraft
                    size: 2
                    freqs: 7
                    label: -4
              - type: OLD
                commands: [{type: fillArea}]
            MCDPatches:
              - type: URBAN
                data: [{MCDIndex: 1, bigWall: 2}]
            terrains:
              - name: TERRAIN
                mapDataSets: [URBAN]
                mapBlocks: [{name: BLOCK, width: 20, length: 10}]
                civilianTypes: !add [CIVILIAN]
                depth: [1, 3]
                script: MAP
            alienRaces:
              - id: RACE
                members: [UNIT]
                retaliationMissionWeights: {0: {MISSION: 2}}
            enviroEffects:
              - type: ENV
                environmentalConditions: {STR_HOSTILE: {chancePerTurn: 5}}
                paletteTransformations: {PAL_A: PAL_B}
            startingConditions:
              - type: START
                allowedItems: [ITEM]
                requiredItems: {ITEM: 1}
            alienDeployments:
              - type: DEPLOY
                terrains: [TERRAIN]
                race: RACE
                enviroEffects: ENV
                startingCondition: START
                script: MAP
                data: [{alienRank: 1}]
                depth: [2, 4]
            """;
        const string patchYaml = """
            mapScripts:
              - type: OLD
                delete: OLD
                commands: [{type: resize, size: [4, 5]}]
            MCDPatches:
              - type: URBAN
                data: [{MCDIndex: 2, noFloor: true, LOFTS: [1, 2]}]
            terrains:
              - update: TERRAIN
                addOnly: true
                mapBlocks: [{name: EXTRA}]
                civilianTypes: !remove [FEMALE_CIVILIAN]
            """;
        using var fixture = new TemporaryTerrainMod(("20-base.rul", baseYaml), ("10-patch.rul", patchYaml));
        var diagnostics = new DiagnosticCollector();

        var catalog = TerrainDeploymentRuleCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        var terrain = Assert.Single(catalog.Terrains.Rules).Value;
        Assert.Equal(["MALE_CIVILIAN", "CIVILIAN"], terrain.CivilianTypes);
        Assert.Equal(["BLOCK", "EXTRA"], terrain.MapBlocks.Select(block => block.Name));
        Assert.Equal([1, 3], [terrain.MinimumDepth, terrain.MaximumDepth]);
        var command = Assert.Single(catalog.MapScripts["MAP"].Commands);
        Assert.Equal(MapScriptCommandType.AddCraft, command.Type);
        Assert.Equal([1], command.Groups);
        Assert.Equal([2, 2, 0], command.Size);
        Assert.Equal([7], command.Frequencies);
        Assert.Equal(4, command.Label);
        Assert.Equal(MapScriptCommandType.Resize, Assert.Single(catalog.MapScripts["OLD"].Commands).Type);
        Assert.Equal(2, catalog.McdPatches["URBAN"].Entries.Count);
        Assert.True(catalog.McdPatches["URBAN"].Entries[1].Booleans["noFloor"]);
        Assert.Equal(100, Assert.Single(catalog.AlienRaces.Rules).Value.ListOrder);
        Assert.Equal(2ul, Assert.Single(Assert.Single(catalog.AlienRaces.Rules).Value.RetaliationMissionWeights).Value["MISSION"]);
        Assert.Equal(5, Assert.Single(catalog.EnviroEffects.Rules).Value.EnvironmentalConditions["STR_HOSTILE"].ChancePerTurn);
        Assert.Equal([2, 4], Assert.Single(catalog.AlienDeployments.Rules).Value.Depth);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("mapScripts: [{type: MAP, commands: [{type: unknown}]}]")]
    [InlineData("mapScripts: [{type: MAP, commands: [{type: addLine}]}]")]
    [InlineData("terrains: [{name: TERRAIN, mapBlocks: [{name: BAD, width: 11}]}]")]
    [InlineData("MCDPatches: [{type: PATCH, data: [{}]}]")]
    public void RejectsMalformedTerrainProperties(string yaml)
    {
        using var fixture = new TemporaryTerrainMod(("fixture.rul", yaml));
        Assert.Throws<YamlFormatException>(() => TerrainDeploymentRuleCatalog.Load(CreatePlan(fixture.Root)));
    }

    [Fact]
    public void ReportsMissingCrossFamilyReferences()
    {
        const string yaml = """
            mapScripts:
              - type: MAP
                commands: [{type: fillArea, randomTerrain: [MISSING_SCRIPT_TERRAIN]}]
            terrains:
              - name: TERRAIN
                enviroEffects: MISSING_ENV
                script: MISSING_SCRIPT
            enviroEffects:
              - type: ENV
                armorTransformations: {MISSING_ARMOR: OTHER_ARMOR}
            startingConditions:
              - type: START
                requiredItems: {MISSING_ITEM: 1}
                craftTransformations: {MISSING_CRAFT: OTHER_CRAFT}
            alienDeployments:
              - type: DEPLOY
                terrains: [MISSING_TERRAIN]
                race: MISSING_RACE
                startingCondition: MISSING_START
            """;
        using var fixture = new TemporaryTerrainMod(("fixture.rul", yaml));
        var plan = CreatePlan(fixture.Root);
        var catalog = TerrainDeploymentRuleCatalog.Load(plan);
        var diagnostics = new DiagnosticCollector();

        var validation = catalog.ValidateRelationships(ItemRuleCatalog.Load(plan),
            EquipmentProductionRuleCatalog.Load(plan), PersonnelTacticalRuleCatalog.Load(plan), diagnostics);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Property == "armorTransformations");
        Assert.Contains(validation.Issues, issue => issue.Property == "craftTransformations");
        Assert.DoesNotContain(validation.Issues, issue => issue.Property == "enviroEffects");
        Assert.DoesNotContain(validation.Issues, issue => issue.Property == "requiredItems");
        Assert.DoesNotContain(validation.Issues, issue => issue.Property == "terrains");
        Assert.DoesNotContain(validation.Issues, issue => issue.Property == "randomTerrain");
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingRuleReference);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DeferredRuleReference &&
            item.Context.RelatedId == "MISSING_SCRIPT_TERRAIN");
    }

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        return ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private sealed class TemporaryTerrainMod : IDisposable
    {
        public TemporaryTerrainMod(params (string Name, string Yaml)[] rulesets)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-terrain-test-{Guid.NewGuid():N}");
            var mod = Path.Combine(Root, "fixture"); var rules = Path.Combine(mod, "Ruleset");
            Directory.CreateDirectory(rules);
            File.WriteAllText(Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\nreservedSpace: 1000\n");
            foreach (var item in rulesets) File.WriteAllText(Path.Combine(rules, item.Name), item.Yaml);
        }
        public string Root { get; }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
