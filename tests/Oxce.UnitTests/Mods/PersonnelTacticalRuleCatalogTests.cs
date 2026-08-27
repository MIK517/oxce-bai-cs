using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class PersonnelTacticalRuleCatalogTests
{
    [Fact]
    public void LoadsPersonnelFamiliesWithReferenceDefaultsAndMergeSemantics()
    {
        const string yaml = """
            invs:
              - id: DELETED_INV
              - id: STR_RIGHT_HAND
                x: 10
                slots: [[0, 0]]
                costs: {STR_LEFT_HAND: 8}
              - id: STR_LEFT_HAND
                costs: {STR_RIGHT_HAND: 8}
              - delete: DELETED_INV
            items:
              - type: CORPSE
                battleType: 11
              - type: ARMOR_ITEM
              - type: WEAPON
                battleType: 1
                clipSize: 1
              - type: LIVE
              - type: PRODUCT
            research:
              - name: RES
            armors:
              - type: DELETED_ARMOR
              - type: ARMOR
                refNode:
                  corpseBattle: [CORPSE]
                  corpseGeo: CORPSE
                  loftempsSet: [1]
                  storeItem: ARMOR_ITEM
                  units: [SOLDIER]
                builtInWeapons: [WEAPON]
                requires: RES
                requiresAward: MEDAL
                requiresBonus: BONUS
                stats: {health: 5}
                moveCost: {runPercent: [80, 70]}
                damageModifier: [0.5]
                layersDefaultPrefix: ARMOR
                layersSpecificPrefix: {1: SPECIAL}
                layersDefinition: {MALE: [TORSO, LEGS]}
              - type: LARGE
                corpseBattle: [CORPSE, CORPSE, CORPSE, CORPSE]
                corpseGeo: CORPSE
                loftempsSet: [1, 2, 3, 4]
                storeItem: STR_NONE
                size: 2
                fearImmune: false
                zombiImmune: false
              - delete: DELETED_ARMOR
            soldierBonuses:
              - name: BONUS
                stats: {firing: 5}
            skills:
              - type: SKILL
                targetMode: 99
                battleType: 1
                tuUse: 12
                costUse: {energy: 4}
                flatUse: {time: true}
                compatibleWeapons: [WEAPON]
                requiredBonuses: [BONUS]
            soldiers:
              - type: DELETED_SOLDIER
              - type: SOLDIER
                armor: ARMOR
                specialWeapon: WEAPON
                minStats: {health: 30}
                statCaps: {health: 60}
                skills: [SKILL]
                soldierNames: [first.nam, delete, second.nam]
                rankStrings: [ROOKIE]
                deathMale: [1, 2]
                spawnedSoldier: {name: BASE, stats: {tu: 50}}
              - update: SOLDIER
                minStats: {health: 0, firing: 40}
                skills: !add [SKILL]
                spawnedSoldier: {name: PATCH}
              - delete: DELETED_SOLDIER
            units:
              - type: UNIT
                armor: ARMOR
                liveAlien: LIVE
                stats: {health: 35}
                standHeight: 20
                floatHeight: 5
                builtInWeaponSets: [[WEAPON]]
                builtInWeapons: [WEAPON]
                weightedBuiltInWeaponSets: [{SET_A: 2, SET_B: 0}]
                deathSound: 3
                avoidsFire: true
            soldierTransformation:
              - name: TRANSFORM
                requires: [RES]
                producedItem: PRODUCT
                producedSoldierType: SOLDIER
                producedSoldierArmor: ARMOR
                allowedSoldierTypes: [SOLDIER]
                requiredPreviousTransformations: [TRANSFORM]
                requiredItems: {WEAPON: 1}
                requiredCommendations: {MEDAL: 1}
                soldierBonusType: BONUS
                requiredMinStats: {health: 20}
                events: {EVENT_A: 2}
              - update: TRANSFORM
                events: {EVENT_A: 0, EVENT_B: 3}
            commendations:
              - type: MEDAL
                description: DESC
                criteria: {kills: [1, 2]}
                killCriteria: [[[2, [HOSTILE]]]]
                soldierBonusTypes: [BONUS]
                requires: [RES]
                units: [SOLDIER]
            """;
        using var fixture = new TemporaryPersonnelMod(("fixture.rul", yaml));
        var diagnostics = new DiagnosticCollector();
        var plan = CreatePlan(fixture.Root);

        var content = PersonnelTacticalRuleCatalog.Load(plan, diagnostics);
        var validation = content.ValidateRelationships(
            ItemRuleCatalog.Load(plan), EquipmentProductionRuleCatalog.Load(plan), diagnostics);

        Assert.Equal(2, content.Inventories.Rules.Count);
        var right = content.Inventories.Rules.Single(rule => rule.Id == "STR_RIGHT_HAND").Value;
        Assert.Equal(20, right.ListOrder);
        Assert.Equal(2, right.Hand);
        Assert.Equal(8, right.Costs["STR_LEFT_HAND"]);
        var armor = content.Armors.Rules.Single(rule => rule.Id == "ARMOR").Value;
        Assert.Equal(200, armor.Integers["listOrder"]);
        Assert.Equal(5, armor.Stats.Get("health"));
        Assert.Equal(new ArmorMoveCostRule(80, 70), armor.MoveCosts["runPercent"]);
        Assert.Equal(0.5, armor.DamageModifiers[0]);
        Assert.Equal("SPECIAL", armor.LayerSpecificPrefixes[1]);
        Assert.Equal(["ARMOR__0__TORSO", "SPECIAL__1__LEGS"], armor.EffectiveLayers("MALE"));
        var large = content.Armors.Rules.Single(rule => rule.Id == "LARGE").Value;
        Assert.False(large.NullableBooleans["fearImmune"]);
        Assert.True(large.NullableBooleans["zombiImmune"]);
        Assert.True(large.InfiniteSupply);

        var skill = Assert.Single(content.Skills.Rules).Value;
        Assert.Equal(0, skill.TargetMode);
        Assert.Equal(12, skill.Cost.Time);
        Assert.Equal(4, skill.Cost.Energy);
        Assert.True(skill.Flat.Time);
        var soldier = Assert.Single(content.Soldiers.Rules).Value;
        Assert.Equal(2, soldier.Integers["listOrder"]);
        Assert.Equal(30, soldier.MinimumStats.Get("health"));
        Assert.Equal(40, soldier.MinimumStats.Get("firing"));
        Assert.Equal(60, soldier.TrainingStatCaps.Get("health"));
        Assert.Equal(["second.nam"], soldier.SoldierNames);
        Assert.Equal(["SKILL", "SKILL"], soldier.Skills);
        Assert.True(soldier.SpawnedSoldierTemplate!.TryGet("stats", out _));
        Assert.True(soldier.SpawnedSoldierTemplate.TryGet("name", out var spawnedName));
        Assert.Equal("PATCH", YamlValueReader.ReadString(spawnedName!));

        var unit = Assert.Single(content.Units.Rules).Value;
        Assert.Equal(35, unit.Stats.Get("health"));
        Assert.Equal(2, unit.BuiltInWeaponSets.Count);
        Assert.Equal(2ul, Assert.Single(unit.WeightedBuiltInWeaponSets)["SET_A"]);
        Assert.True(unit.AvoidsFire);
        var transformation = Assert.Single(content.Transformations.Rules).Value;
        Assert.False(transformation.Events.ContainsKey("EVENT_A"));
        Assert.Equal(3ul, transformation.Events["EVENT_B"]);
        Assert.Equal(9999, transformation.StatSets["requiredMaxStats"].Get("health"));
        Assert.Equal([1, 2], Assert.Single(content.Commendations.Rules).Value.Criteria["kills"]);
        Assert.True(validation.IsValid);
        Assert.Equal(["ARMOR", "LARGE"], validation.Caches.ArmorsForSoldiers);
        Assert.Equal(["ARMOR_ITEM"], validation.Caches.ArmorStorageItems);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsInvalidPersonnelRelationships()
    {
        const string yaml = """
            invs:
              - id: INV
                costs: {MISSING: 1}
            armors:
              - type: ARMOR
            skills:
              - type: SKILL
                compatibleWeapons: [MISSING_ITEM]
            soldiers:
              - type: SOLDIER
                armor: MISSING_ARMOR
                skills: [MISSING_SKILL]
            units:
              - type: UNIT
                armor: MISSING_ARMOR
                spawnUnit: MISSING_UNIT
            soldierTransformation:
              - name: TRANSFORM
                producedSoldierType: MISSING_SOLDIER
            commendations:
              - type: MEDAL
                soldierBonusTypes: [MISSING_BONUS]
            """;
        using var fixture = new TemporaryPersonnelMod(("fixture.rul", yaml));
        var plan = CreatePlan(fixture.Root);
        var content = PersonnelTacticalRuleCatalog.Load(plan);
        var diagnostics = new DiagnosticCollector();

        var result = content.ValidateRelationships(
            ItemRuleCatalog.Load(plan), EquipmentProductionRuleCatalog.Load(plan), diagnostics);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Property == "costs");
        Assert.Contains(result.Issues, issue => issue.Property == "corpseGeo");
        Assert.Contains(result.Issues, issue => issue.Property == "compatibleWeapons");
        Assert.Contains(result.Issues, issue => issue.Property == "spawnUnit");
        Assert.Contains(diagnostics.Snapshot(), item =>
            item.Code == ModDiagnosticCodes.MissingRuleReference && item.Context.RuleType == "invs");
    }

    [Theory]
    [InlineData("invs: [{id: INV, slots: [[0]]}]")]
    [InlineData("armors: [{type: ARMOR, moveCost: [1, 2]}]")]
    [InlineData("soldiers: [{type: SOLDIER, spawnedSoldier: NAME}]")]
    [InlineData("units: [{type: UNIT, standHeight: 26}]")]
    [InlineData("commendations: [{type: MEDAL, killCriteria: {kills: 1}}]")]
    public void RejectsMalformedPersonnelProperties(string yaml)
    {
        using var fixture = new TemporaryPersonnelMod(("fixture.rul", yaml));

        Assert.Throws<YamlFormatException>(() => PersonnelTacticalRuleCatalog.Load(CreatePlan(fixture.Root)));
    }

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        return ModLoadPlanner.Create(catalog, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private sealed class TemporaryPersonnelMod : IDisposable
    {
        public TemporaryPersonnelMod(params (string Name, string Yaml)[] rulesets)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-personnel-test-{Guid.NewGuid():N}");
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
