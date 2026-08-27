using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Items;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ItemRulesFixtureTests
{
    [Fact]
    public void ItemRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "item-rules");
        var expectedPath = Path.Combine(root, "fixtures", "expected", "mods", "item-rules.expected.json");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var mods = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(mods, [new ModActivation("fixture", true)], "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"), diagnostics);
        var actual = ItemRuleCatalog.Load(plan, diagnostics);
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));
        var expectedItems = expected.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(expectedItems.Length, actual.Items.Rules.Count);
        for (var index = 0; index < expectedItems.Length; index++)
        {
            var item = actual.Items.Rules[index];
            var reference = expectedItems[index];
            Assert.Equal(reference.GetProperty("id").GetString(), item.Id);
            Assert.Equal(reference.GetProperty("listOrder").GetInt32(), item.Value.Values.GetInteger("listOrder"));
            Assert.Equal(reference.GetProperty("effectiveLoadOrder").GetInt32(), item.Value.EffectiveLoadOrder);
            Assert.Equal(reference.GetProperty("battleType").GetInt32(), item.Value.Values.GetInteger("battleType"));
            Assert.Equal(reference.GetProperty("ignoreInCraftEquip").GetBoolean(),
                item.Value.Values.Boolean("ignoreInCraftEquip"));
            Assert.Equal(reference.GetProperty("dropoff").GetInt32(), item.Value.Values.GetInteger("dropoff"));
            Assert.Equal(reference.GetProperty("fuseType").GetInt32(), item.Value.Values.GetInteger("fuseType"));
            Assert.Equal(reference.GetProperty("meleeAmmoSlot").GetInt32(), item.Value.Actions["Melee"].AmmoSlot);
            Assert.Equal(reference.GetProperty("targetMatrix").GetInt32(), item.Value.Values.GetInteger("targetMatrix"));
            Assert.Equal(reference.GetProperty("psiRequired").GetBoolean(), item.Value.Values.Boolean("psiRequired"));
            Assert.Equal(reference.GetProperty("manaRequired").GetBoolean(), item.Value.Values.Boolean("manaRequired"));
            Assert.Equal(reference.GetProperty("costBuy").GetInt32(), item.Value.Values.GetInteger("costBuy"));
            Assert.Equal(reference.GetProperty("costSell").GetInt32(), item.Value.Values.GetInteger("costSell"));
            Assert.Equal(reference.GetProperty("transferTime").GetInt32(), item.Value.Values.GetInteger("transferTime"));
            Assert.Equal(reference.GetProperty("categories").EnumerateArray().Select(value => value.GetString()),
                item.Value.Categories);
            Assert.Equal(reference.GetProperty("ammoCount").GetInt32(), item.Value.CompatibleAmmo[0].Count);

            var aimed = reference.GetProperty("aimed").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            Assert.Equal(aimed, new[]
            {
                item.Value.Actions["Aimed"].Accuracy, item.Value.Actions["Aimed"].Range,
                item.Value.Actions["Aimed"].Shots, item.Value.Actions["Aimed"].AmmoSlot,
                item.Value.Actions["Aimed"].Cost.Time!.Value, item.Value.Actions["Aimed"].Cost.Energy!.Value,
            });
            var damage = reference.GetProperty("damage").EnumerateArray().ToArray();
            Assert.Equal(damage[0].GetInt32(), item.Value.Damage.PredefinedType ?? -1);
            Assert.Equal(damage[1].GetInt32(), item.Value.Damage.Integers.GetValueOrDefault("FixRadius"));
            Assert.Equal(damage[2].GetDouble(), item.Value.Damage.Reals.GetValueOrDefault("ToHealth", 1));
            Assert.Equal(damage[3].GetBoolean(), item.Value.Damage.Booleans.GetValueOrDefault("IgnoreDirection"));
            Assert.Equal(reference.GetProperty("fireSound").EnumerateArray().Select(value => value.GetInt32()),
                item.Value.ResourceIndexLists["fireSound"].Select(value => value.Index));
        }
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
