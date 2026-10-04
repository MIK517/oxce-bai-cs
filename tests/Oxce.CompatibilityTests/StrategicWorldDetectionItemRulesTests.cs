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
                         ("isAreaOfEffect", 5), ("getArmorIgnore", 6) })
            {
                code += $"damage.{reader} value; if neq value {row[index].GetInt32()}; return DETECTION_NONE 0; end; " +
                    $"missing.{reader} value; if neq value 0; return DETECTION_NONE 0; end; ";
            }
            code += $"item.isSingleTarget value; if neq value {row[8].GetInt32()}; return DETECTION_NONE 0; end; " +
                "absent.isSingleTarget value; if neq value 0; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            RunItemScript(code, fields, fromCache, fromCraft);
        }
    }

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

    private static void RunItemScript(string code, string fields, bool fromCache, bool fromCraft, string extra = "")
    {
        var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            {{extra}}
            items:
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
