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
