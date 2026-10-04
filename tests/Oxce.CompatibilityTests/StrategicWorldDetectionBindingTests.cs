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

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}
