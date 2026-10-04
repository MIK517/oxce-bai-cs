using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Globals;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionTagTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ItemAndArmorRuleTagsRetainTypedOwnersAndReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "tagValues");
        var itemValue = rows[2][1].GetInt32();
        var armorValue = rows[3][1].GetInt32();
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            extended:
              tags:
                RuleItem: {DETECT_ITEM: int, DETECT_ITEM_ABSENT: int}
                RuleArmor: {DETECT_ARMOR: int}
            items:
              - type: ITEM_DETECT_TAG
                tags: {DETECT_ITEM: {{itemValue}}}
              - type: ITEM_DETECT_EMPTY
            armors:
              - type: ARMOR_DETECT_TAG
                corpseBattle: [CORPSE]
                corpseGeo: CORPSE
                loftempsSet: [0]
                tags: {DETECT_ARMOR: {{armorValue}}}
            ufos:
              - type: UFO_ITEM_ARMOR_TAGS
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleItem item;
                    var ptr RuleItem absentItem null;
                    var ptr RuleArmor armor;
                    var ptr RuleArmor absentArmor null;
                    var int value;
                    rules.getRuleItem item "ITEM_DETECT_TAG";
                    item.getTag value Tag.DETECT_ITEM;
                    if neq value {{itemValue}}; return DETECTION_NONE 0; end;
                    item.getTag value Tag.DETECT_ITEM_ABSENT;
                    if neq value 0; return DETECTION_NONE 0; end;
                    absentItem.getTag value Tag.DETECT_ITEM;
                    if neq value 0; return DETECTION_NONE 0; end;
                    rules.getRuleItem item "ITEM_DETECT_EMPTY";
                    item.getTag value Tag.DETECT_ITEM;
                    if neq value 0; return DETECTION_NONE 0; end;
                    rules.getRuleArmor armor "ARMOR_DETECT_TAG";
                    armor.getTag value Tag.DETECT_ARMOR;
                    if neq value {{armorValue}}; return DETECTION_NONE 0; end;
                    absentArmor.getTag value Tag.DETECT_ARMOR;
                    if neq value 0; return DETECTION_NONE 0; end;
                    return DETECTION_RADAR 100;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_ITEM_ARMOR_TAGS", 2200);
        campaign = Reload(content, campaign, 197, "item-armor-tags-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 197, "item-armor-tags-after.sav");
    }

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UfoTagReadsShareTheHalfHourWorkLimit(bool fromCache, bool loop)
    {
        var tags = string.Join(", ", Enumerable.Range(0, 2048).Select(index => $"DETECT_{index}: int"));
        var code = loop ? "loop var i 512; ufo.getTag detection_chance Tag.DETECT_2047; end; return;"
            : string.Concat(Enumerable.Repeat("ufo.getTag detection_chance Tag.DETECT_2047; ", 512)) + "return;";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            extended:
              tags:
                Ufo: { {{tags}} }
            ufos:
              - type: UFO_TAG_WORK
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: "{{code}}"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_TAG_WORK", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with
                {
                    ScriptValues = Enumerable.Range(0, 2048).Select(index => new ScriptValueEntry($"DETECT_{index}", "int", 1)).ToArray(),
                }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(107));

        AssertTimeBlocked(campaign, "Detection script work exceeds the supported half-hour limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftTagsSurviveOrderedTransferAndHourlyArrival(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"], fromCache);
        var snapshot = CreateSnapshot(content, "Craft tag transfer").WithCraft("SHIP", craft => craft with
        {
            ScriptValues = [new("DETECT_CRAFT", "int", 37)],
            Logistics = craft.Logistics! with { Status = "STR_READY", Fuel = 100 },
        });
        var source = snapshot.Bases[0] with
        {
            Crafts = [snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP")],
            Soldiers = [],
        };
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 0, 59, 55),
            Bases = [source, source with { Id = 2, Name = "Beta", Crafts = [], Soldiers = [], Transfers = [] }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(108));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(source.Id, LogisticsOperation.Transfer, 2)).Events)).Quote;
        var row = Assert.Single(quote.Rows, item => item.Kind == CampaignTransferKind.Craft);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(row.Id, 1)])).Events));
        snapshot = campaign.Capture();
        Assert.Equal(37, Assert.Single(Assert.Single(snapshot.Bases[1].Transfers).Craft!.ScriptValues).Value);
        snapshot = snapshot with
        {
            Bases = [snapshot.Bases[0], snapshot.Bases[1] with
            {
                Transfers = snapshot.Bases[1].Transfers.Select(item => item with { Hours = 1 }).ToArray(),
            }],
        };
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(108));
        campaign = Reload(content, campaign, 108, "craft-tag-transfer.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.Empty(campaign.Capture().Bases[1].Transfers);
        Assert.Equal(37, Assert.Single(Assert.Single(campaign.Capture().Bases[1].Crafts).ScriptValues).Value);
        Reload(content, campaign, 108, "craft-tag-arrival.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedCraftTagsDoNotResurrectDuringOpaqueFieldRewrites(bool logistics)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"]);
        var snapshot = CreateSnapshot(content, "Craft tag rewrite").WithCraft("SHIP", craft => craft with
        {
            ScriptValues = [new("DETECT_CRAFT", "int", 37)],
            Logistics = logistics ? craft.Logistics : null,
        });
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        var line = yaml.Split('\n').Single(item => item.Contains("type: SHIP", StringComparison.Ordinal));
        yaml = yaml.Replace(line, line + "\n" + new string(' ', line.IndexOf("type:", StringComparison.Ordinal)) +
            "auditOpaque: retained", StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 109, name: "craft-tag-opaque.sav");
        snapshot = loaded.Campaign.Capture().WithCraft("SHIP", craft => craft with { ScriptValues = [] });

        for (var cycle = 0; cycle < 3; cycle++)
        {
            yaml = OxceSaveAdapter.EmitLoadedCampaign(snapshot, loaded.Source);
            loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 109, name: "craft-tag-cleared.sav");
            snapshot = loaded.Campaign.Capture();
            Assert.Contains("auditOpaque: retained", yaml, StringComparison.Ordinal);
            Assert.DoesNotContain("DETECT_CRAFT:", yaml, StringComparison.Ordinal);
            Assert.Empty(snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").ScriptValues);
        }
    }

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}
