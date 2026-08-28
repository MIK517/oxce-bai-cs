using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class EquipmentProductionRuleCatalogTests
{
    [Fact]
    public void LoadsEquipmentFamiliesWithReferenceMergeAndDefaultSemantics()
    {
        const string yaml = """
            itemCategories:
              - type: DELETED_CATEGORY
              - type: CATEGORY
                invOrder: [RIGHT_HAND]
              - update: CATEGORY
                invOrder: !add [LEFT_HAND]
              - delete: DELETED_CATEGORY
            weaponSets:
              - type: SET
                weapons: [LAUNCHER]
            items:
              - type: LAUNCHER
              - type: CLIP
                clipSize: 5
              - type: PRODUCT
            craftWeapons:
              - type: CRAFT_WEAPON
                refNode:
                  launcher: LAUNCHER
                  clip: CLIP
                  ammoMax: 10
                  rearmRate: 5
                  stats: {armor: 2}
                damage: 40
                projectileSpeed: 6
                sprite: 7
                sound: {index: 100, mod: current}
            crafts:
              - type: DELETED_CRAFT
              - type: CRAFT
                refNode:
                  soldiers: 8
                  vehicles: 2
                  fixedWeapons: [CRAFT_WEAPON]
                  groups: [1]
                radarRange: 900
                weaponTypes: [2, [1, 2]]
                groups: !add [2]
                skinSprites: 7
                selectSound: [3, 4]
                battlescapeTerrainData: {name: TERRAIN}
                customCraftValue: 7
              - delete: DELETED_CRAFT
            ufos:
              - type: UFO
                size: STR_MEDIUM
                blobSize: 9
                speedMax: 1500
                marker: 10
                raceBonus:
                  RACE: {armor: 4, craftCustomDeploy: CUSTOM}
                customUfoValue: 9
            research:
              - name: RES_A
                cost: 10
              - name: RES_B
                requires: [RES_A]
                events: {EVENT_A: 2, EVENT_B: 1}
              - update: RES_B
                events: {EVENT_A: 0, EVENT_C: 3}
                getOneFreeProtected: {RES_A: [RES_B]}
                customResearchValue: 5
            manufacture:
              - name: PRODUCT
                time: 12
                requires: [RES_A]
                requiredItems: {CLIP: 1}
                producedItems: {PRODUCT: 2}
                randomProducedItems: [[4, {PRODUCT: 1, PRODUCT: 2}]]
                events: {EVENT_M: 2}
            manufactureShortcut:
              - name: PRODUCT_ALT
                startFrom: PRODUCT
                breakDownItems: [PRODUCT]
            """;
        using var fixture = new TemporaryEquipmentMod(("fixture.rul", yaml));
        var diagnostics = new DiagnosticCollector();

        var content = EquipmentProductionRuleCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        var category = Assert.Single(content.ItemCategories.Rules).Value;
        Assert.Equal(200, category.ListOrder);
        Assert.Equal(["RIGHT_HAND", "LEFT_HAND"], category.InventoryOrder);
        Assert.Equal(["LAUNCHER"], Assert.Single(content.WeaponSets.Rules).Value.Weapons);
        var weapon = Assert.Single(content.CraftWeapons.Rules).Value;
        Assert.Equal(40, weapon.Integers["damage"]);
        Assert.Equal(2, weapon.Stats.Get("armor"));
        Assert.Equal((7, "fixture"), (weapon.Sprite.Index, weapon.Sprite.ModId));
        Assert.Equal((100, "fixture"), (weapon.Sound.Index, weapon.Sound.ModId));

        var craftRule = Assert.Single(content.Crafts.Rules);
        var craft = craftRule.Value;
        Assert.Equal(200, craft.Integers["listOrder"]);
        Assert.Equal(8, craft.EffectiveMaximumUnits);
        Assert.Equal(2, craft.EffectiveMaximumVehiclesAndLargeSoldiers);
        Assert.Equal(900, craft.Stats.Get("radarRange"));
        Assert.Equal([1, 2], craft.Groups);
        Assert.All(craft.WeaponTypes[0], value => Assert.Equal(2, value));
        Assert.Equal([1, 2, 1, 1, 1, 1, 1, 1], craft.WeaponTypes[1]);
        Assert.Empty(craft.SkinSprites);
        Assert.Equal([3, 4], craft.SelectSounds.Select(value => value.Index));
        Assert.Contains(craftRule.DeferredProperties, property => property.Key == "battlescapeTerrainData");
        Assert.Contains(craftRule.DeferredProperties, property => property.Key == "customCraftValue");

        var ufoRule = Assert.Single(content.Ufos.Rules);
        Assert.Equal("STR_MEDIUM_UC", ufoRule.Value.Size);
        Assert.Equal(7, ufoRule.Value.Integers["blobSize"]);
        Assert.Equal(4, ufoRule.Value.EffectiveRadius);
        Assert.Equal(1500, ufoRule.Value.Stats.Craft.Get("speedMax"));
        Assert.Equal(4, ufoRule.Value.RaceBonuses["RACE"].Craft.Get("armor"));
        Assert.Equal("CUSTOM", ufoRule.Value.RaceBonuses["RACE"].CraftCustomDeployment);
        Assert.Contains(ufoRule.DeferredProperties, property => property.Key == "customUfoValue");

        var research = content.Research.Rules.Single(rule => rule.Id == "RES_B");
        Assert.False(research.Value.Events.ContainsKey("EVENT_A"));
        Assert.Equal(1ul, research.Value.Events["EVENT_B"]);
        Assert.Equal(3ul, research.Value.Events["EVENT_C"]);
        Assert.Equal(["RES_B"], Assert.Single(research.Value.GetOneFreeProtected).Topics);
        Assert.Contains(research.DeferredProperties, property => property.Key == "customResearchValue");

        var manufacture = Assert.Single(content.Manufacture.Rules).Value;
        Assert.Equal(12, manufacture.Time);
        Assert.Equal(2, manufacture.ProducedItems["PRODUCT"]);
        var randomItems = Assert.Single(manufacture.RandomProducedItems);
        Assert.Equal(4, randomItems.Weight);
        Assert.Equal(1, randomItems.Items["PRODUCT"]);
        Assert.Equal(2ul, manufacture.Events["EVENT_M"]);
        Assert.Equal("PRODUCT", Assert.Single(content.ManufactureShortcuts.Rules).Value.StartFrom);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
    }

    [Fact]
    public void OverlaysManufacturedSoldierTemplatesAndEditableProductionMaps()
    {
        const string first = """
            manufacture:
              - name: PRODUCT
                time: 5
                spawnedSoldier: {type: SOLDIER, stats: {tu: 50}}
                requiredItems: {A: 1}
                producedItems: {PRODUCT: 1}
            """;
        const string second = """
            manufacture:
              - update: PRODUCT
                spawnedSoldier: {name: CUSTOM}
                requiredItems: !add {B: 2}
                producedItems: !remove [PRODUCT]
                transferTimes: [12, 24]
            """;
        using var fixture = new TemporaryEquipmentMod(("20-base.rul", first), ("10-patch.rul", second));

        var manufacture = Assert.Single(EquipmentProductionRuleCatalog.Load(CreatePlan(fixture.Root)).Manufacture.Rules).Value;

        Assert.Equal(1, manufacture.RequiredItems["A"]);
        Assert.Equal(2, manufacture.RequiredItems["B"]);
        Assert.Empty(manufacture.ProducedItems);
        Assert.Equal([12, 24], manufacture.TransferTimes);
        Assert.True(manufacture.SpawnedSoldierTemplate!.TryGet("name", out var name));
        Assert.Equal("CUSTOM", YamlValueReader.ReadString(name!));
        Assert.True(manufacture.SpawnedSoldierTemplate.TryGet("type", out _));
        Assert.True(manufacture.SpawnedSoldierTemplate.TryGet("stats", out _));
    }

    [Fact]
    public void ReportsInvalidEquipmentAndProductionRelationships()
    {
        const string yaml = """
            itemCategories:
              - type: CATEGORY
                replaceBy: MISSING_CATEGORY
            weaponSets:
              - type: SET
                weapons: [MISSING_ITEM]
            craftWeapons:
              - type: BROKEN_WEAPON
                damage: 10
                projectileSpeed: 0
                ammoMax: 1
                rearmRate: 0
            crafts:
              - type: CRAFT
                fixedWeapons: [MISSING_WEAPON]
            research:
              - name: BROKEN_RESEARCH
                cost: 10
                requires: [MISSING_RESEARCH]
            manufacture:
              - name: BROKEN_MANUFACTURE
                time: 0
            manufactureShortcut:
              - name: SHORTCUT
                startFrom: MISSING_MANUFACTURE
            """;
        using var fixture = new TemporaryEquipmentMod(("fixture.rul", yaml));
        var plan = CreatePlan(fixture.Root);
        var content = EquipmentProductionRuleCatalog.Load(plan);
        var items = ItemRuleCatalog.Load(plan);
        var diagnostics = new DiagnosticCollector();

        var result = content.ValidateRelationships(items, diagnostics);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(result.Issues, issue => issue.Property == "replaceBy");
        Assert.Contains(result.Issues, issue => issue.Property == "launcher");
        Assert.Contains(result.Issues, issue => issue.Property == "projectileSpeed");
        Assert.Contains(result.Issues, issue => issue.Property == "time");
        Assert.Contains(result.Issues, issue => issue.Property == "startFrom");
        Assert.Contains(diagnostics.Snapshot(), item =>
            item.Code == ModDiagnosticCodes.DeferredRuleReference && item.Context.RuleType == "itemCategories");
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.InvalidRuleRelationship);
        Assert.False(content.Capabilities.Has(ContentLoadStage.Linked));
    }

    [Theory]
    [InlineData("crafts: [{type: CRAFT, weaponTypes: {slot: 1}}]")]
    [InlineData("ufos: [{type: UFO, raceBonus: [RACE]}]")]
    [InlineData("manufacture: [{name: PRODUCT, randomProducedItems: {PRODUCT: 1}}]")]
    [InlineData("manufacture: [{name: PRODUCT, spawnedSoldier: SOLDIER}]")]
    public void RejectsMalformedEquipmentAndProductionProperties(string yaml)
    {
        using var fixture = new TemporaryEquipmentMod(("fixture.rul", yaml));

        Assert.Throws<YamlFormatException>(() => EquipmentProductionRuleCatalog.Load(CreatePlan(fixture.Root)));
    }

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        return ModLoadPlanner.Create(catalog, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private sealed class TemporaryEquipmentMod : IDisposable
    {
        public TemporaryEquipmentMod(params (string Name, string Yaml)[] rulesets)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-equipment-test-{Guid.NewGuid():N}");
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
