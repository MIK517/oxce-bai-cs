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
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryCostsProjectSignedValuesAndReplacementAcrossCache(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-inventory-cost");
        var rows = TestFixtures.Rows(oracle.RootElement, "costs");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            invs:
              - id: RIGHT
                refNode:
                  costs: {REMOVED: 12}
                costs: {LEFT: -7, GROUND: 2147483647, RIGHT: 99}
              - id: LEFT
                costs: {RIGHT: -2147483648}
              - id: GROUND
            """);
        var rules = content.RuntimeRules.Inventories;
        var right = rules[rules.GetRequired("RIGHT")].Value;
        var left = rules[rules.GetRequired("LEFT")].Value;
        Assert.Equal(rows[1][0].GetInt32(), right.Costs["LEFT"]);
        Assert.Equal(rows[2][0].GetInt32(), right.Costs["GROUND"]);
        Assert.Equal(rows[3][0].GetInt32(), left.Costs["RIGHT"]);
        Assert.Equal(99, right.Costs["RIGHT"]);
        Assert.False(right.Costs.ContainsKey("REMOVED"));
        Assert.Empty(rules[rules.GetRequired("GROUND")].Value.Costs);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)right.Costs).Add("NEW", 1));
    }

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InventoryTypeAndHandReadersMatchNativeBindings(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var code = "var ptr RuleInventory inventory; var ptr RuleInventory absent null; var int type; var int right; var int left; ";
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "inventoryMetadata"))
        {
            code += $"rules.getRuleInventory inventory \"{row.GetProperty("id").GetString()}\"; ";
            code += $"inventory.getType type; if neq type {row.GetProperty("type").GetInt32()}; return DETECTION_NONE 0; end; ";
            code += $"inventory.isRightHand right; if neq right {(row.GetProperty("right").GetBoolean() ? 1 : 0)}; return DETECTION_NONE 0; end; ";
            code += $"inventory.isLeftHand left; if neq left {(row.GetProperty("left").GetBoolean() ? 1 : 0)}; return DETECTION_NONE 0; end; ";
        }
        code += "absent.getType type; absent.isRightHand right; absent.isLeftHand left; if neq type 0; return DETECTION_NONE 0; end; if neq right 0; return DETECTION_NONE 0; end; if neq left 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
        RunInventoryScript(code, fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConventionalInventoryAccessorsMatchNativeAndNullReceivers(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var ids = TestFixtures.Rows(oracle.RootElement, "inventoryConventional");
        var methods = new[] { "RightHand", "LeftHand", "Backpack", "Belt", "Ground" };
        Assert.Equal(methods.Length, ids.Length);
        var code = "var ptr RuleInventory inventory; var ptr RuleInventory generic; var ptr RuleMod absent null; var text id; ";
        for (var index = 0; index < methods.Length; index++)
        {
            var id = ids[index].GetString();
            code += $"rules.getRuleInventory{methods[index]} inventory; inventory.getId id; if neq id \"{id}\"; return DETECTION_NONE 0; end; ";
            code += $"rules.getRuleInventory generic \"{id}\"; if neq inventory generic; return DETECTION_NONE 0; end; ";
            code += $"absent.getRuleInventory{methods[index]} inventory; if neq inventory null; return DETECTION_NONE 0; end; ";
        }
        code += "return DETECTION_RADAR 100;";
        RunInventoryScript(code, fromCache, fromCraft);

        // The member binding returns null before consulting a missing role.
        code = "var ptr RuleInventory inventory; var ptr RuleMod absent; ";
        foreach (var method in methods)
            code += $"absent.getRuleInventory{method} inventory; if neq inventory null; return DETECTION_NONE 0; end; ";
        code += "return DETECTION_RADAR 100;";
        RunInventoryScript(code, fromCache, fromCraft, MissingInventoryRules);
    }

    [Theory]
    [InlineData("RightHand", "STR_RIGHT_HAND")]
    [InlineData("LeftHand", "STR_LEFT_HAND")]
    [InlineData("Backpack", "STR_BACK_PACK")]
    [InlineData("Belt", "STR_BELT")]
    [InlineData("Ground", "STR_GROUND")]
    public void MissingConventionalInventoryStopsRuleAndGlobalScriptsBeforeMutation(string method, string id)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        Assert.True(oracle.RootElement.GetProperty("missingInventoryThrows").GetBoolean());
        foreach (var fromCache in new[] { false, true })
            foreach (var global in new[] { false, true })
            {
                var code = $"var ptr RuleInventory inventory; rules.getRuleInventory{method} inventory; return DETECTION_RADAR 100;";
                var script = global ? $$"""
                    extended:
                      scripts:
                        detectUfoFromBase:
                          - new: inventory-read
                            offset: -1
                            code: '{{code}}'
                    """ : $$"""
                    ufos:
                      - type: UFO_SCOUT
                        scripts:
                          detectUfoFromBase: '{{code}}'
                    """;
                var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, MissingInventoryRules + "\n" + script);
                var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
                AssertTimeBlocked(campaign, $"Detection script requires missing inventory {id}.");
                campaign = Reload(content, campaign, 214, "missing-inventory-before.sav");
                AssertTimeBlocked(campaign, $"Detection script requires missing inventory {id}.");
            }
    }

    private const string MissingInventoryRules = """
        invs:
          - delete: STR_RIGHT_HAND
          - delete: STR_LEFT_HAND
          - delete: STR_BACK_PACK
          - delete: STR_BELT
          - delete: STR_GROUND
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryTypeAndHandAreIndependent(bool fromCache)
    {
        RunInventoryScript("""
            var ptr RuleInventory inventory; var int type; var int hand;
            rules.getRuleInventory inventory "STR_RIGHT_HAND";
            inventory.getType type; if neq type 0; return DETECTION_NONE 0; end;
            inventory.isRightHand hand; if neq hand 1; return DETECTION_NONE 0; end;
            rules.getRuleInventory inventory "CUSTOM_HAND";
            inventory.getType type; if neq type 1; return DETECTION_NONE 0; end;
            inventory.isRightHand hand; if neq hand 0; return DETECTION_NONE 0; end;
            inventory.isLeftHand hand; if neq hand 0; return DETECTION_NONE 0; end;
            return DETECTION_RADAR 100;
            """.ReplaceLineEndings(" "), fromCache, false, """
            invs:
              - id: STR_RIGHT_HAND
                type: 0
              - id: CUSTOM_HAND
                type: 1
            """);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonNullModAliasCannotBypassMissingInventoryGuard(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, MissingInventoryRules + """

            ufos:
              - type: UFO_SCOUT
                scripts:
                  detectUfoFromBase: 'var ptr RuleMod actual rules; var ptr RuleInventory inventory; actual.getRuleInventoryGround inventory; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
        AssertTimeBlocked(campaign, "Detection script requires missing inventory STR_GROUND.");
    }

    [Fact]
    public void InventoryMovementRemainsPreciselyGuarded()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, InventoryRules + """

            ufos:
              - type: UFO_SCOUT
                scripts:
                  detectUfoFromBase: 'var ptr RuleInventory source; var ptr RuleInventory target null; var int cost; rules.getRuleInventory source "STR_RIGHT_HAND"; source.getMoveToCost cost target; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
        AssertTimeBlocked(campaign, "Detection inventory movement requires a nonnull destination forecast.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InventoryMovementCostsMatchNativeIdentityNullAndSignedValues(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-inventory-cost");
        var rows = TestFixtures.Rows(oracle.RootElement, "costs");
        var pairs = new[] { ("RIGHT", "RIGHT"), ("RIGHT", "LEFT"), ("RIGHT", "GROUND"),
            ("LEFT", "RIGHT"), ("MISSING", "RIGHT"), ("MISSING", "MISSING"), ("GROUND", "GROUND") };
        Assert.Equal(rows.Length, pairs.Length);
        var code = "var ptr RuleInventory source; var ptr RuleInventory target; var int cost; ";
        for (var index = 0; index < pairs.Length; index++)
            code += $"rules.getRuleInventory source \"{pairs[index].Item1}\"; " +
                $"rules.getRuleInventory target \"{pairs[index].Item2}\"; source.getMoveToCost cost target; " +
                $"if neq cost {rows[index][0].GetInt32()}; return DETECTION_NONE 0; end; ";
        code += "return DETECTION_RADAR 100;";
        RunInventoryScript(code, fromCache, fromCraft, MovementRules);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeMovementCostOutputsFeedCalculatedQueries(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-inventory-cost");
        var rows = TestFixtures.Rows(oracle.RootElement, "costs");
        var pairs = new[] { ("RIGHT", "RIGHT"), ("RIGHT", "LEFT"), ("RIGHT", "GROUND"),
            ("LEFT", "RIGHT"), ("MISSING", "RIGHT"), ("MISSING", "MISSING"), ("GROUND", "GROUND") };
        Assert.Equal(rows.Length, pairs.Length);
        var code = "var ptr RuleInventory source; var ptr RuleInventory target; var int cost; var int value; var int quotient; " +
            "var ptr RuleItem item; var ptr RuleDamageType damage; rules.getRuleItem item \"HALF_DAMAGE\"; item.getDamageType damage; ";
        for (var index = 0; index < pairs.Length; index++)
        {
            var expected = rows[index][0].GetInt32();
            code += $"rules.getRuleInventory source \"{pairs[index].Item1}\"; " +
                $"rules.getRuleInventory target \"{pairs[index].Item2}\"; source.getMoveToCost cost target; " +
                "damage.getDamageToHealth value cost; " +
                $"if neq value {(int)(0.5f * expected)}; return DETECTION_NONE 0; end; ";
            if (expected != 0)
                code += $"set quotient {expected}; div quotient cost; if neq quotient 1; return DETECTION_NONE 0; end; ";
        }
        code += "return DETECTION_RADAR 100;";
        RunInventoryScript(code, fromCache, fromCraft, MovementRules + """

            items: [{type: HALF_DAMAGE, damageAlter: {ToHealth: 0.5}}]
            """);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalSameRegisterMovementAlwaysPreservesIdentity(bool fromCache)
    {
        RunInventoryScript("var ptr RuleInventory source; var int cost; " +
            "if eq already_tracked 0; rules.getRuleInventory source \"RIGHT\"; " +
            "else; rules.getRuleInventory source \"LEFT\"; end; source.getMoveToCost cost source; " +
            "if neq cost 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            fromCache, false, MovementRules);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalMissingMovementCostStopsBeforeTick(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, MovementRules + """

            ufos:
              - type: UFO_SCOUT
                scripts:
                  detectUfoFromBase: 'var ptr RuleInventory source; var ptr RuleInventory target; var int cost; rules.getRuleInventory source "LEFT"; if eq already_tracked 0; rules.getRuleInventory target "RIGHT"; else; rules.getRuleInventory target "GROUND"; end; source.getMoveToCost cost target; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
        AssertTimeBlocked(campaign, "Detection inventory movement requires missing cost LEFT -> GROUND.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullCheckedMovementDestinationAndAliasAreForecastAtTheCall(bool fromCache, bool fromCraft)
    {
        RunInventoryScript("var ptr RuleInventory source; var ptr RuleInventory target; var ptr RuleInventory alias; var int cost; " +
            "rules.getRuleInventory source \"RIGHT\"; " +
            "if eq already_tracked 0; rules.getRuleInventory target \"LEFT\"; else; rules.getRuleInventory target \"MISSING\"; end; " +
            "if neq target null; set alias target; source.getMoveToCost cost alias; " +
            "if neq cost -7; return DETECTION_NONE 0; end; end; return DETECTION_RADAR 100;",
            fromCache, fromCraft, MovementRules);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullModAliasesBypassMissingConventionalInventoryOnlyOnNullPath(bool fromCache)
    {
        RunInventoryScript("var ptr RuleMod absent null; var ptr RuleMod alias; var ptr RuleInventory target; " +
            "set alias absent; alias.getRuleInventoryGround target; if neq target null; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;", fromCache, false, MissingInventoryRules);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopCarriedNullDestinationsCannotEscapeMovementForecast(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, MovementRules + """

            ufos:
              - type: UFO_SCOUT
                scripts:
                  detectUfoFromBase: 'var ptr RuleInventory source; var ptr RuleInventory target; var int cost; rules.getRuleInventory source "RIGHT"; rules.getRuleInventory target "LEFT"; loop var i 2; source.getMoveToCost cost target; rules.getRuleInventory target "MISSING"; end; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 2200);
        AssertTimeBlocked(campaign, "Detection inventory movement requires a nonnull destination forecast.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitNullModBranchDoesNotRequireMissingConventionalInventory(bool fromCache)
    {
        RunInventoryScript("var ptr RuleMod absent null; var ptr RuleMod actual rules; var ptr RuleInventory target; " +
            "if eq already_tracked 0; set actual absent; end; if eq actual null; " +
            "actual.getRuleInventoryGround target; if neq target null; return DETECTION_NONE 0; end; end; " +
            "return DETECTION_RADAR 100;", fromCache, false, MissingInventoryRules);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceForecastBudgetRejectsBeforeStateExpansion(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, MovementRules);
        var parser = Oxce.Scripting.Compilation.ScriptParserDefinition.FromCatalog("detectUfoFromBase");
        var program = Oxce.Scripting.Compilation.ScriptCompiler.Compile(
            "var ptr RuleInventory source; var ptr RuleInventory target; var int cost; " +
            "rules.getRuleInventory source \"RIGHT\"; rules.getRuleInventory target \"LEFT\"; " +
            "source.getMoveToCost cost target; return;", parser).Program!;
        var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        Assert.NotNull(Oxce.Gameplay.Campaigns.World.WorldDetectionReferenceForecast.Build(
            program, content.RuntimeRules, visits, 1000));
        Assert.Null(Oxce.Gameplay.Campaigns.World.WorldDetectionReferenceForecast.Build(
            program, content.RuntimeRules, visits, 8));
        Assert.Equal("Detection inventory cost forecast exceeds the supported work limit.",
            Oxce.Gameplay.Campaigns.World.WorldDetectionInventoryCosts.UnavailableReason(
                program, content.RuntimeRules, visits, 8));
    }

    private const string MovementRules = """
        invs:
          - id: RIGHT
            costs: {LEFT: -7, GROUND: 2147483647, RIGHT: 99}
          - id: LEFT
            costs: {RIGHT: -2147483648}
          - id: GROUND
        """;

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
