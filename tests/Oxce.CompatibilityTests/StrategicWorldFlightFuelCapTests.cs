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

public sealed class StrategicWorldFlightFuelCapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlightFuelCapsRefundsAndFreePatrolMatchNativeAndPersist(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var itemFuel in new[] { false, true })
            foreach (var freePatrol in new[] { false, true })
            {
                var content = Content(cache, itemFuel, freePatrol);
                foreach (var row in TestFixtures.Rows(oracle.RootElement, "flightFuelCapRows")
                    .Where(row => row[0].GetBoolean() == itemFuel && row[2].GetBoolean() == freePatrol))
                {
                    var initial = Snapshot(content, row[1].GetBoolean(), row[3].GetInt32(), row[4].GetInt32(), row[5].GetInt32());
                    var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
                    AdvanceUnblocked(campaign, 1);
                    var after = campaign.Capture();
                    var state = Assert.Single(after.Bases[0].Crafts).Logistics!;
                    var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                    {
                        itemFuel, row[1].GetBoolean(), freePatrol, row[3].GetInt32(), row[4].GetInt32(), row[5].GetInt32(),
                        state.Fuel, state.ExcessFuel, after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                    });
                    Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
                    Assert.Equal(99, state.Takeoff);
                    for (var rewrite = 0; rewrite < 3; rewrite++)
                    {
                        campaign = Reload(content, campaign, 17, "flight-fuel-cap.sav");
                        Assert.Equivalent(after, campaign.Capture(), strict: true);
                    }
                    AdvanceUnblocked(campaign, 1);
                    Assert.Equal(state.Fuel, campaign.Capture().Bases[0].Crafts[0].Logistics!.Fuel);
                    Assert.Equal(after.Bases[0].Items.GetValueOrDefault("SUPPLY"), campaign.Capture().Bases[0].Items.GetValueOrDefault("SUPPLY"));
                }
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefundStockBoundsAreAggregatedAndAtomic(bool cache)
    {
        var content = Content(cache, true, false);
        var initial = Snapshot(content, true, 500, 245, 0);
        initial = initial with { Bases = [initial.Bases[0] with { Items = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue - 3 } }] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().Bases[0].Items["SUPPLY"]);
        var craft = initial.Bases[0].Crafts[0];
        var overflow = initial with { Bases = [initial.Bases[0] with { Crafts = [craft, craft with { Id = 2 }] }] };
        AssertTimeBlocked(CampaignState.Restore(overflow, content, new SplitMix64RandomSource(17)),
            "Craft flight fuel refunds exceed the supported stock range.");
        overflow = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { ExcessFuel = int.MaxValue } });
        AssertTimeBlocked(CampaignState.Restore(overflow, content, new SplitMix64RandomSource(17)),
            "Craft flight fuel values exceed the supported range.");
        content = Content(cache, true, false, 0);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, true, 500, 245, 0), content, new SplitMix64RandomSource(17)),
            "Item flight fuel refunds require a positive refuel rate.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HourlyArrivalAndFlightRefundShareOneStockCapacity(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, true, false);
        var initial = Snapshot(content, true, 500, 245, 0) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Items = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue - 4 },
                Transfers = [new(1, 1, CampaignTransferKind.Item, "SUPPLY", 1)],
            }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().Bases[0].Items["SUPPLY"]);
        var overflow = initial with
        {
            Bases = [initial.Bases[0] with
            { Items = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue - 3 } }]
        };
        AssertTimeBlocked(CampaignState.Restore(overflow, content, new SplitMix64RandomSource(17)),
            "Craft flight fuel refunds exceed the supported stock range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsafeFlightArithmeticIsRefusedBeforeForecastsOrTickMutation(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, false, false);
        var initial = Snapshot(content, true, -100, int.MaxValue, 0);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft flight fuel values exceed the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelaunchedFlightRefundIsValidatedBeforeHuntingForecast(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, true, false, 0);
        var initial = Snapshot(content, true, 500, 245, 0) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_REARMING",
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 2,
                AutoPatrolLatitude = 0.7,
            },
        });
        var hunter = CreateUfoTransitCampaign(content, initial.Time, "HUNTER", 2200).Capture().World;
        initial = initial with { World = hunter with { Ufos = [hunter.Ufos[0] with { HunterKiller = true }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Item flight fuel refunds require a positive refuel rate.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstalledDisabledWeaponsContributeToFlightFuelCapacityAndReload(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
            craftWeapons:
              - {type: FLIGHT_FUEL_BONUS, ammoMax: 0, launcher: SUPPLY, stats: {fuelMax: 50, soldiers: 2147483647}}
            """, finalRules: "crafts: [{type: SHIP, fuelMax: 0, refuelItem: SUPPLY, refuelRate: 40, patrolWithoutFuel: false}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "flightFuelCapRows")
            .Where(row => row[0].GetBoolean() && row[1].GetBoolean() && !row[2].GetBoolean()))
        {
            var initial = Snapshot(content, true, row[3].GetInt32(), row[4].GetInt32(), row[5].GetInt32());
            initial = initial.WithCraft("SHIP", craft => craft with
            { Logistics = craft.Logistics! with { Weapons = [new("FLIGHT_FUEL_BONUS", 0, Disabled: true), new("FLIGHT_FUEL_BONUS", 0, Disabled: true)] } });
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(row[6].GetInt32(), after.Bases[0].Crafts[0].Logistics!.Fuel);
            Assert.Equal(row[7].GetInt32(), after.Bases[0].Crafts[0].Logistics!.ExcessFuel);
            Assert.Equal(row[8].GetInt32(), after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
            Assert.Equivalent(after, Reload(content, campaign, 17, "flight-fuel-weapon-bonus.sav").Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryRelaunchSubtractionIsValidatedWithoutAnyHunter(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            finalRules: "crafts: [{type: SHIP, speedMax: -100, fuelMax: 100, refuelRate: 0}]\n");
        var initial = Snapshot(content, true, 0, int.MaxValue, 0) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_REARMING",
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 2,
                AutoPatrolLatitude = 0.7,
            },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft flight fuel values exceed the supported range.");
    }

    internal static RuntimeContent Content(bool cache, bool itemFuel, bool freePatrol, int rate = 40) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, $$$"""
            crafts:
              - {type: SHIP, speedMax: 6000, fuelMax: 100, refuelRate: {{{rate}}}, patrolWithoutFuel: {{{freePatrol.ToString().ToLowerInvariant()}}}{{{(itemFuel ? ", refuelItem: SUPPLY" : "")}}}}
            """, extraRulesAfterOverlays: true);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, bool destination, int speed, int fuel, int excess)
    {
        var initial = CreateSnapshot(content, "Flight fuel caps") with { RandomState = 17, Time = new(1, 1, 1, 1999, 1, 9, 55) };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = fuel,
                ExcessFuel = excess,
                Speed = speed,
                Takeoff = 100,
                Longitude = 1,
                Latitude = 0.6,
                Weapons = [],
                LowFuel = false,
                MissionComplete = false,
                IsAutoPatrolling = false,
                Destination = destination ? WorldTargetReference.ForWaypoint(21, new(2, 0.7)) : null,
            },
        });
        return initial with
        {
            Bases = [initial.Bases[0] with
            {
                Crafts = [initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP")],
                Items = new Dictionary<string, int>(),
                Research = [],
                Productions = [],
                Transfers = [],
                Soldiers = [],
            }],
            World = initial.World with { Waypoints = destination ? [new(21, 2, 0.7)] : [] },
        };
    }
}
