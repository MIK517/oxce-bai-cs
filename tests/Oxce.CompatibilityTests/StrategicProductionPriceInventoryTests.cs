using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InventoryLookupRolesAndMetadataMatchNativePriceQueries(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var code = "var ptr RuleInventory inventory; var ptr RuleInventory same; var ptr RuleMod absent null; " +
            "var text id; var int value; ";
        var rows = TestFixtures.Rows(oracle.RootElement, "inventoryMetadata");
        var definitions = "invs: [" + string.Join(", ", rows.Select(row => $"{{id: {row.GetProperty("id").GetString()}, type: {row.GetProperty("type").GetInt32()}}}")) + "]";
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "inventoryLookup"))
            code += $"{(row.GetProperty("source").GetBoolean() ? "rules" : "absent")}.getRuleInventory inventory \"{row.GetProperty("name").GetString()}\"; " +
                $"inventory.getId id; if neq id \"{row.GetProperty("id").GetString()}\"; div cost_current 0; end; ";
        foreach (var row in rows)
        {
            code += $"rules.getRuleInventory inventory \"{row.GetProperty("id").GetString()}\"; " +
                $"inventory.getType value; if neq value {row.GetProperty("type").GetInt32()}; div cost_current 0; end; " +
                $"inventory.isRightHand value; if neq value {(row.GetProperty("right").GetBoolean() ? 1 : 0)}; div cost_current 0; end; " +
                $"inventory.isLeftHand value; if neq value {(row.GetProperty("left").GetBoolean() ? 1 : 0)}; div cost_current 0; end; ";
        }
        var roles = TestFixtures.Rows(oracle.RootElement, "inventoryConventional");
        var methods = new[] { "RightHand", "LeftHand", "Backpack", "Belt", "Ground" };
        Assert.Equal(roles.Length, methods.Length);
        for (var index = 0; index < roles.Length; index++)
            code += $"rules.getRuleInventory{methods[index]} inventory; rules.getRuleInventory same \"{roles[index].GetString()}\"; " +
                "if neq inventory same; div cost_current 0; end; " +
                $"absent.getRuleInventory{methods[index]} inventory; if neq inventory null; div cost_current 0; end; ";
        code += "inventory.getType value; if neq value 0; div cost_current 0; end; " +
            "inventory.isRightHand value; if neq value 0; div cost_current 0; end; " +
            "inventory.isLeftHand value; if neq value 0; div cost_current 0; end; return 7;";
        AssertPrice(Load(cache, global, code, extraRules: definitions));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InventoryMovementPreservesNativeIdentityNullAndSignedCosts(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-inventory-cost");
        var rows = TestFixtures.Rows(oracle.RootElement, "costs");
        var pairs = new[] { ("RIGHT", "RIGHT"), ("RIGHT", "LEFT"), ("RIGHT", "GROUND"),
            ("LEFT", "RIGHT"), ("MISSING", "RIGHT"), ("MISSING", "MISSING"), ("GROUND", "GROUND") };
        Assert.Equal(pairs.Length, rows.Length);
        var code = "var ptr RuleInventory source; var ptr RuleInventory target; var int value; ";
        for (var index = 0; index < rows.Length; index++)
            code += $"rules.getRuleInventory source \"{pairs[index].Item1}\"; rules.getRuleInventory target \"{pairs[index].Item2}\"; " +
                $"source.getMoveToCost value target; if neq value {rows[index][0].GetInt32()}; div cost_current 0; end; ";
        AssertPrice(Load(cache, global, code + "return 7;", extraRules:
            "invs: [{id: RIGHT, costs: {LEFT: -7, GROUND: 2147483647, RIGHT: 99}}, {id: LEFT, costs: {RIGHT: -2147483648}}, {id: GROUND}]"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OnlyConsumedUnsafeInventoryCallsStopBeforeTickMutation(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        Assert.True(oracle.RootElement.GetProperty("missingInventoryThrows").GetBoolean());
        foreach (var (call, binding, reason) in new[]
        {
            ("rules.getRuleInventoryGround target;", "RuleMod.getRuleInventoryGround", "Price script requires missing inventory STR_GROUND."),
            ("source.getMoveToCost value target;", "RuleInventory.getMoveToCost", "Price inventory movement requires a nonnull destination."),
            ("rules.getRuleInventory target \"LEFT\"; source.getMoveToCost value target;", "RuleInventory.getMoveToCost", "Price inventory movement requires missing cost RIGHT -> LEFT."),
        })
            foreach (var consumed in new[] { false, true })
            {
                var code = "var ptr RuleInventory source; var ptr RuleInventory target null; var int value; rules.getRuleInventory source \"RIGHT\"; " +
                    $"if eq 1 {(consumed ? 1 : 0)}; {call} end; return 7;";
                var content = Load(cache, global, code, extraRules: "invs: [{delete: STR_GROUND}, {id: RIGHT}, {id: LEFT}]");
                if (!consumed) AssertPrice(content);
                else
                {
                    var campaign = Reload(content, Create(content), 81, "inventory-price-guard.sav");
                    AssertTimeBlocked(campaign, $"Price {(global ? "event" : "script")} failed: Script binding '{binding}' failed: {reason}.");
                }
            }
    }
}
