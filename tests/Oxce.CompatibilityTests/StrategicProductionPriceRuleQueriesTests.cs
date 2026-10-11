using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSoldierAndArmorStatWidthsAndTagsComposeWithPrices(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "unitStats");
        Assert.Equal(4, rows.Length);
        var yaml = new[] { "minStats", "maxStats", "statCaps", "stats" };
        var fields = "";
        var code = "var ptr RuleSoldier soldier; var ptr RuleSoldier absentSoldier null; var ptr RuleSoldier same; " +
            "var ptr RuleArmor armor; var ptr RuleArmor absentArmor null; var int value; rules.getRuleSoldier soldier \"PRICE_SOLDIER\"; " +
            "rules.getRuleSoldier same \"PRICE_SOLDIER\"; if neq same soldier; div cost_current 0; end; " +
            "rules.getRuleArmor armor \"PRICE_STAT_ARMOR\"; soldier.getTag value Tag.PRICE_SOLDIER_TAG; " +
            "if neq value -7; div cost_current 0; end; absentSoldier.getTag value Tag.PRICE_SOLDIER_TAG; if neq value 0; div cost_current 0; end; ";
        for (var group = 0; group < rows.Length; group++)
        {
            Assert.Equal(12, rows[group].EnumerateObject().Count());
            fields += $"    {yaml[group]}: {{" + string.Join(", ", rows[group].EnumerateObject().Select(field =>
                $"{StatField(field.Name[(field.Name.IndexOf('.') + 1)..])}: {field.Value.GetInt32()}")) + "}\n";
            foreach (var field in rows[group].EnumerateObject())
                code += $"{(group == 3 ? "armor" : "soldier")}.{field.Name} value; if neq value {field.Value.GetInt32()}; div cost_current 0; end; " +
                    $"{(group == 3 ? "absentArmor" : "absentSoldier")}.{field.Name} value; if neq value 0; div cost_current 0; end; ";
        }
        var armorField = fields[fields.LastIndexOf("    stats:", StringComparison.Ordinal)..];
        AssertPrice(Load(global, code + "return 7;", extraRules:
            "soldiers:\n  - type: PRICE_SOLDIER\n    armor: ARMOR\n    tags: {PRICE_SOLDIER_TAG: -7}\n" + fields[..^armorField.Length] +
            "armors:\n  - type: PRICE_STAT_ARMOR\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n" + armorField,
            extraExtended: "tags:\n  RuleSoldier: {PRICE_SOLDIER_TAG: int}"));
    }

    private static string StatField(string name) => name switch
    {
        "getTimeUnits" => "tu",
        "getManaPool" => "mana",
        _ => char.ToLowerInvariant(name[3]) + name[4..],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitLookupsPreserveNativeEmptyMissingNoneAndNullRules(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var code = "var ptr RuleUnit unit; var ptr RuleMod absent null; var text id; ";
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "unitLookup"))
        {
            var name = row.GetProperty("name").GetString();
            if (name == "NUL") continue; // A raw NUL is not a script text literal.
            var receiver = row.GetProperty("source").GetBoolean() ? "rules" : "absent";
            code += $"{receiver}.getRuleUnit unit \"{name}\"; unit.getType id; " +
                $"if neq id \"{row.GetProperty("type").GetString()}\"; div cost_current 0; end; " +
                $"if {(row.GetProperty("found").GetBoolean() ? "eq" : "neq")} unit null; div cost_current 0; end; ";
        }
        AssertPrice(Load(global, code + "return 7;", extraRules:
            "units: [{type: UNIT_METADATA, armor: ARMOR}, {type: STR_NONE, armor: ARMOR}]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitLinksPreserveNativeRecoveryAndCanonicalItemIdentity(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "unitLinks"))
        {
            var id = row.GetProperty("type").GetString()!;
            var fields = row.GetProperty("case").GetString() switch
            {
                "default" or "missing-default" => "",
                "null" => "liveAlien: null\ncivilianRecoveryType: null",
                "empty" => "liveAlien: \"\"",
                "none" => "liveAlien: STR_NONE",
                "explicit" => "liveAlien: LIVE_ITEM\ncivilianRecoveryType: RECOVERY\nspawnUnit: UNIT_METADATA",
                "literal" => "liveAlien: STR_NULL\ncivilianRecoveryType: STR_NULL",
                "engineer" => "liveAlien: \"\"\ncivilianRecoveryType: STR_ENGINEER",
                "scientist" => "liveAlien: \"\"\ncivilianRecoveryType: STR_SCIENTIST",
                "soldier" => "liveAlien: \"\"\ncivilianRecoveryType: COLLISION",
                _ => throw new InvalidOperationException("Unknown native Unit linkage case."),
            };
            var code = "var ptr RuleUnit unit; var ptr RuleUnit absent null; var ptr RuleUnit spawn; var ptr RuleItem item; " +
                $"var ptr RuleItem same; var text name; rules.getRuleUnit unit \"{id}\"; unit.getSpawnUnit spawn; spawn.getType name; " +
                $"if neq name \"{row.GetProperty("spawn").GetString()}\"; div cost_current 0; end; ";
            foreach (var (method, key) in new[] { ("getLiveAlienGeoscape", "live"), ("getCivilianRecoveryItemType", "recovery") })
                code += $"unit.{method} item; item.getType name; if neq name \"{row.GetProperty(key).GetString()}\"; div cost_current 0; end; " +
                    "rules.getRuleItem same name; if neq same item; div cost_current 0; end; " +
                    $"absent.{method} item; if neq item null; div cost_current 0; end; ";
            code += "absent.getSpawnUnit spawn; if neq spawn null; div cost_current 0; end; return 7;";
            AssertPrice(Load(global, code, extraRules:
                $"units:\n  - type: {id}\n    armor: ARMOR\n" + Indent(fields, 4) + "soldiers: [{type: COLLISION, armor: ARMOR}]",
                extraItems: "- {type: UNIT_METADATA}\n- {type: LIVE_ITEM}\n- {type: RECOVERY}\n- {type: STR_NULL}\n- {type: COLLISION}"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmorAndSkillLookupsRetainNativeMissingAndNullRules(bool global)
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
        AssertPrice(Load(global, code + "return 7;", extraRules:
            "armors: [{type: PRICE_LOOKUP, corpseBattle: [CORPSE], corpseGeo: CORPSE, loftempsSet: [0]}]\nskills: [{type: PRICE_LOOKUP}]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmorVisionAndSkillIdentityRetainNativeReadersAndTags(bool global)
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
            AssertPrice(Load(global, code + "return 7;", extraRules:
                "armors:\n  - type: PRICE_ARMOR\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n" +
                "    tags: {PRICE_ARMOR_TAG: -7}\n" + fields + $"\nskills: [{{type: {skillId}, tags: {{PRICE_SKILL_TAG: 31}}}}]",
                extraExtended: "tags:\n  RuleArmor: {PRICE_ARMOR_TAG: int}\n  RuleSkill: {PRICE_SKILL_TAG: int}"));
        }
    }
}
