using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionConsumedFieldsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullAndUnreachableQueriesDoNotConsumeLiveStatistics(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "nullMemberOutputs"))
            Assert.All(row.EnumerateArray(), static value => Assert.Equal(0, value.GetInt32()));
        foreach (var scenario in new[] { "null", "scalar", "text" })
        {
            var receiver = fromCraft ? "craft" : "ufo";
            var type = fromCraft ? "Craft" : "Ufo";
            var code = scenario switch
            {
                "scalar" => $"var int flag 0; var int value; if neq flag 0; {receiver}.getDamagePercentage value; {receiver}.Stats.getArmor value; end; ",
                "text" => $"var text marker \"UNUSED\"; var int value; if eq marker \"LIVE\"; {receiver}.getDamagePercentage value; {receiver}.Stats.getArmor value; end; ",
                _ => $"var ptr {type} absent null; var int value 73; absent.getDamagePercentage value; if neq value 0; return DETECTION_NONE 0; end; absent.Stats.getArmor value; if neq value 0; return DETECTION_NONE 0; end; ",
            };
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                crafts:
                  - type: CRAFT_UNUSED_FIELDS
                    speedMax: 6000
                    fuelMax: 100
                    damageMax: 100
                    radarRange: 10000
                    radarChance: 100
                    armor: 2147483647
                    weapons: 1
                craftWeapons:
                  - type: UNUSED_ARMOR_BONUS
                    ammoMax: 1
                    launcher: SUPPLY
                    stats: {armor: 1}
                ufos:
                  - type: UFO_UNUSED_FIELDS
                    speedMax: 2200
                    damageMax: 0
                    armor: 2147483647
                    raceBonus:
                      RACE_A: {armor: 1}
                    scripts:
                      {{(fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase")}}: '{{code}}return DETECTION_RADAR 100;'
                """);
            var snapshot = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 29, 55), "UFO_UNUSED_FIELDS", 2200).Capture();
            if (fromCraft) snapshot = snapshot.WithCraft("SHIP", craft => craft with
            {
                RuleId = "CRAFT_UNUSED_FIELDS",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Speed = 0,
                    Destination = null,
                    Damage = 0,
                    Weapons = [new CraftWeaponSnapshot("UNUSED_ARMOR_BONUS", 1)],
                },
            });
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(219));
            campaign = Reload(content, campaign, 219, "unused-fields-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected, scenario);
            Reload(content, campaign, 219, "unused-fields-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleLiveReceiverStillRequiresItsDamageDenominator(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            ufos:
              - type: UFO_NULLABLE_FIELDS
                speedMax: 2200
                damageMax: 0
                scripts:
                  detectUfoFromBase: |
                    var ptr Ufo source null;
                    if eq already_tracked 0; set source ufo; end;
                    source.getDamagePercentage detection_chance;
                    return;
            """);
        var campaign = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 29, 55), "UFO_NULLABLE_FIELDS", 2200);
        AssertTimeBlocked(campaign, "Detection UFO damage percentage exceeds the supported range.");
    }
}
