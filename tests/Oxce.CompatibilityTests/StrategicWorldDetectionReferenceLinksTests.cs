using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionReferenceLinksTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DynamicModFamilyLookupsProveNativeNullBranches(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "ruleLookup"))
        {
            var receiver = row[0].GetInt32() != 0 ? "rules" : "absent";
            var known = row[1].GetInt32() != 0;
            var calls = new[] { ("getRuleItem", "RuleItem", "SUPPLY", 2), ("getRuleArmor", "RuleArmor", "ARMOR", 3),
                ("getRuleSoldier", "RuleSoldier", "RECRUIT", 4), ("getRuleSkill", "RuleSkill", "SKILL_LOOKUP", 5) };
            var code = "var ptr RuleMod absent null; var text name; " +
                string.Concat(calls.Select((call, index) => $"var ptr {call.Item2} value{index}; "));
            for (var index = 0; index < calls.Length; index++)
            {
                var call = calls[index];
                code += $"set name \"{(known ? call.Item3 : "MISSING")}\"; " +
                    $"{receiver}.{call.Item1} value{index} name; " +
                    $"if {(row[call.Item4].GetInt32() != 0 ? "eq" : "neq")} value{index} null; div detection_chance 0; end; ";
            }
            code += "return DETECTION_RADAR 100;";
            AssertDetectionReadScript(code, fromCache, fromCraft, "skills: [{type: SKILL_LOOKUP}]");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnitAndResearchLinksExcludeUnrelatedUnsafeDamageRules(bool fromCache, bool fromCraft)
    {
        AssertDetectionReadScript("var text name \"UNIT_LINK\"; var ptr RuleUnit unit; var ptr RuleUnit spawned; " +
            "var ptr RuleItem item; var ptr RuleResearch research; var ptr RuleResearch lookup; " +
            "var ptr RuleDamageType damage; var int value; rules.getRuleUnit unit name; unit.getSpawnUnit spawned; " +
            "spawned.getLiveAlienGeoscape item; item.getDamageType damage; damage.getDamageToHealth value 39; " +
            "if neq value 19; return DETECTION_NONE 0; end; " +
            "spawned.getCivilianRecoveryItemType item; item.getDamageType damage; damage.getDamageToHealth value 39; " +
            "if neq value 19; return DETECTION_NONE 0; end; " +
            "set name \"RESEARCH_LINK\"; rules.getRuleResearch research name; research.getLookup lookup; lookup.getNeededItem item; " +
            "item.getDamageType damage; damage.getDamageToHealth value 39; " +
            "if neq value 19; return DETECTION_NONE 0; end; " +
            "set name \"MISSING\"; rules.getRuleUnit unit name; unit.getLiveAlienGeoscape item; " +
            "item.getDamageType damage; damage.getDamageToHealth value detection_chance; " +
            "if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;", fromCache, fromCraft, """
            items:
              - type: LINK_ITEM
                damageAlter: {ToHealth: 0.5}
              - type: UNSAFE_LINK_ITEM
                damageAlter: {ToHealth: 1e100}
            units:
              - type: UNIT_LINK
                armor: ARMOR
                liveAlien: LINK_ITEM
                civilianRecoveryType: LINK_ITEM
                spawnUnit: UNIT_LINK
            research:
              - name: RESEARCH_LINK
                cost: 1
                lookup: RESEARCH_ITEM
              - name: RESEARCH_ITEM
                cost: 1
                needItem: true
                neededItem: LINK_ITEM
            """);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullEntityRuleAndClockLinksStayNullThroughQueryComposition(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "nullMemberOutputs"))
        {
            Assert.Equal(2, row.GetArrayLength());
            Assert.All(row.EnumerateArray(), static value => Assert.Equal(0, value.GetInt32()));
        }
        AssertDetectionReadScript("var ptr Ufo absentUfo null; var ptr Craft absentCraft null; var ptr GeoscapeGame absentGame null; " +
            "var ptr RuleUfo ufoRule; var ptr RuleCraft craftRule; var ptr Time time; var ptr RuleItem item; " +
            "var ptr RuleDamageType damage; var int power; var int value; rules.getRuleItem item \"NULL_LINK_QUERY\"; " +
            "item.getDamageType damage; absentUfo.getRules ufoRule; ufoRule.getWeaponPower power; " +
            "damage.getDamageToHealth value power; if neq value 0; return DETECTION_NONE 0; end; " +
            "if neq ufoRule null; div detection_chance 0; end; absentUfo.getRuleUfo ufoRule; " +
            "if neq ufoRule null; div detection_chance 0; end; " +
            (fromCraft ? "absentCraft.getRuleCraft craftRule; " +
            "craftRule.Stats.getSpeedMax power; damage.getDamageToHealth value power; " +
            "if neq value 0; return DETECTION_NONE 0; end; if neq craftRule null; div detection_chance 0; end; " : "") +
            "absentGame.getTime time; time.getHour power; damage.getDamageToHealth value power; " +
            "if neq value 0; return DETECTION_NONE 0; end; if neq time null; div detection_chance 0; end; " +
            "return DETECTION_RADAR 100;", fromCache, fromCraft,
            "items: [{type: NULL_LINK_QUERY, damageAlter: {ToHealth: 1}}]");
    }

}
