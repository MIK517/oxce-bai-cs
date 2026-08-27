using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PersonnelTacticalRulesFixtureTests
{
    [Fact]
    public void PersonnelTacticalRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "personnel-tactical-rules");
        var expectedPath = Path.Combine(
            root, "fixtures", "expected", "mods", "personnel-tactical-rules.expected.json");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var mods = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(mods, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"), diagnostics);
        var actual = PersonnelTacticalRuleCatalog.Load(plan, diagnostics);
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));
        var document = expected.RootElement;

        var inventory = actual.Inventories.Rules.Single(rule => rule.Id == "STR_RIGHT_HAND").Value;
        var inventoryReference = document.GetProperty("inventory").EnumerateArray().ToArray();
        Assert.Equal(inventoryReference[0].GetInt32(), inventory.ListOrder);
        Assert.Equal(inventoryReference[1].GetInt32(), inventory.Hand);
        Assert.Equal(inventoryReference[2].GetInt32(), inventory.X);
        Assert.Equal(inventoryReference[3].GetInt32(), inventory.Slots.Count);
        Assert.Equal(inventoryReference[4].GetInt32(), inventory.Costs["STR_LEFT_HAND"]);

        var armor = Assert.Single(actual.Armors.Rules).Value;
        var armorReference = document.GetProperty("armor").EnumerateArray().ToArray();
        Assert.Equal(armorReference[0].GetInt32(), armor.Integers["listOrder"]);
        Assert.Equal(armorReference[1].GetInt32(), armor.Stats.Get("health"));
        Assert.Equal(armorReference[2].GetInt32(), armor.Stats.Get("firing"));
        Assert.Equal(armorReference[3].GetInt32(), armor.MoveCosts["runPercent"].TimePercent);
        Assert.Equal(armorReference[4].GetInt32(), armor.MoveCosts["runPercent"].EnergyPercent);
        Assert.Equal(armorReference[5].GetDouble(), armor.DamageModifiers[0]);

        var skill = Assert.Single(actual.Skills.Rules).Value;
        var skillReference = document.GetProperty("skill").EnumerateArray().ToArray();
        Assert.Equal(skillReference[0].GetInt32(), skill.TargetMode);
        Assert.Equal(skillReference[1].GetInt32(), skill.BattleType);
        Assert.Equal(skillReference[2].GetInt32(), skill.Cost.Time);
        Assert.Equal(skillReference[3].GetInt32(), skill.Cost.Energy);

        var soldier = Assert.Single(actual.Soldiers.Rules).Value;
        var soldierReference = document.GetProperty("soldier").EnumerateArray().ToArray();
        Assert.Equal(soldierReference[0].GetInt32(), soldier.Integers["listOrder"]);
        Assert.Equal(soldierReference[1].GetInt32(), soldier.MinimumStats.Get("health"));
        Assert.Equal(soldierReference[2].GetInt32(), soldier.MinimumStats.Get("firing"));
        Assert.Equal(soldierReference[3].GetInt32(), soldier.StatCaps.Get("health"));
        Assert.Equal(soldierReference[4].GetInt32(), soldier.SoldierNames.Count);

        var unit = Assert.Single(actual.Units.Rules).Value;
        var unitReference = document.GetProperty("unit").EnumerateArray().ToArray();
        Assert.Equal(unitReference[0].GetInt32(), unit.Stats.Get("health"));
        Assert.Equal(unitReference[1].GetInt32(), unit.Integers["standHeight"] + unit.Integers["floatHeight"]);
        Assert.Equal(unitReference[2].GetInt32(), unit.BuiltInWeaponSets.Count);
        Assert.Equal(unitReference[3].GetUInt64(), Assert.Single(unit.WeightedBuiltInWeaponSets)["SET_A"]);

        var bonus = Assert.Single(actual.Bonuses.Rules).Value;
        var bonusReference = document.GetProperty("bonus").EnumerateArray().ToArray();
        Assert.Equal(bonusReference[0].GetInt32(), bonus.ListOrder);
        Assert.Equal(bonusReference[1].GetInt32(), bonus.Stats.Get("firing"));
        var transformation = Assert.Single(actual.Transformations.Rules).Value;
        var transformationReference = document.GetProperty("transformation").EnumerateArray().ToArray();
        Assert.Equal(transformationReference[0].GetInt32(), transformation.Integers["listOrder"]);
        Assert.Equal(transformationReference[1].GetInt32(), transformation.StatSets["requiredMinStats"].Get("health"));
        Assert.Equal(transformationReference[2].GetInt32(), transformation.StatSets["requiredMaxStats"].Get("health"));
        Assert.Equal(transformationReference[3].GetProperty("EVENT_B").GetUInt64(), transformation.Events["EVENT_B"]);
        var commendation = Assert.Single(actual.Commendations.Rules).Value;
        var commendationReference = document.GetProperty("commendation").EnumerateArray().ToArray();
        Assert.Equal(commendationReference[0].GetString(), commendation.Description);
        Assert.Equal(commendationReference[1].GetInt32(), commendation.Criteria["kills"].Count);

        var validation = actual.ValidateRelationships(
            ItemRuleCatalog.Load(plan), EquipmentProductionRuleCatalog.Load(plan), diagnostics);
        Assert.True(validation.IsValid);
        Assert.Equal(["ARMOR"], validation.Caches.ArmorsForSoldiers);
        Assert.Equal(["ARMOR_ITEM"], validation.Caches.ArmorStorageItems);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
