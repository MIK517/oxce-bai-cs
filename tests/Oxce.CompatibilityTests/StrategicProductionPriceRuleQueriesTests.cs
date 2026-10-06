using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ArmorAndSkillLookupsRetainNativeMissingAndNullRules(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var code = "var ptr RuleMod absent null; var ptr RuleArmor armor; var ptr RuleSkill skill; ";
        var rows = TestFixtures.Rows(oracle.RootElement, "ruleLookup");
        foreach (var row in rows)
        {
            var receiver = row[0].GetInt32() == 0 ? "absent" : "rules";
            var name = row[1].GetInt32() == 0 ? "MISSING" : "PRICE_LOOKUP";
            code += $"{receiver}.getRuleArmor armor \"{name}\"; if {(row[3].GetInt32() == 0 ? "neq" : "eq")} armor null; div cost_current 0; end; " +
                $"{receiver}.getRuleSkill skill \"{name}\"; if {(row[5].GetInt32() == 0 ? "neq" : "eq")} skill null; div cost_current 0; end; ";
        }
        AssertPrice(Load(cache, global, code + "return 7;", extraRules:
            "armors: [{type: PRICE_LOOKUP, corpseBattle: [CORPSE], corpseGeo: CORPSE, loftempsSet: [0]}]\nskills: [{type: PRICE_LOOKUP}]"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ArmorVisionAndSkillIdentityRetainNativeReadersAndTags(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        using var copy = TestFixtures.ReadVerifiedExpected("strategic-detection-value-copy");
        var skillId = TestFixtures.Rows(copy.RootElement, "soldierType")[0].GetString()!;
        var rows = TestFixtures.Rows(oracle.RootElement, "armorVision");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var fields = stage == 0 ? "" : string.Join('\n', rows[stage].EnumerateObject().Select(field =>
                $"    {char.ToLowerInvariant(field.Name[3])}{field.Name[4..]}: {field.Value.GetInt32()}"));
            var code = "var ptr RuleArmor armor; var ptr RuleArmor same; var ptr RuleArmor absent null; " +
                "var ptr RuleSkill skill; var ptr RuleSkill absentSkill null; var int value; var text id; " +
                "rules.getRuleArmor armor \"PRICE_ARMOR\"; rules.getRuleArmor same \"PRICE_ARMOR\"; " +
                "if neq armor same; div cost_current 0; end; armor.getType id; if neq id \"PRICE_ARMOR\"; div cost_current 0; end; " +
                "absent.getType id; if neq id \"\"; div cost_current 0; end; " +
                $"rules.getRuleSkill skill \"{skillId}\"; skill.getType id; if neq id \"{skillId}\"; div cost_current 0; end; " +
                $"RuleSoldier.getType skill id; if neq id \"{skillId}\"; div cost_current 0; end; " +
                "RuleSoldier.getType absentSkill id; if neq id \"\"; div cost_current 0; end; " +
                "armor.getTag value Tag.PRICE_ARMOR_TAG; if neq value -7; div cost_current 0; end; " +
                "skill.getTag value Tag.PRICE_SKILL_TAG; if neq value 31; div cost_current 0; end; " +
                "absent.getTag value Tag.PRICE_ARMOR_TAG; if neq value 0; div cost_current 0; end; " +
                "absentSkill.getTag value Tag.PRICE_SKILL_TAG; if neq value 0; div cost_current 0; end; ";
            foreach (var field in rows[stage].EnumerateObject())
                code += $"armor.{field.Name} value; if neq value {field.Value.GetInt32()}; div cost_current 0; end; " +
                    $"absent.{field.Name} value; if neq value 0; div cost_current 0; end; ";
            AssertPrice(Load(cache, global, code + "return 7;", extraRules:
                "armors:\n  - type: PRICE_ARMOR\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n" +
                "    tags: {PRICE_ARMOR_TAG: -7}\n" + fields + $"\nskills: [{{type: {skillId}, tags: {{PRICE_SKILL_TAG: 31}}}}]",
                extraExtended: "tags:\n  RuleArmor: {PRICE_ARMOR_TAG: int}\n  RuleSkill: {PRICE_SKILL_TAG: int}"));
        }
    }
}
