using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>RuleItem script readers from the pinned native metadata trace.</summary>
public sealed class StrategicWorldDetectionItemRulesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ItemAccuracyReadersMatchNativeDefaultsAndSignedValues(bool fromCache, bool fromCraft) =>
        RunItemReaderFixture("getAccuracy", fromCache, fromCraft);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ItemRawPowerArmorAndWaypointsMatchNative(bool fromCache, bool fromCraft) =>
        RunItemReaderFixture("get", fromCache, fromCraft);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ItemHandlingFlagsMatchNative(bool fromCache, bool fromCraft) =>
        RunItemReaderFixture("is", fromCache, fromCraft);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CategoriesMatchOrderedNativeReplacementIncludingCyclesAndUnicode(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "itemCategoryReplacement");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var categories = stage == 2 ? "[𐀀]" : "[A, B, C, FREE, A]";
            var definitions = stage switch
            {
                0 => """
                    itemCategories:
                      - type: C
                        replaceBy: A
                      - type: A
                        replaceBy: B
                      - type: B
                        replaceBy: C
                      - type: FREE
                    """,
                1 => """
                    itemCategories:
                      - type: C
                        replaceBy: B
                      - type: B
                        replaceBy: A
                      - type: A
                      - type: FREE
                    """,
                2 => """
                    itemCategories:
                      - type: 𐀀
                        replaceBy: 
                      - type: 
                        replaceBy: A
                      - type: A
                    """,
                _ => throw new InvalidOperationException("Unknown native category scenario."),
            };
            var code = "var ptr RuleItem item; var ptr RuleItem absent null; var int found; rules.getRuleItem item \"ITEM_READ\"; ";
            foreach (var row in rows[stage].EnumerateArray())
            {
                var category = row[0].GetString();
                var source = category == "NULL" ? "absent" : "item";
                code += $"{source}.hasCategory found \"{category}\"; if neq found {row[1].GetInt32()}; return DETECTION_NONE 0; end; ";
            }
            code += "return DETECTION_RADAR 100;";
            RunItemScript(code, $"categories: {categories}", fromCache, fromCraft, definitions);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageClassificationAndPointerIdentityMatchNative(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var predefined = row[0].GetInt32();
            var fields = predefined == -1 ? "" : $"damageType: {predefined}";
            var code = "var ptr RuleItem item; var ptr RuleItem other; var ptr RuleItem absent null; " +
                "var ptr RuleDamageType damage; var ptr RuleDamageType same; var ptr RuleDamageType melee; " +
                "var ptr RuleDamageType different; var ptr RuleDamageType missing; var int value; " +
                "rules.getRuleItem item \"ITEM_READ\"; rules.getRuleItem other \"ITEM_OTHER\"; " +
                "item.getDamageType damage; item.getDamageType same; item.getMeleeDamageType melee; " +
                "other.getDamageType different; absent.getDamageType missing; " +
                "if neq damage same; return DETECTION_NONE 0; end; " +
                "if eq damage melee; return DETECTION_NONE 0; end; " +
                "if eq damage different; return DETECTION_NONE 0; end; " +
                "if neq missing null; return DETECTION_NONE 0; end; ";
            foreach (var (reader, index) in new[] { ("getRandomType", 2), ("getResistType", 3), ("isDirect", 4),
                         ("isAreaOfEffect", 5), ("getArmorIgnore", 6), ("getArmorEffectiveness", 7) })
            {
                code += $"damage.{reader} value; if neq value {row[index].GetInt32()}; return DETECTION_NONE 0; end; " +
                    $"missing.{reader} value; if neq value 0; return DETECTION_NONE 0; end; ";
            }
            code += $"item.isSingleTarget value; if neq value {row[8].GetInt32()}; return DETECTION_NONE 0; end; " +
                "absent.isSingleTarget value; if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            RunItemScript(code, fields, fromCache, fromCraft);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageConversionsMatchNativeFloatTruncation(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "conversionRows"))
        {
            var type = row[0].GetInt32();
            var fields = type switch
            {
                -1 => "",
                20 => CustomDamageFields,
                _ => $"damageType: {type}",
            };
            var code = "var ptr RuleItem item; var ptr RuleDamageType damage; var ptr RuleDamageType absent null; " +
                "var int value; rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; ";
            for (var index = 0; index < DamageReaders.Length; index++)
                code += $"damage.{DamageReaders[index]} value {row[1].GetInt32()}; " +
                    $"if neq value {row[2][index].GetInt32()}; return DETECTION_NONE 0; end; " +
                    $"absent.{DamageReaders[index]} value 2147483647; if neq value 0; return DETECTION_NONE 0; end; ";
            code += "return DETECTION_RADAR 100;";
            RunItemScript(code, fields, fromCache, fromCraft);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageArmorPercentAndFloatBoundariesMatchNative(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "armorPercent"))
            RunItemScript(
                $"var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
                $"rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; damage.getArmorEffectiveness value; " +
                $"if neq value {row[1].GetInt32()}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
                $"damageAlter: {{ArmorEffectiveness: {row[0].GetRawText()}}}", fromCache, fromCraft);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "floatBoundaries"))
            RunItemScript(
                $"var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
                $"rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; damage.getDamageToHealth value {row[0].GetInt32()}; " +
                $"if neq value {row[2].GetInt32()}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
                $"damageAlter: {{ToHealth: {row[1].GetRawText()}}}", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false, "getDamageToHealth", "ToHealth: 1", "2147483647", "integer range")]
    [InlineData(true, "getDamageToHealth", "ToHealth: 1", "2147483647", "integer range")]
    [InlineData(false, "getDamageToHealth", "ToHealth: 1e100", "1", "integer range")]
    [InlineData(true, "getArmorEffectiveness", "ArmorEffectiveness: 1e100", "", "integer range")]
    [InlineData(false, "getDamageToHealth", "ToHealth: 1", "detection_chance", "bounded power forecast")]
    public void UnsafeDamageQueriesStopBeforeTickMutation(bool fromCache, string reader, string fields, string power, string reason)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            items:
              - type: ITEM_DAMAGE_GUARD
                damageAlter: { {{fields}} }
            ufos:
              - type: UFO_DAMAGE_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: 'var ptr RuleItem item; var ptr RuleDamageType damage; rules.getRuleItem item "ITEM_DAMAGE_GUARD"; item.getDamageType damage; damage.{{reader}} detection_chance {{power}}; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_DAMAGE_GUARD", 2200);
        AssertTimeBlocked(campaign, reason == "integer range"
            ? "Detection damage query exceeds the supported integer range."
            : "Detection damage conversion requires a bounded power forecast.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageAliasesAndBoundedScalarLocalsPreserveNativeResults(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var row = TestFixtures.Rows(oracle.RootElement, "conversionRows")
            .Single(row => row[0].GetInt32() == 20 && row[1].GetInt32() == -39);
        var code = "var ptr RuleItem item; var ptr RuleItem alias; var ptr RuleDamageType damage; " +
            "var ptr RuleDamageType copy; var int power -39; var int bounded; var int value; " +
            "rules.getRuleItem item \"ITEM_READ\"; set alias item; alias.getDamageType damage; " +
            "set copy damage; set damage copy; set bounded power; ";
        for (var index = 0; index < DamageReaders.Length; index++)
            code += $"copy.{DamageReaders[index]} value bounded; if neq value {row[2][index].GetInt32()}; return DETECTION_NONE 0; end; ";
        code += "return DETECTION_RADAR 100;";
        RunItemScript(code, CustomDamageFields, fromCache, fromCraft, additionalItems: UnsafeUnrelatedItem);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullProfileAliasesBypassUnconsumedPowerAndUnsafeItemData(bool fromCache, bool fromCraft)
    {
        RunItemScript("var ptr RuleDamageType absent null; var ptr RuleDamageType copy; var int value; " +
            "set copy absent; set absent copy; copy.getDamageToHealth value detection_chance; " +
            "if neq value 0; return DETECTION_NONE 0; end; " +
            "copy.getArmorEffectiveness value; if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "damageAlter: {ToHealth: 1e100, ArmorEffectiveness: 1e100}", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FullRangeScalarInputsAreAllowedForSafeDamageMultipliers(bool fromCache, bool fromCraft)
    {
        RunItemScript("var ptr RuleItem item; var ptr RuleDamageType damage; " +
            "rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; " +
            "damage.getDamageToHealth detection_chance radar_total_strength; return DETECTION_RADAR 100;",
            "damageAlter: {ToHealth: 0.5}", fromCache, fromCraft, additionalItems: UnsafeUnrelatedItem);
    }

    [Theory]
    [InlineData(false, "var int power 39; set power 2147483647;")]
    [InlineData(true, "var int power 39; var int source 2147483647; swap power source;")]
    [InlineData(false, "var int power 39; var int source; set source 2147483647; set power source;")]
    [InlineData(true, "var int power 39; add power detection_chance;")]
    public void ScalarReassignmentAndUnknownArithmeticCannotEscapeDamageBounds(bool fromCache, string assignments)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            items:
              - type: ITEM_DAMAGE_GUARD
            ufos:
              - type: UFO_DAMAGE_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: 'var ptr RuleItem item; var ptr RuleDamageType damage; {{assignments}} rules.getRuleItem item "ITEM_DAMAGE_GUARD"; item.getDamageType damage; damage.getDamageToHealth detection_chance power; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_DAMAGE_GUARD", 2200);
        AssertTimeBlocked(campaign, assignments.Contains("add", StringComparison.Ordinal)
            ? "Detection damage conversion requires a bounded power forecast."
            : "Detection damage query exceeds the supported integer range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileReassignmentThroughAliasesIncludesEveryPossibleWrite(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            items:
              {{UnsafeUnrelatedItem.ReplaceLineEndings("\n  ")}}
              - type: ITEM_DAMAGE_GUARD
            ufos:
              - type: UFO_DAMAGE_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: 'var ptr RuleItem item; var ptr RuleItem unsafe_item; var ptr RuleDamageType damage; var ptr RuleDamageType copy; rules.getRuleItem item "ITEM_DAMAGE_GUARD"; item.getDamageType damage; set copy damage; rules.getRuleItem unsafe_item "ITEM_UNSAFE"; unsafe_item.getDamageType damage; set copy damage; copy.getDamageToHealth detection_chance 39; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_DAMAGE_GUARD", 2200);
        AssertTimeBlocked(campaign, "Detection damage query exceeds the supported integer range.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageForecastUsesProfileAtTheCallRatherThanEarlierAssignments(bool fromCache, bool fromCraft)
    {
        RunItemScript("var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
            "rules.getRuleItem item \"ITEM_UNSAFE\"; item.getDamageType damage; " +
            "rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; damage.getDamageToHealth value 39; " +
            "if neq value 19; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "damageAlter: {ToHealth: 0.5}", fromCache, fromCraft, additionalItems: UnsafeUnrelatedItem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullModLookupDoesNotConsumeUnsafeDamageProfiles(bool fromCache)
    {
        RunItemScript("var ptr RuleMod absent null; var ptr RuleItem item; var ptr RuleDamageType damage; " +
            "var int value; absent.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; " +
            "damage.getDamageToHealth value detection_chance; if neq value 0; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;", "damageAlter: {ToHealth: 1e100}", fromCache, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SentinelItemLookupRemainsNullEvenWhenItsRuleExists(bool fromCache)
    {
        RunItemScript("var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
            "rules.getRuleItem item \"STR_NONE\"; item.getDamageType damage; " +
            "damage.getDamageToHealth value detection_chance; if neq value 0; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;", "", fromCache, false,
            additionalItems: "- type: STR_NONE\n  damageAlter: {ToHealth: 1e100}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamageForecastWorkBudgetStopsBeforeCandidateExpansion(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache);
        var parser = Oxce.Scripting.Compilation.ScriptParserDefinition.FromCatalog("detectUfoFromBase");
        var compiled = Oxce.Scripting.Compilation.ScriptCompiler.Compile(
            "var ptr RuleItem item; var ptr RuleDamageType damage; rules.getRuleItem item \"SUPPLY\"; " +
            "item.getDamageType damage; damage.getDamageToHealth detection_chance 39; return;", parser).Program!;
        var visits = Enumerable.Repeat(1, compiled.Instructions.Count).ToArray();
        Assert.Null(Oxce.Gameplay.Campaigns.World.WorldDetectionDamageQueries.UnavailableReason(
            compiled, content.RuntimeRules, visits, 1000));
        Assert.Equal("Detection damage query forecast exceeds the supported work limit.",
            Oxce.Gameplay.Campaigns.World.WorldDetectionDamageQueries.UnavailableReason(
                compiled, content.RuntimeRules, visits, 3));
    }

    private const string UnsafeUnrelatedItem = """
        - type: ITEM_UNSAFE
          damageAlter: {ToHealth: 1e100, ArmorEffectiveness: 1e100}
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TextAndTypedTagCopiesMatchNativeBindSet(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-value-copy");
        var texts = TestFixtures.Rows(oracle.RootElement, "text");
        var tag = oracle.RootElement.GetProperty("tagCopy").GetInt32();
        var code = $"var text original \"{texts[0].GetString()}\"; var text copy; " +
            "var ptr RuleItem item; var RuleItem.Tag selected; var RuleItem.Tag alias; var int value; " +
            $"set copy original; if neq copy \"{texts[0].GetString()}\"; return DETECTION_NONE 0; end; " +
            $"set copy \"{texts[1].GetString()}\"; if neq copy \"{texts[1].GetString()}\"; return DETECTION_NONE 0; end; " +
            "set selected Tag.COPY; set alias selected; rules.getRuleItem item \"ITEM_READ\"; item.getTag value alias; " +
            $"if neq value {tag}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
        RunItemScript(code, $"tags: {{COPY: {tag}}}", fromCache, fromCraft, """
            extended:
              tags:
                RuleItem: {COPY: int}
            """);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SoldierTypeBindingRetainsNativeSkillReceiver(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-value-copy");
        var types = TestFixtures.Rows(oracle.RootElement, "soldierType");
        RunItemScript("var ptr RuleSkill skill; var ptr RuleSkill absent null; var text id; " +
            $"rules.getRuleSkill skill \"{types[0].GetString()}\"; RuleSoldier.getType skill id; " +
            $"if neq id \"{types[0].GetString()}\"; return DETECTION_NONE 0; end; " +
            $"RuleSoldier.getType absent id; if neq id \"{types[1].GetString()}\"; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "", fromCache, fromCraft, $"skills: [{{type: {types[0].GetString()}}}]");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RandomDamageNullStateAndProfilePreserveNativeShortCircuit(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var rows = TestFixtures.Rows(oracle.RootElement, "nullRandomDamage");
        Assert.Equal(0, oracle.RootElement.GetProperty("unusedRandomCalls").GetInt32());
        var code = "var ptr RuleItem item; var ptr RuleDamageType damage; var ptr RuleDamageType absent null; " +
            "var ptre RandomState random null; var ptre RandomState copy; var int value; " +
            "set copy random; rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; " +
            $"damage.getRandomDamage value 2147483647 copy; if neq value {rows[0].GetInt32()}; return DETECTION_NONE 0; end; " +
            $"absent.getRandomDamage value -2147483648 random; if neq value {rows[1].GetInt32()}; return DETECTION_NONE 0; end; " +
            $"absent.getRandomDamage value detection_chance copy; if neq value {rows[2].GetInt32()}; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;";
        RunItemScript(code, "damageAlter: {ToHealth: 1e100}", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ItemPowerReadersFeedBoundedNativeDamageConversions(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        var row = TestFixtures.Rows(oracle.RootElement, "conversionRows")
            .Single(row => row[0].GetInt32() == 20 && row[1].GetInt32() == -39);
        var code = "var ptr RuleItem item; var ptr RuleDamageType damage; var int power; var int melee; var int value; " +
            "rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; item.getPower power; item.getMeleePower melee; ";
        for (var index = 0; index < DamageReaders.Length; index++)
            code += $"damage.{DamageReaders[index]} value power; if neq value {row[2][index].GetInt32()}; return DETECTION_NONE 0; end; " +
                $"damage.{DamageReaders[index]} value melee; if neq value {row[2][index].GetInt32()}; return DETECTION_NONE 0; end; ";
        code += "set power 2147483647; return DETECTION_RADAR 100;";
        RunItemScript(code, CustomDamageFields + "\npower: -39\nmeleePower: -39",
            fromCache, fromCraft, additionalItems: UnsafeUnrelatedItem);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RulePowerOverwriteBoundsCallerOutputsBeforeConversion(bool fromCache, bool fromCraft)
    {
        RunItemScript("var ptr RuleItem item; var ptr RuleDamageType damage; " +
            "rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; item.getPower detection_chance; " +
            "damage.getDamageToHealth detection_chance detection_chance; " +
            "if neq detection_chance 19; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "power: 39\ndamageAlter: {ToHealth: 0.5}", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopCarriedPowerOverflowStopsBeforeTickMutation(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            items:
              - type: SAFE_POWER
                power: 39
              - type: UNSAFE_POWER
                power: 2147483647
            ufos:
              - type: UFO_POWER_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: 'var ptr RuleItem item; var ptr RuleItem alternate; var ptr RuleDamageType damage; var int power; rules.getRuleItem item "SAFE_POWER"; rules.getRuleItem alternate "UNSAFE_POWER"; item.getDamageType damage; item.getPower power; loop var i 2; damage.getDamageToHealth detection_chance power; alternate.getPower power; end; return;'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_POWER_GUARD", 2200);
        AssertTimeBlocked(campaign, "Detection damage query exceeds the supported integer range.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullRandomDamageFeedsAZeroIntervalToLaterConversions(bool fromCache, bool fromCraft)
    {
        RunItemScript("var ptr RuleItem item; var ptr RuleDamageType damage; var ptre RandomState state null; " +
            "var int power; var int value; rules.getRuleItem item \"ITEM_READ\"; item.getDamageType damage; " +
            "damage.getRandomDamage power 2147483647 state; damage.getDamageToHealth value power; " +
            "if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarForecastReservesWorkAndReadsItemPowerAtTheCall(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, "items: [{type: POWER_QUERY, power: 39}]");
        var parser = Oxce.Scripting.Compilation.ScriptParserDefinition.FromCatalog("detectUfoFromBase");
        var program = Oxce.Scripting.Compilation.ScriptCompiler.Compile(
            "var ptr RuleItem item; var ptr RuleDamageType damage; var int power; var int value; " +
            "rules.getRuleItem item \"POWER_QUERY\"; item.getDamageType damage; item.getPower power; " +
            "damage.getDamageToHealth value power; return;", parser).Program!;
        var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        var references = Oxce.Gameplay.Campaigns.World.WorldDetectionReferenceForecast.Build(
            program, content.RuntimeRules, visits, 1000)!;
        var forecast = Oxce.Gameplay.Campaigns.World.WorldDetectionScalarForecast.Build(
            program, content.RuntimeRules, references, visits, 1000)!;
        var query = program.Instructions.Select((instruction, index) => (instruction, index))
            .Single(pair => pair.instruction.Operands.Count > 0 &&
                pair.instruction.Operands[0].Kind == Oxce.Scripting.Compilation.ScriptOperandKind.Binding &&
                program.Bindings.Any(binding => binding.Id.Value == pair.instruction.Operands[0].Scalar &&
                    binding.Name == "RuleDamageType.getDamageToHealth"));
        Assert.Equal((39, 39), forecast.At(query.index, query.instruction.Operands[3]));
        Assert.Null(Oxce.Gameplay.Campaigns.World.WorldDetectionScalarForecast.Build(
            program, content.RuntimeRules, references, visits, 8));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DefaultTextCopiesNativeEmptyValue(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-value-copy");
        var empty = TestFixtures.Rows(oracle.RootElement, "text")[1].GetString();
        RunItemScript("var text original; var text copy; set copy original; " +
            $"if neq copy \"{empty}\"; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExplicitTextClearCopiesNativeEmptyValue(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-value-copy");
        var empty = oracle.RootElement.GetProperty("clearedText").GetString();
        RunItemScript("var text original \"copied\"; var text copy; clear original; set copy original; " +
            $"if neq copy \"{empty}\"; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            "", fromCache, fromCraft);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReusedBlockRegistersRemainValidForReferenceAndScalarForecasts(bool fromCache, bool fromCraft)
    {
        RunItemScript("var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
            "rules.getRuleItem item \"ITEM_READ\"; " +
            "begin; var ptr RuleItem local item; var int power 39; local.getDamageType damage; " +
            "damage.getDamageToHealth value power; if neq value 39; return DETECTION_NONE 0; end; end; " +
            "begin; var ptr RuleItem local item; var int power 19; local.getDamageType damage; " +
            "damage.getDamageToHealth value power; if neq value 19; return DETECTION_NONE 0; end; end; " +
            "return DETECTION_RADAR 100;", "", fromCache, fromCraft);
    }

    private static readonly string[] DamageReaders =
        ["getDamageToArmorPre", "getDamageToArmor", "getDamageToEnergy", "getDamageToHealth",
         "getDamageToItem", "getDamageToMana", "getDamageToMorale", "getDamageToStun",
         "getDamageToTile", "getDamageToTime", "getDamageToWound"];

    private const string CustomDamageFields = """
        damageAlter:
          ToArmorPre: -0.125
          ToArmor: 0.375
          ToEnergy: -1.125
          ToHealth: 0.5
          ToItem: 1.25
          ToMana: -0.75
          ToMorale: 2.125
          ToStun: 0.025
          ToTile: -0.01
          ToTime: 0.1
          ToWound: 0.33333333
        """;

    private static void RunItemReaderFixture(string prefix, bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "itemReaders");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var fields = rows[stage].EnumerateObject().Where(field => field.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(fields);
            var code = "var ptr RuleItem item; var ptr RuleItem absent null; var int value; rules.getRuleItem item \"ITEM_READ\"; ";
            foreach (var field in fields)
            {
                code += $"item.{field.Name} value; if neq value {field.Value.GetInt32()}; return DETECTION_NONE 0; end; ";
                code += $"absent.{field.Name} value; if neq value 0; return DETECTION_NONE 0; end; ";
            }
            code += "return DETECTION_RADAR 100;";
            RunItemScript(code, stage == 0 ? "" : CustomFields, fromCache, fromCraft);
        }
    }

    private static void RunItemScript(string code, string fields, bool fromCache, bool fromCraft, string extra = "", string additionalItems = "")
    {
        var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            {{extra}}
            items:
              {{additionalItems.ReplaceLineEndings("\n  ")}}
              - type: ITEM_OTHER
              - type: ITEM_READ
                {{fields.ReplaceLineEndings("\n    ")}}
            ufos:
              - type: UFO_ITEM_READ
                speedMax: 2200
                damageMax: 50
                scripts:
                  {{parser}}: '{{code}}'
            """);
        var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_ITEM_READ", 2200).Capture();
        if (fromCraft)
            snapshot = snapshot.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with { Status = "STR_OUT", Speed = 0, Destination = null },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(215));
        campaign = Reload(content, campaign, 215, "item-read-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 215, "item-read-after.sav");
    }

    private const string CustomFields = """
        accuracyAimed: -7
        accuracyAuto: 2147483647
        accuracySnap: 83
        accuracyMelee: 41
        accuracyUse: 27
        accuracyMindControl: -11
        accuracyPanic: -2147483648
        accuracyThrow: 120
        power: -9
        meleePower: 2147483647
        armor: 0
        waypoints: -1
        twoHanded: true
        blockBothHands: true
        underwaterOnly: true
        """;
}
