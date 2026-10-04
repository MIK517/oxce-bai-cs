using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>Read-only Inventory rules from RuleInventory::ScriptRegister and Mod inventory wrappers.</summary>
public sealed class StrategicWorldDetectionInventoryRulesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InventoryLookupAndIdentityMatchNativeBindings(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var code = "var ptr RuleInventory inventory; var ptr RuleMod absent null; var text id; ";
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "inventoryLookup"))
        {
            var source = row.GetProperty("source").GetBoolean() ? "rules" : "absent";
            code += $"{source}.getRuleInventory inventory \"{row.GetProperty("name").GetString()}\"; inventory.getId id; ";
            code += $"if neq id \"{row.GetProperty("id").GetString()}\"; return DETECTION_NONE 0; end; ";
        }
        code += "return DETECTION_RADAR 100;";
        RunInventoryScript(code, fromCache, fromCraft);
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, InventoryRules);
        var other = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, InventoryRules);
        var handle = content.RuntimeRules.Inventories.GetRequired("STR_NONE");
        Assert.Equal("STR_NONE", content.RuntimeRules.Inventories[handle].Value.Id);
        Assert.Throws<InvalidOperationException>(() => content.RuntimeRules.Inventories.Validate(
            other.RuntimeRules.Inventories.GetRequired("STR_NONE")));
    }

    private static void RunInventoryScript(string code, bool fromCache, bool fromCraft, string? inventoryRules = null)
    {
        var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            {{inventoryRules ?? InventoryRules}}
            ufos:
              - type: UFO_INVENTORY_READ
                speedMax: 2200
                damageMax: 50
                scripts:
                  {{parser}}: '{{code}}'
            """);
        var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_INVENTORY_READ", 2200).Capture();
        if (fromCraft)
            snapshot = snapshot.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with { Status = "STR_OUT", Speed = 0, Destination = null },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(214));
        campaign = Reload(content, campaign, 214, "inventory-read-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 214, "inventory-read-after.sav");
    }

    private const string InventoryRules = """
        invs:
          - id: STR_RIGHT_HAND
            type: 1
          - id: STR_LEFT_HAND
            type: 1
          - id: STR_BACK_PACK
          - id: STR_BELT
          - id: STR_GROUND
            type: 2
          - id: STR_NONE
        """;
}
