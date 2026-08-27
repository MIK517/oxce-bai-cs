using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class EquipmentProductionRulesFixtureTests
{
    [Fact]
    public void EquipmentProductionRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "equipment-production-rules");
        var expectedPath = Path.Combine(
            root, "fixtures", "expected", "mods", "equipment-production-rules.expected.json");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var mods = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(mods, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"), diagnostics);
        var actual = EquipmentProductionRuleCatalog.Load(plan, diagnostics);
        var items = ItemRuleCatalog.Load(plan, diagnostics);
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));
        var document = expected.RootElement;

        var category = Assert.Single(actual.ItemCategories.Rules).Value;
        var categoryReference = document.GetProperty("category").EnumerateArray().ToArray();
        Assert.Equal(categoryReference[0].GetInt32(), category.ListOrder);
        Assert.Equal(categoryReference[1].GetBoolean(), category.Hidden);
        Assert.Equal(categoryReference[2].EnumerateArray().Select(value => value.GetString()), category.InventoryOrder);
        Assert.Equal(document.GetProperty("weaponSetCount").GetInt32(), actual.WeaponSets.Rules.Count);

        var weapon = Assert.Single(actual.CraftWeapons.Rules).Value;
        var weaponReference = document.GetProperty("craftWeapon").EnumerateArray().ToArray();
        Assert.Equal(weaponReference[0].GetString(), weapon.Launcher);
        Assert.Equal(weaponReference[1].GetString(), weapon.Clip);
        Assert.Equal(weaponReference[2].GetInt32(), weapon.Integers["damage"]);
        Assert.Equal(weaponReference[3].GetInt32(), weapon.Integers["ammoMax"]);
        Assert.Equal(weaponReference[4].GetInt32(), weapon.Integers["rearmRate"]);
        Assert.Equal(weaponReference[5].GetInt32(), weapon.Integers["projectileSpeed"]);

        var craft = Assert.Single(actual.Crafts.Rules).Value;
        var craftReference = document.GetProperty("craft").EnumerateArray().ToArray();
        Assert.Equal(craftReference[0].GetInt32(), craft.Integers["listOrder"]);
        Assert.Equal(craftReference[1].GetInt32(), craft.EffectiveMaximumUnits);
        Assert.Equal(craftReference[2].GetInt32(), craft.EffectiveMaximumVehiclesAndLargeSoldiers);
        Assert.Equal(craftReference[3].GetInt32(), craft.Stats.Get("radarRange"));
        Assert.Equal(craftReference[4].GetInt32(), craft.WeaponTypes[0][0]);
        Assert.Equal(craftReference[5].GetInt32(), craft.WeaponTypes[1][0]);
        Assert.Equal(craftReference[6].GetString(), craft.FixedWeapons[0]);

        var ufo = Assert.Single(actual.Ufos.Rules).Value;
        var ufoReference = document.GetProperty("ufo").EnumerateArray().ToArray();
        Assert.Equal(ufoReference[0].GetString(), ufo.Size);
        Assert.Equal(ufoReference[1].GetInt32(), ufo.Integers["blobSize"]);
        Assert.Equal(ufoReference[2].GetInt32(), ufo.Stats.Craft.Get("speedMax"));

        var research = actual.Research.Rules.Single(rule => rule.Id == "RES_B").Value;
        var researchReference = document.GetProperty("research").EnumerateArray().ToArray();
        Assert.Equal(researchReference[0].GetInt32(), research.ListOrder);
        Assert.Equal(researchReference[1].GetInt32(), research.Cost);
        Assert.Equal(researchReference[2].GetInt32(), research.Requirements.Count);
        Assert.Equal(
            researchReference[3].EnumerateObject().Select(pair => (pair.Name, pair.Value.GetUInt64())),
            research.Events.Select(pair => (pair.Key, pair.Value)));

        var manufacture = Assert.Single(actual.Manufacture.Rules).Value;
        var manufactureReference = document.GetProperty("manufacture").EnumerateArray().ToArray();
        Assert.Equal(manufactureReference[0].GetInt32(), manufacture.ListOrder);
        Assert.Equal(manufactureReference[1].GetInt32(), manufacture.Time);
        Assert.Equal(manufactureReference[2].GetInt32(), manufacture.Requirements.Count);
        Assert.Equal(manufactureReference[3].GetInt32(), manufacture.RequiredItems.Count);
        Assert.Equal(manufactureReference[4].GetInt32(), manufacture.ProducedItems.Count);
        Assert.Equal(document.GetProperty("shortcut").GetString(),
            Assert.Single(actual.ManufactureShortcuts.Rules).Value.StartFrom);

        Assert.True(actual.ValidateRelationships(items, diagnostics).IsValid);
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
