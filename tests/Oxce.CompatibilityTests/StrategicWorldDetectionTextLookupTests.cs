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
    public void UnsafeHostIdentityOutputRetainsDamageGuard(bool fromCache)
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForecastsMapListSeparatorParametersToRuntimeOperandSlots(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache);
        var compiled = ScriptCompiler.Compile("var text name \"SUPPLY\"; var ptr RuleItem item; var int value; " +
            "rules.getRuleItem item name; loop var country geoscape_game.getCountries.list; " +
            "country.getPact value; end; rules.getRuleItem item name; return;",
            ScriptParserDefinition.FromCatalog("detectUfoFromBase"));
        Assert.True(compiled.Succeeded);
        var program = compiled.Program!;
        Assert.Null(WorldDetectionScriptSafety.UnavailableReason(program, _ => true, out var visits, 2));
        var text = WorldDetectionTextForecast.Build(program, visits, 20000, out _)!;
        var references = WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 20000)!;
        var scalars = WorldDetectionScalarForecast.Build(program, content.RuntimeRules, references, visits, 20000)!;
        var calls = program.Instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => (CoreScriptOperation)pair.instruction.Operation.Value == CoreScriptOperation.HostCall).ToArray();
        var lookup = calls.Last(pair => program.Bindings.Single(binding => binding.Id.Value == pair.instruction.Operands[0].Scalar)
            .Name == "RuleMod.getRuleItem");
        Assert.Equal("SUPPLY", Assert.Single(text.At(lookup.index, lookup.instruction.Operands[3])!));
        Assert.Same(content.RuntimeRules.Items[content.RuntimeRules.Items.GetRequired("SUPPLY")].Value,
            Assert.Single(references.At(lookup.index + 1, lookup.instruction.Operands[2])!));
        var pact = calls.Single(pair => program.Bindings.Single(binding => binding.Id.Value == pair.instruction.Operands[0].Scalar)
            .Name == "Country.getPact");
        Assert.Equal((0, 1), scalars.At(pact.index + 1, pact.instruction.Operands[2]));
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ImmutableMetadataNamesExcludeUnrelatedItemFamilies(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        Assert.Equal("", TestFixtures.Rows(oracle.RootElement, "ruleMetadata")[4].GetString());
        AssertDetectionReadScript("var ptr RuleUnit unit; var ptr RuleItem item; var ptr RuleDamageType damage; " +
            "var text name; var int value; rules.getRuleUnit unit \"TYPE_CHAIN\"; unit.getType name; " +
            "rules.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth value 39; " +
            "if neq value 19; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;", fromCache, fromCraft, Items + """

              - type: TYPE_CHAIN
                damageAlter: {ToHealth: 0.5}
            units: [{type: TYPE_CHAIN, armor: ARMOR}]
            """);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "inventoryMetadata"))
        {
            var id = row.GetProperty("id").GetString()!;
            AssertDetectionReadScript("var ptr RuleInventory inventory; var ptr RuleItem item; var ptr RuleDamageType damage; " +
                $"var text name; var int value; rules.getRuleInventory inventory \"{id}\"; inventory.getId name; " +
                "rules.getRuleItem item name; item.getDamageType damage; damage.getDamageToHealth value detection_chance; " +
                "if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;", fromCache, fromCraft, Items + """

                invs:
                  - id: STR_RIGHT_HAND
                  - id: STR_LEFT_HAND
                  - id: STR_BACK_PACK
                  - id: STR_BELT
                  - id: STR_GROUND
                  - id: STR_NONE
                """);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TextEqualityBranchesNarrowPossibleLookupNames(bool fromCache, bool fromCraft)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-numeric");
        foreach (var condition in new[] { "eq name \"SAFE\"", "neq name \"UNSAFE\"", "and eq name \"SAFE\" neq name \"UNSAFE\"" })
            AssertDetectionReadScript("var text name; var text copy; var ptr RuleItem item; var ptr RuleDamageType damage; " +
                "var int value; if eq already_tracked 0; set name \"SAFE\"; else; set name \"UNSAFE\"; end; " +
                $"if {condition}; set copy name; rules.getRuleItem item copy; item.getDamageType damage; " +
                "damage.getDamageToHealth value 39; if neq value 19; return DETECTION_NONE 0; end; end; return DETECTION_RADAR 100;",
                fromCache, fromCraft, Items);
        AssertDetectionReadScript("var text name \"SAFE\"; if eq name \"UNSAFE\"; div detection_chance 0; end; " +
            "if or eq name \"UNSAFE\" eq name \"\"; div detection_chance 0; else; " +
            "return DETECTION_RADAR 100; end; return;", fromCache, fromCraft);
    }

    [Fact]
    public void TextConditionsRemoveImpossibleNumericInstructionsFromReferenceForecast()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"]);
        var program = ScriptCompiler.Compile("var text name \"SAFE\"; if eq name \"UNSAFE\"; div detection_chance 0; end; " +
            "if or eq name \"UNSAFE\" eq name \"\"; div detection_chance 0; end; return;",
            ScriptParserDefinition.FromCatalog("detectUfoFromBase")).Program!;
        var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        var names = WorldDetectionTextForecast.Build(program, visits, 10000, out _, content.RuntimeRules)!;
        var references = WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 10000)!;
        var textRegister = program.Registers.Single(static register => register.Name == "name");
        var description = string.Join("\n", program.Instructions.Select((instruction, index) =>
            $"{index}: {(CoreScriptOperation)instruction.Operation.Value} {string.Join(",", instruction.Operands)} text={string.Join("|", names.At(index, ScriptOperand.Register(textRegister.Offset)) ?? [])}"));
        foreach (var (instruction, index) in program.Instructions.Select((instruction, index) => (instruction, index)))
            if ((CoreScriptOperation)instruction.Operation.Value == CoreScriptOperation.Divide)
            {
                Assert.False(names.Reachable(index), description);
                Assert.False(references.Reachable(index));
            }
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
