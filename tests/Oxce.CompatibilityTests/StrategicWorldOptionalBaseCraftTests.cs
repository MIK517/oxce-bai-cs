using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseCraftTests
{
    [Theory]
    [InlineData(false, "STR_READY")]
    [InlineData(true, "STR_READY")]
    [InlineData(false, "STR_REPAIRS")]
    [InlineData(true, "STR_REPAIRS")]
    [InlineData(false, "STR_REARMING")]
    [InlineData(true, "STR_REARMING")]
    [InlineData(false, "STR_REFUELLING")]
    [InlineData(true, "STR_REFUELLING")]
    public void OptionalDeletionReturnsGroundedFollowersBeforeOrderedServicing(bool cache, string status)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-cleanup");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "followerRows"));
        TestFixtures.LoadVerifiedManifest("strategic-logistics");
        var content = Content(cache);
        var deletedContent = Content(cache, "CORE");
        var survivedContent = Content(cache, "");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, status);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var craft = Assert.Single(after.Bases[0].Crafts).Logistics!;
            Assert.NotEqual("STR_OUT", craft.Status);
            Assert.False(craft.IsAutoPatrolling);
            Assert.Equal(deleted ? WorldTargetKind.Base : WorldTargetKind.AlienBase, craft.Destination!.Kind);
            Assert.Equal(deleted ? after.Bases[0].Id : 6, craft.Destination.Id);
            Assert.Equal(deleted ? 6000 : 0, craft.Speed);
            Assert.Equal(deleted ? 60 : 7, craft.Takeoff);
            Assert.Equal(initial.Bases[0].Crafts[0].Logistics!.Longitude, craft.Longitude);
            Assert.Equal(initial.Bases[0].Crafts[0].Logistics!.Latitude, craft.Latitude);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-grounded-craft.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleGroundedReturnStillValidatesConsumedEffectiveSpeed(bool cache)
    {
        var content = Content(cache);
        var initial = Snapshot(content, 17, "STR_READY").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Weapons = [new("SPEED_EXCESS", 0), null] },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft speed exceeds the supported range.");
    }

    internal static RuntimeContent Content(bool cache, string code = "BONUS", bool itemFuel = false, int escortRange = 20, int speedMaximum = 6000, int escortSpeedMaximum = 2000) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            escortRange: {{{escortRange}}}
            crafts:
              - {type: SHIP, speedMax: {{{speedMaximum}}}, fuelMax: 1000, refuelRate: 100, repairRate: 2, autoPatrol: true, radarRange: 20000, radarChance: 100{{{(itemFuel ? ", refuelItem: SUPPLY" : "")}}}}
              - {type: OPTIONAL_NORMAL_ESCORT, speedMax: {{{escortSpeedMaximum}}}, damageMax: 100, fuelMax: 1000, weapons: 2, refuelRate: 100, repairRate: 2, autoPatrol: true}
            research:
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienDeployments:
              - {type: OPTIONAL_OPERATION_BASE, markerName: OPTIONAL_SOURCE, baseSelfDestructCode: {{{code}}}}
            ufos:
              - {type: OPTIONAL_RETURN_HUNTER, speedMax: 2200, damageMax: 50, radarRange: 20000, noAlert: true}
            ufoTrajectories:
              - {id: OPTIONAL_RETURN_AIR, waypoints: [[4, 3, 100], [5, 2, 80], [4, 3, 100]]}
            alienMissions:
              - type: OPTIONAL_RETURN_MISSION
                objective: 0
                waves: [{ufo: OPTIONAL_RETURN_HUNTER, count: 1, trajectory: OPTIONAL_RETURN_AIR, timer: 9000}]
            """, extraRulesAfterOverlays: true);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, string status)
    {
        var initial = CreateSnapshot(content, "Optional base craft return") with
        {
            RandomState = seed,
            Time = new(2, 2, 1, 1999, 23, 59, 55),
        };
        initial = StrategicWorldOptionalBaseMissionTests.Source(initial with
        {
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)], Soldiers = [], Transfers = [], Productions = [] }],
            World = initial.World with { Missions = [new(4, "OPTIONAL_RETURN_MISSION", "REGION", "RACE_A", 0, 0, 9000, 0, -1)] },
        });
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = status,
                Damage = status == "STR_REPAIRS" ? 4 : 0,
                Fuel = status == "STR_REFUELLING" ? 900 : 1000,
                Weapons = [],
                IsAutoPatrolling = false,
                LowFuel = false,
                MissionComplete = false,
                Speed = 0,
                Takeoff = 7,
                Longitude = initial.Bases[0].Longitude,
                Latitude = initial.Bases[0].Latitude,
                Destination = new(WorldTargetKind.AlienBase, "OPTIONAL_SOURCE", 6, 0.4, 0.2),
            },
        });
        return initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP")] }],
            World = initial.World with
            {
                AlienBases = [initial.World.AlienBases[0] with { Longitude = 0.4, Latitude = 0.2 }],
                Missions = [initial.World.Missions[0] with { AlienBase = initial.World.Missions[0].AlienBase! with { Longitude = 0.4, Latitude = 0.2 } }],
            },
        };
    }
}
