using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedItemProvidersRemainGuardedBeforeMutation(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            ufos:
              - type: UFO_ITEM_PROVIDER_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleItem item;
                    rules.getRuleItem item "SUPPLY";
                    item.getPower detection_chance;
                    return;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_ITEM_PROVIDER_GUARD", 2200);
        AssertTimeBlocked(campaign, "Detection script requires unsupported binding RuleItem.getPower.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuleMetadataReadsPreserveTextAndScalarDefaultsAcrossReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var types = TestFixtures.Rows(oracle.RootElement, "ruleMetadata").Select(value => value.GetString()!).ToArray();
        var scalars = TestFixtures.Rows(oracle.RootElement, "ruleScalars").Select(value => value.GetInt32()).ToArray();
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            items:
              - type: {{types[0]}}
                weight: {{scalars[0]}}
                battleType: {{scalars[1]}}
                clipSize: -1
            armors:
              - type: {{types[1]}}
                size: {{scalars[2]}}
                corpseBattle: [CORPSE, CORPSE, CORPSE, CORPSE]
                corpseGeo: CORPSE
                loftempsSet: [0, 0, 0, 0]
            skills:
              - type: {{types[2]}}
            crafts:
              - type: {{types[3]}}
                speedMax: 6000
                damageMax: 100
                fuelMax: 100
            ufos:
              - type: UFO_RULE_METADATA
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromCraft: |
                    var ptr RuleItem item;
                    var ptr RuleItem absentItem null;
                    var ptr RuleArmor armor;
                    var ptr RuleArmor absentArmor null;
                    var ptr RuleSkill skill;
                    var ptr RuleSkill absentSkill null;
                    var ptr RuleCraft craftRule;
                    var ptr RuleCraft absentCraft null;
                    var text typeName;
                    var int value;
                    rules.getRuleItem item "{{types[0]}}";
                    item.getType typeName;
                    if neq typeName "{{types[0]}}"; return DETECTION_NONE 0; end;
                    item.getWeight value;
                    if neq value {{scalars[0]}}; return DETECTION_NONE 0; end;
                    item.getBattleType value;
                    if neq value {{scalars[1]}}; return DETECTION_NONE 0; end;
                    absentItem.getWeight value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    absentItem.getBattleType value;
                    if neq value {{oracle.RootElement.GetProperty("nullBattleType").GetInt32()}}; return DETECTION_NONE 0; end;
                    absentItem.getType typeName;
                    if neq typeName "{{types[4]}}"; return DETECTION_NONE 0; end;
                    rules.getRuleArmor armor "{{types[1]}}";
                    armor.getType typeName;
                    if neq typeName "{{types[1]}}"; return DETECTION_NONE 0; end;
                    armor.getSize value;
                    if neq value {{scalars[2]}}; return DETECTION_NONE 0; end;
                    absentArmor.getSize value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    absentArmor.getType typeName;
                    if neq typeName "{{types[5]}}"; return DETECTION_NONE 0; end;
                    rules.getRuleSkill skill "{{types[2]}}";
                    skill.getType typeName;
                    if neq typeName "{{types[2]}}"; return DETECTION_NONE 0; end;
                    absentSkill.getType typeName;
                    if neq typeName "{{types[6]}}"; return DETECTION_NONE 0; end;
                    craft.getRuleCraft craftRule;
                    craftRule.getType typeName;
                    if neq typeName "{{types[3]}}"; return DETECTION_NONE 0; end;
                    absentCraft.getType typeName;
                    if neq typeName "{{types[7]}}"; return DETECTION_NONE 0; end;
                    return DETECTION_RADAR 100;
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_RULE_METADATA", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                RuleId = types[3],
                Logistics = craft.Logistics! with { Status = "STR_OUT", Destination = null, Speed = 0, Weapons = [] },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(199));
        campaign = Reload(content, campaign, 199, "rule-metadata-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 199, "rule-metadata-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCraftRuleStatUsesNativeRawFieldsAndReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var native = oracle.RootElement.GetProperty("craftStats");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Stats.getFuelMax"] = "fuelMax",
            ["Stats.getDamageMax"] = "damageMax",
            ["Stats.getSpeedMax"] = "speedMax",
            ["Stats.getAccel"] = "accel",
            ["Stats.getRadarRange"] = "radarRange",
            ["Stats.getRadarChance"] = "radarChance",
            ["Stats.getSightRange"] = "sightRange",
            ["Stats.getHitBonus"] = "hitBonus",
            ["Stats.getAvoidBonus"] = "avoidBonus",
            ["Stats.getAvoidBonus2"] = "avoidBonus2",
            ["Stats.getPowerBonus"] = "powerBonus",
            ["Stats.getArmor"] = "armor",
            ["Stats.getShieldCapacity"] = "shieldCapacity",
            ["Stats.getShieldRecharge"] = "shieldRecharge",
            ["Stats.getShieldRechargeInGeoscape"] = "shieldRechargeInGeoscape",
            ["Stats.getShieldBleedThrough"] = "shieldBleedThrough",
            ["Stats.getMaxUnits"] = "soldiers",
            ["Stats.getMaxVehiclesAndLargeSoldiers"] = "vehicles",
            ["Stats.getMaxItems"] = "maxItems",
        };
        Assert.Equal(19, native.EnumerateObject().Count());
        var fields = string.Join("\n", keys.Select(pair => $"    {pair.Value}: {native.GetProperty(pair.Key).GetInt32()}"));
        var code = "var int value; var ptr RuleCraft rule; var ptr RuleCraft absent null; craft.getRuleCraft rule; ";
        foreach (var pair in keys)
        {
            code += $"rule.{pair.Key} value; if neq value {native.GetProperty(pair.Key).GetInt32()}; return DETECTION_NONE 0; end; ";
            code += $"absent.{pair.Key} value; if neq value 0; return DETECTION_NONE 0; end; ";
        }
        code += "return DETECTION_RADAR 100;";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            crafts:
              - type: CRAFT_RULE_STATS
            {{fields}}
                weapons: 2
                maxUnitsLimit: 99
                maxHWPUnitsLimit: 98
            ufos:
              - type: UFO_CRAFT_RULE_STATS
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromCraft: "{{code}}"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_CRAFT_RULE_STATS", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                RuleId = "CRAFT_RULE_STATS",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Speed = 0,
                    Destination = null,
                    Weapons = [new CraftWeaponSnapshot("RADAR_BOOST", 1)],
                },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(196));
        campaign = Reload(content, campaign, 196, "craft-rule-stats-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 196, "craft-rule-stats-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModDetectionConstantsPreserveDefaultsOverridesAndReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "modSettings");
        for (var index = 0; index < rows.Length; index++)
        {
            var values = rows[index].EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var overrides = index == 0 ? "" : $"maxViewDistance: {values[0]}\nmaxDarknessToSeeUnits: {values[1]}";
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                {{overrides}}
                ufos:
                  - type: UFO_MOD_READS
                    speedMax: 2200
                    damageMax: 50
                    scripts:
                      detectUfoFromBase: |
                        var int value;
                        var ptr RuleMod absent null;
                        absent.getMaxViewDistance value;
                        if neq value 0; return DETECTION_NONE 0; end;
                        absent.getMaxDarknessToSeeUnits value;
                        if neq value 0; return DETECTION_NONE 0; end;
                        rules.getMaxViewDistance value;
                        if neq value {{values[0]}}; return DETECTION_NONE 0; end;
                        rules.getMaxDarknessToSeeUnits value;
                        if neq value {{values[1]}}; return DETECTION_NONE 0; end;
                        return DETECTION_RADAR 100;
                """);
            Assert.Equal(values[0], content.RuntimeRules.Campaign.MaximumViewDistance);
            Assert.Equal(values[1], content.RuntimeRules.Campaign.MaximumDarknessToSeeUnits);
            var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_MOD_READS", 2200);
            campaign = Reload(content, campaign, 195, "mod-settings-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 195, "mod-settings-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UfoRuleGettersMatchNativeDefaultsAndReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var sizes = new[] { "STR_VERY_SMALL", "STR_SMALL", "STR_MEDIUM_UC", "STR_LARGE", "STR_VERY_LARGE", "UNKNOWN" };
        var weapons = oracle.RootElement.GetProperty("ufoWeapons").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "ufoRules"))
        {
            var values = row.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                ufos:
                  - type: UFO_RULE_READS
                    size: {{sizes[values[0]]}}
                    radius: {{values[1]}}
                    speedMax: 2200
                    damageMax: 50
                    range: {{weapons[0]}}
                    power: {{weapons[1]}}
                    reload: {{weapons[2]}}
                    scripts:
                      detectUfoFromBase: |
                        var ptr RuleUfo rule;
                        var ptr RuleUfo absent null;
                        var int value;
                        absent.getRadius value;
                        if neq value 0; return DETECTION_NONE 0; end;
                        absent.getWeaponPower value;
                        if neq value 0; return DETECTION_NONE 0; end;
                        ufo.getRuleUfo rule;
                        rule.getRadius value;
                        if neq value {{values[2]}}; return DETECTION_NONE 0; end;
                        rule.getWeaponRange value;
                        if neq value {{weapons[0]}}; return DETECTION_NONE 0; end;
                        rule.getWeaponPower value;
                        if neq value {{weapons[1]}}; return DETECTION_NONE 0; end;
                        rule.getWeaponReload value;
                        if neq value {{weapons[2]}}; return DETECTION_NONE 0; end;
                        return DETECTION_RADAR 100;
                """);
            var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_RULE_READS", 2200);
            campaign = Reload(content, campaign, 194, "ufo-rule-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 194, "ufo-rule-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UfoReadBindingsMatchReferenceAndReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var values = TestFixtures.Rows(oracle.RootElement, "bindings")[0].EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            ufos:
              - type: UFO_READ_BINDINGS
                speedMax: 2200
                damageMax: 50
                shieldCapacity: 10
                scripts:
                  detectUfoFromBase: |
                    var int value;
                    set detection_chance 0;
                    ufo.getDamage value;
                    if neq value {{values[0]}}; return; end;
                    ufo.getShield value;
                    if neq value {{values[1]}}; return; end;
                    ufo.getShieldPercentage value;
                    if neq value {{values[2]}}; return; end;
                    ufo.getDetected value;
                    if neq value {{values[3]}}; return; end;
                    ufo.getHyperDetected value;
                    if neq value {{values[4]}}; return; end;
                    ufo.isHunterKiller value;
                    if neq value {{values[5]}}; return; end;
                    ufo.isHunting value;
                    if neq value {{values[6]}}; return; end;
                    ufo.getHuntMode value;
                    if neq value {{values[8]}}; return; end;
                    ufo.getHuntBehavior value;
                    if neq value {{values[9]}}; return; end;
                    ufo.getAltitude value;
                    if neq value 3; return; end;
                    ufo.getStatus value;
                    if neq value UFO_FLYING; return; end;
                    set detection_type DETECTION_HYPERWAVE;
                    set detection_chance 100;
                    return;
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_READ_BINDINGS", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with
                {
                    Damage = values[0], Shield = values[1], Detected = true, HunterKiller = true,
                    HuntMode = values[8], HuntBehavior = values[9],
                }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(93));
        campaign = Reload(content, campaign, 93, "detection-read-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).HyperDetected);
        Reload(content, campaign, 93, "detection-read-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShieldProvidersIncludeMissionRaceBonus(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            ufos:
              - type: UFO_READ_RACE
                speedMax: 2200
                damageMax: 50
                shieldCapacity: 10
                raceBonus:
                  RACE_B: {shieldCapacity: 5}
                scripts:
                  detectUfoFromBase: |
                    var int maximum;
                    var int percentage;
                    ufo.getShieldMax maximum;
                    ufo.getShieldPercentage percentage;
                    if eq maximum 15;
                      if eq percentage 20;
                        return DETECTION_RADAR 100;
                      end;
                    end;
                    return DETECTION_NONE 0;
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_READ_RACE", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }],
                Ufos = [snapshot.World.Ufos[0] with { Shield = 3 }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(94));

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameTimeAndNullReceiverProvidersUseCurrentTick(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            ufos:
              - type: UFO_READ_CLOCK
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var int value;
                    var ptr Time time;
                    var ptr Ufo absent null;
                    absent.getDamage value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    geoscape_game.getTime time;
                    time.getMinute value;
                    if neq value 30; return DETECTION_NONE 0; end;
                    time.getSecond value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    time.getSecondsPastMidnight value;
                    if neq value 5400; return DETECTION_NONE 0; end;
                    geoscape_game.difficultyLevel value;
                    if neq value 2; return DETECTION_NONE 0; end;
                    geoscape_game.getMonthsPassed value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    return DETECTION_RADAR 100;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_READ_CLOCK", 2200);

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftProviderReadsInstalledRadarAndRuleCapacity(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            crafts:
              - type: SHIP
                soldiers: 4
                maxUnitsLimit: 8
            ufos:
              - type: UFO_READ_CRAFT
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromCraft: |
                    var int value;
                    var ptr RuleCraft rule;
                    craft.Stats.getRadarChance value;
                    if neq value radar_total_strength; return DETECTION_NONE 0; end;
                    craft.Stats.getRadarRange value;
                    if neq value radar_max_distance; return DETECTION_NONE 0; end;
                    craft.getDamage value;
                    if neq value 7; return DETECTION_NONE 0; end;
                    craft.getRuleCraft rule;
                    rule.getSoldiersMax value;
                    if neq value 4; return DETECTION_NONE 0; end;
                    return DETECTION_RADAR 100;
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_READ_CRAFT", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Damage = 7,
                    Speed = 0,
                    Destination = null,
                    Weapons = [new CraftWeaponSnapshot("RADAR_BOOST", 1)],
                },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(95));

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 95, "detection-craft-read.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagePercentageMatchesNativeRoundingAndMissionRaceStats(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "damage"))
        {
            var damage = row[0].GetInt32();
            var maximum = row[1].GetInt32();
            var percentage = row[2].GetInt32();
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                ufos:
                  - type: UFO_DAMAGE_BINDINGS
                    speedMax: 2200
                    damageMax: {{maximum - 5}}
                    raceBonus:
                      RACE_B: {damageMax: 5}
                    scripts:
                      detectUfoFromBase: |
                        var ptr Ufo absent null;
                        var int value;
                        absent.getDamageMax value;
                        if neq value 0; return DETECTION_NONE 0; end;
                        absent.getDamagePercentage value;
                        if neq value 0; return DETECTION_NONE 0; end;
                        ufo.getDamageMax value;
                        if neq value {{maximum}}; return DETECTION_NONE 0; end;
                        ufo.getDamagePercentage value;
                        if neq value {{percentage}}; return DETECTION_NONE 0; end;
                        return DETECTION_RADAR 100;
                """);
            var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_DAMAGE_BINDINGS", 2200).Capture();
            snapshot = snapshot with
            {
                World = snapshot.World with
                {
                    Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }],
                    Ufos = [snapshot.World.Ufos[0] with { Damage = damage }],
                },
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(111));
            campaign = Reload(content, campaign, 111, "damage-provider-before.sav");

            AdvanceUnblocked(campaign, 1);

            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 111, "damage-provider-after.sav");
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2147483647)]
    public void UndefinedDamagePercentageStopsBeforeMutation(int maximum, int damage)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            ufos:
              - type: UFO_DAMAGE_UNSAFE
                speedMax: 2200
                damageMax: {{maximum}}
                scripts:
                  detectUfoFromBase: "ufo.getDamagePercentage detection_chance; return;"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_DAMAGE_UNSAFE", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with { Ufos = [snapshot.World.Ufos[0] with { Damage = damage }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(112));

        AssertTimeBlocked(campaign, "Detection UFO damage percentage exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryUfoStatBindingMatchesRawAndRaceAdjustedNativeFields(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "stats");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getFuelMax"] = "fuelMax",
            ["getDamageMax"] = "damageMax",
            ["getSpeedMax"] = "speedMax",
            ["getAccel"] = "accel",
            ["getRadarRange"] = "radarRange",
            ["getRadarChance"] = "radarChance",
            ["getSightRange"] = "sightRange",
            ["getHitBonus"] = "hitBonus",
            ["getAvoidBonus"] = "avoidBonus",
            ["getAvoidBonus2"] = "avoidBonus2",
            ["getPowerBonus"] = "powerBonus",
            ["getArmor"] = "armor",
            ["getShieldCapacity"] = "shieldCapacity",
            ["getShieldRecharge"] = "shieldRecharge",
            ["getShieldRechargeInGeoscape"] = "shieldRechargeInGeoscape",
            ["getShieldBleedThrough"] = "shieldBleedThrough",
            ["getMaxUnits"] = "soldiers",
            ["getMaxVehiclesAndLargeSoldiers"] = "vehicles",
            ["getMaxItems"] = "maxItems",
        };
        Assert.Equal(19, rows[0].EnumerateObject().Count());
        var ruleFields = string.Join("\n", keys.Select(pair => $"    {pair.Value}: {rows[0].GetProperty(pair.Key).GetInt32()}"));
        var bonusFields = string.Join(", ", keys.Select(pair =>
            $"{pair.Value}: {rows[1].GetProperty(pair.Key).GetInt32() - rows[0].GetProperty(pair.Key).GetInt32()}"));
        var code = "var int value; var ptr RuleUfo rule; var ptr RuleUfo absent null; ufo.getRules rule; ";
        foreach (var pair in keys)
        {
            code += $"rule.{pair.Key} value; if neq value {rows[0].GetProperty(pair.Key).GetInt32()}; return DETECTION_NONE 0; end; ";
            code += $"absent.{pair.Key} value; if neq value 0; return DETECTION_NONE 0; end; ";
            code += $"ufo.Stats.{pair.Key} value; if neq value {rows[1].GetProperty(pair.Key).GetInt32()}; return DETECTION_NONE 0; end; ";
        }
        code += "return DETECTION_RADAR 100;";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            ufos:
              - type: UFO_ALL_STATS
            {{ruleFields}}
                raceBonus:
                  RACE_B: { {{bonusFields}} }
                scripts:
                  detectUfoFromBase: "{{code}}"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_ALL_STATS", 2300).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(113));
        campaign = Reload(content, campaign, 113, "ufo-stats-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 113, "ufo-stats-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueriedStatOverflowStopsBeforeMutation(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            ufos:
              - type: UFO_STAT_OVERFLOW
                speedMax: 2200
                damageMax: 50
                armor: 2147483647
                raceBonus:
                  RACE_B: {armor: 1}
                scripts:
                  detectUfoFromBase: "ufo.Stats.getArmor detection_chance; return;"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_STAT_OVERFLOW", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(114));

        AssertTimeBlocked(campaign, "Detection UFO statistics exceed the supported range.");
    }

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}
