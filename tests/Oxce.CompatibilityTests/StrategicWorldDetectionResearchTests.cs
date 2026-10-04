using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchDetectionMatchesReferenceAndIgnoresDebugMode(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var content = Load(fromCache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "research"))
        {
            var completed = row[0].GetInt32() != 0;
            var debug = row[1].GetInt32() != 0;
            var knownMod = row[2].GetInt32() != 0;
            var snapshot = CreateUfoTransitCampaign(content, HalfHour, knownMod ? "UFO_RESEARCH" : "UFO_RESEARCH_NULL", 2200).Capture();
            snapshot = snapshot with { CompletedResearch = completed ? ["R_DETECT"] : [], DebugMode = debug };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(105));

            AdvanceUnblocked(campaign, 1);

            Assert.Equal(row[3].GetInt32() != 0, Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 105, "research-detection.sav");
        }
        Assert.Equal(0, oracle.RootElement.GetProperty("missingResearch").GetInt32());
        Assert.Equal([0, 0, 1], oracle.RootElement.GetProperty("emptyResearch").EnumerateArray()
            .Select(item => item.GetInt32()).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectionReadsSameTickDailyResearchAndReloadsItsResult(bool fromCache)
    {
        var content = Load(fromCache);
        var snapshot = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 23, 59, 55), "UFO_RESEARCH", 2200).Capture()
            .WithFacility("FUEL_WORKSHOP");
        snapshot = snapshot with
        {
            CompletedResearch = [],
            Bases = [snapshot.Bases[0] with { Research = [new("R_DETECT", 1, 0, 1)] }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(106));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Contains(result.Events, item => item is CampaignResearchCompleted);
        Assert.Contains("R_DETECT", campaign.Capture().CompletedResearch);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 106, "daily-research-detection.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyRuleLookupMatchesNativeNullAndMissingCases(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "ruleLookup"))
        {
            var source = row[0].GetInt32() != 0 ? "rules" : "absent";
            var known = row[1].GetInt32() != 0;
            var calls = new[]
            {
                ("getRuleItem", "RuleItem", "SUPPLY", 2),
                ("getRuleArmor", "RuleArmor", "ARMOR", 3),
                ("getRuleSoldier", "RuleSoldier", "RECRUIT", 4),
                ("getRuleSkill", "RuleSkill", "SKILL_LOOKUP", 5),
            };
            var code = "var ptr RuleMod absent null; " + string.Concat(calls.Select((call, index) =>
                $"var ptr {call.Item2} value{index}; var ptr {call.Item2} other{index}; "));
            for (var index = 0; index < calls.Length; index++)
            {
                var (method, type, id, column) = calls[index];
                code += $"{source}.{method} value{index} \"{(known ? id : "MISSING")}\"; ";
                code += $"if {(row[column].GetInt32() != 0 ? "eq" : "neq")} value{index} null; return DETECTION_NONE 0; end; ";
                code += $"{source}.{method} other{index} \"{(known ? id : "MISSING")}\"; ";
                code += $"if neq value{index} other{index}; return DETECTION_NONE 0; end; ";
            }
            code += "rules.getRuleItem value0 \"STR_NONE\"; if neq value0 null; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                skills:
                  - type: SKILL_LOOKUP
                items:
                  - type: STR_NONE
                ufos:
                  - type: UFO_RULE_LOOKUP
                    speedMax: 2200
                    damageMax: 50
                    scripts:
                      detectUfoFromBase: '{{code}}'
                """);
            var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_RULE_LOOKUP", 2200);

            AdvanceUnblocked(campaign, 1);

            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 115, "rule-lookup-detection.sav");
        }
        Assert.Equal(0, oracle.RootElement.GetProperty("noneItem").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchTagsAndNeededItemPointersPreserveLinkedIdentity(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            extended:
              tags:
                RuleResearch: {DETECT_RESEARCH: int}
            research:
              - name: R_ITEM_TRUE
                cost: 1
                needItem: true
                neededItem: SUPPLY
                tags: {DETECT_RESEARCH: 14}
              - name: R_ITEM_FALSE
                cost: 1
                needItem: false
                neededItem: SUPPLY
              - name: SUPPLY
                cost: 1
                needItem: true
              - name: R_ITEM_MISSING
                cost: 1
                needItem: true
            ufos:
              - type: UFO_RESEARCH_ITEM
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleResearch topic;
                    var ptr RuleResearch absent null;
                    var ptr RuleItem item;
                    var ptr RuleItem wanted;
                    var int value;
                    rules.getRuleItem wanted "SUPPLY";
                    rules.getRuleResearch topic "R_ITEM_TRUE";
                    topic.getNeededItem item;
                    if neq item wanted; return DETECTION_NONE 0; end;
                    topic.getTag value Tag.DETECT_RESEARCH;
                    if neq value 14; return DETECTION_NONE 0; end;
                    absent.getTag value Tag.DETECT_RESEARCH;
                    if neq value 0; return DETECTION_NONE 0; end;
                    rules.getRuleResearch topic "R_ITEM_FALSE";
                    topic.getNeededItem item;
                    if neq item null; return DETECTION_NONE 0; end;
                    topic.getTag value Tag.DETECT_RESEARCH;
                    if neq value 0; return DETECTION_NONE 0; end;
                    rules.getRuleResearch topic "SUPPLY";
                    topic.getNeededItem item;
                    if neq item wanted; return DETECTION_NONE 0; end;
                    rules.getRuleResearch topic "R_ITEM_MISSING";
                    topic.getNeededItem item;
                    if neq item null; return DETECTION_NONE 0; end;
                    absent.getNeededItem item;
                    if neq item null; return DETECTION_NONE 0; end;
                    return DETECTION_RADAR 100;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_RESEARCH_ITEM", 2200);

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 116, "research-item-detection.sav");
    }

    private static RuntimeContent Load(bool fromCache) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            research:
              - name: R_DETECT
                cost: 1
                points: 7
                lookup: R_LOOKUP
              - name: R_LOOKUP
                cost: 0
              - name: '0'
                cost: 3
            ufos:
              - type: UFO_RESEARCH
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleResearch research;
                    var ptr RuleResearch other;
                    var ptr RuleMod absent null;
                    var int value;
                    rules.getRuleResearch research "R_DETECT";
                    rules.getRuleResearch other "R_DETECT";
                    if neq research other; return DETECTION_NONE 0; end;
                    research.getCost value;
                    if neq value 1; return DETECTION_NONE 0; end;
                    research.getPoints value;
                    if neq value 7; return DETECTION_NONE 0; end;
                    research.getLookup other;
                    if eq other null; return DETECTION_NONE 0; end;
                    rules.getRuleResearch other "MISSING";
                    if neq other null; return DETECTION_NONE 0; end;
                    rules.getRuleResearch other "";
                    if neq other null; return DETECTION_NONE 0; end;
                    rules.getRuleResearch other "0";
                    if eq other null; return DETECTION_NONE 0; end;
                    other.getCost value;
                    if neq value 3; return DETECTION_NONE 0; end;
                    absent.getRuleResearch other "R_DETECT";
                    if neq other null; return DETECTION_NONE 0; end;
                    geoscape_game.isResearched value research;
                    if eq value 1; return DETECTION_RADAR 100; end;
                    return DETECTION_NONE 0;
              - type: UFO_RESEARCH_NULL
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleResearch research;
                    var ptr RuleMod absent null;
                    var int value;
                    absent.getRuleResearch research "R_DETECT";
                    geoscape_game.isResearched value research;
                    if eq value 1; return DETECTION_RADAR 100; end;
                    return DETECTION_NONE 0;
            """);

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}
