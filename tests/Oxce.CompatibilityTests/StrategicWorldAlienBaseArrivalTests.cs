using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldAlienBaseArrivalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ArrivalUsesNativeDiscoveryAndLandingConditions(int takeoff)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var contents = new[] { Content(false), Content(true) };
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "alienBaseArrivalRows").Where(row => row[4].GetInt32() == takeoff))
        {
            var discovered = row[0].GetBoolean();
            var units = row[1].GetInt32();
            var allow = row[2].GetBoolean();
            var suppress = row[3].GetBoolean();
            var content = contents[allow ? 1 : 0];
            var initial = Snapshot(content, discovered, units, suppress, takeoff);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            if (row[5].GetBoolean())
            {
                AssertLandingRequested(campaign);
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var state = after.Bases[0].Crafts[0].Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                discovered, units, allow, suppress, takeoff, false,
                state.Destination!.Kind == WorldTargetKind.Base, state.Speed, state.Takeoff, state.Status,
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            Assert.Equal(WorldTargetKind.AlienBase, initial.Bases[0].Crafts[0].Logistics!.Destination!.Kind);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, 81, "alien-base-arrival.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(81));
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    [Fact]
    public void EarlierDiscoveryReservesLandingWithoutConsumingTheTick()
    {
        foreach (var (daily, facilityRange, buildTime, patrolRange, patrolFuel, blocked) in new[]
        {
            (true, 20000, 0, 0, 1000, true),
            (true, 20000, 1, 0, 1000, true),
            (true, 20000, 2, 0, 1000, false),
            (true, 0, 0, 0, 1000, false),
            (false, 20000, 0, 0, 1000, false),
            (false, 0, 0, 10000, 1000, true),
            (false, 0, 0, 10000, 0, false),
            (false, 0, 0, 0, 1000, false),
        })
        {
            var content = Content(true, $$"""
                facilities:
                  - {type: ARRIVAL_SIGHT, mapName: LOGISTICS, sightRange: {{facilityRange}}, sightChance: 100}
                """, patrolRange);
            var initial = Snapshot(content, false, 1, false, 0);
            var owner = initial.Bases[0];
            var patrol = owner.Crafts[0] with
            {
                Id = 2,
                Logistics = owner.Crafts[0].Logistics! with { Destination = null, Fuel = patrolFuel, Speed = 0 },
            };
            initial = initial with
            {
                Time = daily ? new(2, 2, 1, 1999, 23, 59, 55) : new(1, 1, 1, 1999, 1, 9, 55),
                Bases = [owner with
                {
                    Facilities = [new("ARRIVAL_SIGHT", 0, 0, buildTime, 0, false, false, false)],
                    Crafts = [owner.Crafts[0], patrol],
                }],
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            if (blocked)
                AssertLandingRequested(campaign);
            else
            {
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.True(!result.Events.OfType<CampaignActionBlocked>().Any(),
                    $"daily={daily}, facilityRange={facilityRange}, buildTime={buildTime}, patrolRange={patrolRange}, patrolFuel={patrolFuel}: {string.Join(';', result.Events)}");
                var after = campaign.Capture();
                Assert.False(Assert.Single(after.World.AlienBases).Discovered);
                Assert.Equal(WorldTargetKind.AlienBase, after.Bases[0].Crafts[0].Logistics!.Destination!.Kind);
                campaign = Reload(content, campaign, 81, "undiscovered-base-after-sight.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManufacturedPatrolRemovalPrecedesDiscoveryAndLanding(bool removed)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "deletedCraftFollowerRows"));
        var content = Content(true, """
            facilities: [{type: ARRIVAL_WORKSHOP, mapName: LOGISTICS, workshops: 2}]
            manufacture: [{name: ARRIVAL_DELETE_PATROL, time: 1, requiredItems: {ARRIVAL_PATROL: 0}, producedItems: {SUPPLY: 0}}]
            """, extraCrafts: "  - {type: ARRIVAL_PATROL, fuelMax: 1000, speedMax: 6000, sightRange: 10000}");
        var initial = Snapshot(content, false, 1, false, 0);
        var owner = initial.Bases[0];
        var patrol = owner.Crafts[0] with
        {
            RuleId = "ARRIVAL_PATROL",
            Id = 2,
            Logistics = owner.Crafts[0].Logistics! with { Destination = null, Speed = 0 },
        };
        initial = initial with
        {
            Time = new(1, 1, 1, 1999, 1, 59, 55),
            Bases = [owner with
            {
                Crafts = [owner.Crafts[0], patrol],
                Facilities = [new("ARRIVAL_WORKSHOP", 0, 0, 0, 0, false, false, false)],
                Engineers = 0,
                Productions = [new("ARRIVAL_DELETE_PATROL", 1, 0, removed ? 2 : 1, false, false, false,
                    new Dictionary<string, int>(StringComparer.Ordinal))],
            }],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        if (!removed)
        {
            AssertLandingRequested(campaign);
            return;
        }
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Single(after.Bases[0].Crafts);
        Assert.False(Assert.Single(after.World.AlienBases).Discovered);
        Assert.Equal(WorldTargetKind.AlienBase, after.Bases[0].Crafts[0].Logistics!.Destination!.Kind);
        campaign = Reload(content, campaign, 81, "removed-patrol-before-discovery.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
    }

    internal static RuntimeContent Content(bool allow, string extra = "", int sightRange = 0, string extraCrafts = "") => StrategicReadinessTestContent.Load(
        ["strategic-world.rul"], $$"""
        crafts:
          - {type: SHIP, fuelMax: 1000, speedMax: 6000, sightRange: {{sightRange}}, allowLanding: {{(allow ? "true" : "false")}}}
        {{extraCrafts}}
        alienDeployments:
          - {type: ARRIVAL_BASE, markerName: ARRIVAL_BASE, points: 0, genMissionFrequency: 0, huntMissionFrequency: 0}
        {{extra}}
        """, extraRulesAfterOverlays: true);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, bool discovered, int units, bool suppress, int takeoff)
    {
        var initial = CreateSnapshot(content, "Alien base arrival");
        var owner = initial.Bases[0];
        var ship = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        ship = ship with
        {
            Logistics = ship.Logistics! with
            {
                Status = "STR_OUT",
                Speed = 1000,
                Fuel = 1000,
                Takeoff = takeoff,
                LowFuel = false,
                IsAutoPatrolling = false,
                Longitude = 0.4,
                Latitude = 0.2,
                Items = new Dictionary<string, int>(StringComparer.Ordinal),
                Weapons = [],
                Destination = new(WorldTargetKind.AlienBase, "ARRIVAL_BASE", 6, 0.4, 0.2),
            },
        };
        return initial with
        {
            Time = new(1, 1, 1, 1999, 1, 0, 0),
            Options = initial.Options with { SuppressLandingWithoutEquipment = suppress },
            Bases = [owner with
            {
                Crafts = [ship],
                Soldiers = units == 0 ? [] : [owner.Soldiers[0] with
                {
                    Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = ship.Id },
                }],
                Research = [],
                Transfers = [],
                Productions = [],
            }],
            World = initial.World with { AlienBases = [new(6, "ARRIVAL_BASE", "RACE_A", 0.4, 0.2) { Discovered = discovered }] },
        };
    }
}
