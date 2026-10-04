using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Globals;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionTagTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UfoInstanceTagAndAbsentTagExecuteAcrossReloads(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var value = TestFixtures.Rows(oracle.RootElement, "tagValues")[2][1].GetInt32();
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"], fromCache);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_TAGS", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with { ScriptValues = [new("DETECT_UFO", "int", value)] }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(101));
        campaign = Reload(content, campaign, 101, "ufo-detection-tags-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).HyperDetected);
        Reload(content, campaign, 101, "ufo-detection-tags-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftInstanceTagsExecuteAndPersist(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"], fromCache);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_CRAFT_TAGS", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                ScriptValues = [new("DETECT_CRAFT", "int", 37)],
                Logistics = craft.Logistics! with { Status = "STR_OUT", Destination = null, Speed = 0 },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(102));
        campaign = Reload(content, campaign, 102, "craft-detection-tags-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 102, "craft-detection-tags-after.sav");
    }

    [Theory]
    [InlineData("unknown", "int")]
    [InlineData("DETECT_CRAFT", "text")]
    public void InvalidCraftTagStateFailsIntentionally(string name, string type)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"]);
        var snapshot = CreateSnapshot(content, "Invalid craft tag").WithCraft("SHIP",
            craft => craft with { ScriptValues = [new(name, type, 1)] });

        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(103)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RuleAndGlobalTagsKeepOwnerIdentityAcrossCacheAndReload(bool fromCache, bool craftOnly)
    {
        var extraRules = """
            extended:
              tags:
                Ufo: {DETECT_UFO: int, DETECT_UFO_MISSING: int}
                Craft: {DETECT_CRAFT: int}
                RuleUfo: {DETECT_RULE_UFO: int}
                RuleCraft: {DETECT_RULE_CRAFT: int}
                RuleMod: {DETECT_GLOBAL: int, DETECT_RULE_LIST: RuleList}
                GeoscapeGame: {DETECT_GAME: int}
              globals: {DETECT_GLOBAL: 5, DETECT_RULE_LIST: logistics}
            crafts:
              - type: SHIP
                tags: {DETECT_RULE_CRAFT: 13}
            ufos:
              - type: UFO_RULE_GLOBAL_TAGS
                speedMax: 2200
                damageMax: 50
                tags: {DETECT_RULE_UFO: 9}
                scripts:
                  detectUfoFromBase: |
                    var int value;
                    var ptr RuleUfo rule;
                    var ptr RuleUfo absent null;
                    ufo.getTag value Tag.DETECT_RULE_UFO;
                    if neq value 9; return DETECTION_NONE 0; end;
                    ufo.getRules rule;
                    rule.getTag value Tag.DETECT_RULE_UFO;
                    if neq value 9; return DETECTION_NONE 0; end;
                    absent.getTag value Tag.DETECT_RULE_UFO;
                    if neq value 0; return DETECTION_NONE 0; end;
                    rules.getTag value Tag.DETECT_GLOBAL;
                    if neq value 5; return DETECTION_NONE 0; end;
                    rules.getTag value Tag.DETECT_RULE_LIST;
                    if neq value RuleList.master; return DETECTION_NONE 0; end;
                    geoscape_game.getTag value Tag.DETECT_GAME;
                    if neq value 23; return DETECTION_NONE 0; end;
                    ufo.getTag value Tag.DETECT_UFO;
                    if neq value -7; return DETECTION_NONE 0; end;
                    return DETECTION_HYPERWAVE 100;
                  detectUfoFromCraft: |
                    var int value;
                    var ptr RuleCraft rule;
                    craft.getTag value Tag.DETECT_RULE_CRAFT;
                    if neq value 13; return DETECTION_NONE 0; end;
                    craft.getRuleCraft rule;
                    rule.getTag value Tag.DETECT_RULE_CRAFT;
                    if neq value 13; return DETECTION_NONE 0; end;
                    craft.getTag value Tag.DETECT_CRAFT;
                    if neq value 37; return DETECTION_NONE 0; end;
                    return DETECTION_RADAR 100;
            """;
        if (craftOnly) extraRules = extraRules.Replace("return DETECTION_HYPERWAVE 100;", "return DETECTION_NONE 0;", StringComparison.Ordinal);
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"], fromCache, extraRules);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_RULE_GLOBAL_TAGS", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                ScriptValues = [new("DETECT_CRAFT", "int", 37)],
                Logistics = craft.Logistics! with { Status = "STR_OUT", Destination = null, Speed = 0 },
            });
        snapshot = snapshot with
        {
            ScriptValues = [new("DETECT_GAME", "int", 23)],
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with { ScriptValues = [new("DETECT_UFO", "int", -7)] }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(104));
        campaign = Reload(content, campaign, 104, "rule-global-detection-tags.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.Equal(!craftOnly, Assert.Single(campaign.Capture().World.Ufos).HyperDetected);
        Reload(content, campaign, 104, "rule-global-detection-tags-after.sav");
    }

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}
