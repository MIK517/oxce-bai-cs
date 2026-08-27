using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Items;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ItemRuleCatalogTests
{
    [Fact]
    public void LoadsItemDefaultsInheritanceActionsAndEditableCollections()
    {
        const string yaml = """
            items:
              - type: DELETED
              - type: AMMO
                battleType: 2
                clipSize: 6
              - type: ALIEN
                liveAlien: true
              - type: WEAPON
                refNode:
                  battleType: 1
                  categories: [CAT_A]
                  recoveryDividers: {AMMO: 2}
                  compatibleAmmo: [AMMO]
                  costAimed: {energy: 7}
                  flatRate: true
                categories: !add [CAT_B]
                recoveryDividers: !add {ALIEN: 4}
                tuAimed: 30
                flatAimed: {time: true, energy: false}
                flatUse: false
                confAimed:
                  shots: 2
                  ammoSlot: 9
                  ammoSpawnItemChanceOverride: ~
                ammo:
                  1: {compatibleAmmo: [AMMO], tuLoad: 20}
                damageType: 3
                blastRadius: 4
                damageAlter: {ToHealth: 0.75, IgnoreDirection: true}
                fireSound: [-1, 2, {index: 100, mod: current}]
                createItem: return
                customItemValue: 12
              - delete: DELETED
              - type: PSI
                battleType: 9
            """;
        using var fixture = new TemporaryItemMod(("fixture.rul", yaml));
        var diagnostics = new DiagnosticCollector();

        var catalog = ItemRuleCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        Assert.Equal(["AMMO", "ALIEN", "WEAPON", "PSI"], catalog.Items.Rules.Select(rule => rule.Id));
        var weapon = catalog.Items.Rules[2];
        Assert.Equal(400, weapon.Value.Values.GetInteger("listOrder"));
        Assert.Equal(400, weapon.Value.EffectiveLoadOrder);
        Assert.False(weapon.Value.Values.Boolean("ignoreInCraftEquip"));
        Assert.Equal(["CAT_A", "CAT_B"], weapon.Value.Categories);
        Assert.Equal(2, weapon.Value.RecoveryDividers["AMMO"]);
        Assert.Equal(4, weapon.Value.RecoveryDividers["ALIEN"]);
        Assert.Equal(["AMMO"], weapon.Value.CompatibleAmmo[0]);
        Assert.Equal(["AMMO"], weapon.Value.CompatibleAmmo[1]);
        Assert.Equal(20, weapon.Value.TimeUnitsToLoad[1]);
        var aimed = weapon.Value.Actions["Aimed"];
        Assert.Equal((30, 7), (aimed.Cost.Time, aimed.Cost.Energy));
        Assert.Equal((true, false), (aimed.Flat.Time, aimed.Flat.Energy));
        Assert.False(weapon.Value.UseFlats["Use"].Time);
        Assert.Equal(2, aimed.Shots);
        Assert.Equal(0, aimed.AmmoSlot);
        Assert.Equal(-1, aimed.AmmoSpawnItemChanceOverride);
        Assert.Equal(3, weapon.Value.Damage.PredefinedType);
        Assert.Equal(4, weapon.Value.Damage.Integers["FixRadius"]);
        Assert.Equal(0.75, weapon.Value.Damage.Reals["ToHealth"]);
        Assert.True(weapon.Value.Damage.Booleans["IgnoreDirection"]);
        Assert.Equal([2, 100], weapon.Value.ResourceIndexLists["fireSound"].Select(value => value.Index));
        Assert.Equal("fixture", weapon.Value.ResourceIndexLists["fireSound"][1].ModId);
        Assert.Contains(weapon.DeferredProperties, property => property.Key == "createItem");
        Assert.Contains(weapon.DeferredProperties, property => property.Key == "customItemValue");

        var psi = catalog.Items.Rules[3].Value;
        Assert.Equal(500, psi.Values.GetInteger("listOrder"));
        Assert.True(psi.Values.Boolean("psiRequired"));
        Assert.Equal(0, psi.Actions["Aimed"].Range);
        Assert.Equal(6, psi.Values.GetInteger("targetMatrix"));
        var ammo = catalog.Items.Rules[0].Value;
        Assert.Equal(8, ammo.Damage.Integers["RandomType"]);
        Assert.Equal(0.25, ammo.Damage.Reals["ToStun"]);
        Assert.True(ammo.Damage.Booleans["RandomWound"]);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
    }

    [Fact]
    public void ReportsInvalidInternalItemRelationships()
    {
        const string yaml = """
            items:
              - type: ALIEN
                liveAlien: true
              - type: BROKEN
                battleType: 1
                clipSize: 0
                spawnItem: MISSING
                recoveryTransformations:
                  ALIEN: []
                ammo:
                  1: {compatibleAmmo: [MISSING_AMMO]}
            """;
        using var fixture = new TemporaryItemMod(("fixture.rul", yaml));
        var content = ItemRuleCatalog.Load(CreatePlan(fixture.Root));
        var diagnostics = new DiagnosticCollector();

        var result = content.ValidateInternalRelationships(diagnostics);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Property == "clipSize");
        Assert.Contains(result.Issues, issue => issue.RelatedId == "MISSING");
        Assert.Contains(result.Issues, issue => issue.RelatedId == "MISSING_AMMO");
        Assert.Contains(result.Issues, issue => issue.RelatedId == "ALIEN" &&
            issue.Message.Contains("live-alien", StringComparison.Ordinal));
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

    private sealed class TemporaryItemMod : IDisposable
    {
        public TemporaryItemMod(params (string Name, string Yaml)[] rulesets)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-item-test-{Guid.NewGuid():N}");
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
