using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Compilation;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionTextLookupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TextAssignmentsBranchesAndClearResolveOnlyNamedDamageRules(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var native = TestFixtures.Rows(oracle.RootElement, "conversionRows")
            .Single(row => row[0].GetInt32() == 20 && row[1].GetInt32() == 39)[2][3].GetInt32();
        AssertDetectionReadScript("var text original \"SAFE\"; var text name original; " +
            "var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
            "if eq already_tracked 0; set name original; else; set name \"SAFE_TOO\"; end; " +
            "rules.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth value 39; " +
            $"if neq value {native}; return DETECTION_NONE 0; end; " +
            "clear name; rules.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth value detection_chance; " +
            "if neq value 0; return DETECTION_NONE 0; end; " +
            "set name \"STR_NONE\"; rules.getRuleItem item name; item.getDamageType damage; " +
            "damage.getDamageToHealth value detection_chance; if neq value 0; return DETECTION_NONE 0; end; " +
            "if eq already_tracked 0; var text scoped \"SAFE\"; rules.getRuleItem item scoped; end; " +
            "if eq already_tracked 0; var text reused \"SAFE_TOO\"; rules.getRuleItem item reused; end; " +
            "item.getDamageType damage; damage.getDamageToHealth value 39; " +
            $"if neq value {native}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            fromCache, fromCraft, Items);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopiedInventoryNamesPermitNativeMovementCostQueries(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-inventory-cost");
        var cost = TestFixtures.Rows(oracle.RootElement, "costs")[3][0].GetInt32();
        AssertDetectionReadScript("var text sourceId \"LEFT\"; var text original \"RIGHT\"; var text targetId original; " +
            "var ptr RuleInventory source; var ptr RuleInventory target; var int cost; " +
            "rules.getRuleInventory source sourceId; rules.getRuleInventory target targetId; source.getMoveToCost cost target; " +
            $"if neq cost {cost}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;", fromCache, fromCraft,
            "invs: [{id: LEFT, costs: {RIGHT: -2147483648}}, {id: RIGHT}, {id: UNRELATED}]");
    }

    [Theory]
    [InlineData(false, "if eq already_tracked 0; set name \"UNSAFE\"; end;")]
    [InlineData(true, "if eq already_tracked 0; set name \"UNSAFE\"; end;")]
    [InlineData(false, "loop var count 2; rules.getRuleItem item name; set name \"UNSAFE\"; end;")]
    [InlineData(true, "loop var count 2; rules.getRuleItem item name; set name \"UNSAFE\"; end;")]
    public void TextJoinsRetainUnsafeBranchAndLoopNames(bool fromCache, string assignments)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, Items + $$"""

            ufos:
              - type: UFO_SCOUT
                scripts:
                  detectUfoFromBase: 'var text name "SAFE"; var ptr RuleItem item; var ptr RuleDamageType damage; {{assignments}} rules.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth detection_chance 39; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
        AssertTimeBlocked(campaign, "Detection damage query exceeds the supported integer range.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullModDoesNotConsumeCopiedUnsafeName(bool fromCache, bool fromCraft) =>
        AssertDetectionReadScript("var text original \"UNSAFE\"; var text name original; var ptr RuleMod absent null; " +
            "var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
            "absent.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth value detection_chance; " +
            "if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;", fromCache, fromCraft, Items);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnforecastHostTextOutputRetainsConservativeDamageGuard(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, Items + """

            invs: [{id: UNSAFE}]
            ufos:
              - type: UFO_SCOUT
                scripts:
                  detectUfoFromBase: 'var text name "SAFE"; var ptr RuleInventory inventory; var ptr RuleItem item; var ptr RuleDamageType damage; rules.getRuleInventory inventory "UNSAFE"; inventory.getId name; rules.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth detection_chance 39; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
        AssertTimeBlocked(campaign, "Detection damage query exceeds the supported integer range.");
    }

    [Fact]
    public void TextNamesReserveWorkBeforeReferenceCandidateExpansion()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, Items);
        var compiled = ScriptCompiler.Compile("var text name \"SAFE\"; var ptr RuleItem item; " +
            "rules.getRuleItem item name; return;", ScriptParserDefinition.FromCatalog("detectUfoFromBase"));
        Assert.True(compiled.Succeeded);
        var program = compiled.Program!;
        var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        Assert.NotNull(WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 4096));
        Assert.Null(WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 8));
        Assert.Null(WorldDetectionTextForecast.Build(program, visits, 8, out var work));
        Assert.True(work > 8);
    }

    private const string Items = """
        items:
          - type: SAFE
            damageAlter: {ToHealth: 0.5}
          - type: SAFE_TOO
            damageAlter: {ToHealth: 0.5}
          - type: UNSAFE
            damageAlter: {ToHealth: 1e100}
          - type: STR_NONE
            damageAlter: {ToHealth: 1e100}
        """;
}
