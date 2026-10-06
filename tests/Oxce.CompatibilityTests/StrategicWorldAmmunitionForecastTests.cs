using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false, "stock")]
    [InlineData(true, "stock")]
    [InlineData(false, "transfer")]
    [InlineData(true, "transfer")]
    [InlineData(false, "production")]
    [InlineData(true, "production")]
    [InlineData(false, "facility")]
    [InlineData(true, "facility")]
    public void SharedAmmunitionFuelRangesExecuteWhenEveryOutcomeRelaunchesTheSameCraft(bool cache, string source)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var native = TestFixtures.Rows(oracle.RootElement, "ammunitionFuelRows")
            .Where(row => row[0].GetInt32() == 4 && row[1].GetBoolean() && row[2].GetInt32() == 9 && row[3].GetInt32() == 2).ToArray();
        var overlay = AmmunitionForecastRules();
        if (source == "production") overlay += "manufacture: [{name: RANGE_REFILL, time: 1, producedItems: {RANGE_AMMO: 1}}]\n";
        overlay += "facilities: [{type: RANGE_WORKSHOP, mapName: LOGISTICS, workshops: 1}, {type: RANGE_FACILITY, mapName: LOGISTICS, ammoItem: RANGE_AMMO, ammoMax: 2, rearmRate: 1}]\n";
        var content = Load(cache, overlay);
        HashSet<int> outcomes = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = AmmunitionRangeSnapshot(content, source == "stock" ? 2 : source == "facility" ? 3 : 1, true) with { RandomState = seed };
            var owner = initial.Bases[0];
            if (source == "transfer") initial = initial with { Bases = [owner with { Transfers = [new(17, 1, CampaignTransferKind.Item, "RANGE_AMMO", 1)] }] };
            if (source == "production") initial = initial with
            {
                Bases = [owner with
            {
                Facilities = [.. owner.Facilities, new("RANGE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("RANGE_REFILL", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }]
            };
            if (source == "facility") initial = initial with { Bases = [owner with { Facilities = [.. owner.Facilities, new("RANGE_FACILITY", 1, 1, 0, 0, false, false, false)] }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "ammo-fuel-range-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var stock = after.Bases[0].Items.GetValueOrDefault("RANGE_AMMO");
            outcomes.Add(stock);
            var row = Assert.Single(native, row => row[7].GetInt32() == stock);
            var gun = after.Bases[0].Crafts[0].Logistics!.Weapons[0]!;
            var craft = after.Bases[0].Crafts[1].Logistics!;
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).Take(6).ToArray());
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { gun.Ammo, gun.Rearming, stock, craft.Status, craft.Fuel, after.World.Ufos[0].Destination!.Id });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, seed, "ammo-fuel-range-after.sav");
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void OrderedFuelConsumersRequireEnoughStockAtBothAmmunitionEndpoints(bool cache, int stock)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(cache, AmmunitionForecastRules());
        var initial = AmmunitionRangeSnapshot(content, stock, true);
        var owner = initial.Bases[0];
        initial = initial with { Bases = [owner with { Crafts = [owner.Crafts[0], owner.Crafts[1], owner.Crafts[1] with { Id = 4 }] }] };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ammo-fuel-order-before.sav");
        if (stock == 2) AssertTimeBlocked(campaign, "UFO retargeting requires stochastic craft ammunition stock forecasts.");
        else
        {
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.All(after.Bases[0].Crafts.Where(craft => craft.RuleId == "RANGE_FUEL"), craft =>
            {
                Assert.Equal("STR_OUT", craft.Logistics!.Status);
                Assert.Equal(99, craft.Logistics.Fuel);
            });
            Assert.True(after.World.Ufos[0].Hunting);
            Assert.Equal(1, after.World.Ufos[0].Destination!.Id);
            Reload(content, campaign, 81, "ammo-fuel-order-after.sav");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NonfuelAmmunitionRangesPreserveRealStocksAndSharedCraftStates(bool cache, bool shared)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(cache, AmmunitionForecastRules());
        HashSet<int> quantities = [];
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var initial = AmmunitionRangeSnapshot(content, shared ? 3 : 1, false) with { RandomState = seed };
            if (shared) initial = initial with
            {
                Bases = [initial.Bases[0] with { Crafts = [initial.Bases[0].Crafts[0], initial.Bases[0].Crafts[0] with { Id = 3 }, initial.Bases[0].Crafts[1]] }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "ammo-range-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.All(after.Bases[0].Crafts.Where(craft => craft.RuleId == "RANGE_CONSUMER"), craft =>
            {
                Assert.Equal(10, craft.Logistics!.Weapons[0]!.Ammo);
                Assert.False(craft.Logistics.Weapons[0]!.Rearming);
                Assert.Equal("STR_REARMING", craft.Logistics.Status);
            });
            var stock = after.Bases[0].Items.GetValueOrDefault("RANGE_AMMO");
            Assert.InRange(stock, shared ? 1 : 0, shared ? 3 : 1);
            quantities.Add(stock);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            Reload(content, campaign, seed, "ammo-range-after.sav");
        }
        Assert.Equal(shared ? 3 : 2, quantities.Count);
    }

    [Theory]
    [InlineData(false, "craft")]
    [InlineData(true, "craft")]
    [InlineData(false, "facility")]
    [InlineData(true, "facility")]
    [InlineData(false, "material")]
    [InlineData(true, "material")]
    [InlineData(false, "overflow")]
    [InlineData(true, "overflow")]
    public void StochasticSharedEffectsStopBeforeTickMutation(bool cache, string effect)
    {
        var overlay = AmmunitionForecastRules();
        if (effect is "material" or "overflow") overlay +=
            $"manufacture: [{{name: RANGE_PRODUCT, time: 1, {(effect == "material" ? "requiredItems: {RANGE_AMMO: 1}, producedItems: {SUPPLY: 1}" : "producedItems: {RANGE_AMMO: 1}")}}}]\n";
        overlay += "facilities: [{type: RANGE_WORKSHOP, mapName: LOGISTICS, workshops: 6}, {type: RANGE_FACILITY, mapName: LOGISTICS, ammoItem: RANGE_AMMO, ammoMax: 2, rearmRate: 1}]\n";
        var content = Load(cache, overlay);
        var initial = AmmunitionRangeSnapshot(content, effect == "overflow" ? int.MaxValue : 1, false);
        var owner = initial.Bases[0];
        if (effect == "craft") initial = initial with
        {
            Bases = [owner with { Crafts = [owner.Crafts[0], owner.Crafts[0] with { Id = 3, Logistics = owner.Crafts[0].Logistics! with { Weapons = [new("RANGE_GUN", 0, true)] } }, owner.Crafts[1]] }],
        };
        else if (effect == "facility") initial = initial with
        {
            Bases = [owner with { Facilities = [.. owner.Facilities, new("RANGE_FACILITY", 1, 1, 0, 0, false, false, false)] }],
        };
        else initial = initial with
        {
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities, new("RANGE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("RANGE_PRODUCT", 2, 0, 99, true, false, false, new Dictionary<string, int>())],
            }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), effect switch
        {
            "craft" => "UFO retargeting requires stochastic shared craft ammunition forecasts.",
            "facility" => "UFO retargeting requires stochastic facility ammunition forecasts.",
            "material" => "UFO retargeting requires stochastic production material forecasts.",
            _ => "Production outputs exceed the supported stock range.",
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RearmRangesMatchBothNativeBulletSavingEndpointsWithoutChangingInputs(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var settings in TestFixtures.Rows(oracle.RootElement, "ammunitionFuelRows")
            .GroupBy(row => (Size: row[0].GetInt32(), Saving: row[1].GetBoolean())))
        {
            var rules = Load(cache, AmmunitionForecastRules(settings.Key.Size, settings.Key.Saving)).RuntimeRules;
            foreach (var outcomes in settings.GroupBy(row => (Ammo: row[2].GetInt32(), Stock: row[3].GetInt32())))
            {
                var state = new CraftLogisticsState(100, 0, "STR_REARMING", [new("RANGE_GUN", outcomes.Key.Ammo, true)], new Dictionary<string, int>(), []);
                var stocks = new Dictionary<string, int> { ["RANGE_AMMO"] = outcomes.Key.Stock };
                Assert.True(CraftServicing.TryForecastRearmRange(state, rules, stocks, out var forecast));
                var consumption = outcomes.Select(row => outcomes.Key.Stock - row[7].GetInt32() - (row[8].GetString() == "STR_OUT" ? 1 : 0)).ToArray();
                var row = outcomes.First();
                var expected = JsonSerializer.SerializeToUtf8Bytes(new object[] { row[5].GetInt32(), row[6].GetBoolean(), consumption.Min(), consumption.Max() });
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { forecast.State.Weapons[0]!.Ammo, forecast.State.Weapons[0]!.Rearming, forecast.MinimumUsed, forecast.MaximumUsed });
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(outcomes.Key.Stock, stocks["RANGE_AMMO"]);
                Assert.Equal(outcomes.Key.Ammo, state.Weapons[0]!.Ammo);
                Assert.True(state.Weapons[0]!.Rearming);
            }
        }
    }

    private static string AmmunitionForecastRules(int clipSize = 4, bool saving = true) =>
        $"items: [{{type: RANGE_AMMO, clipSize: {clipSize}}}]\n" +
        $"craftWeapons: [{{type: RANGE_GUN, ammoMax: 10, rearmRate: 4, bulletSaving: {saving.ToString().ToLowerInvariant()}, launcher: SUPPLY, clip: RANGE_AMMO}}]\n" +
        "crafts: [{type: RANGE_CONSUMER, speedMax: 2000, damageMax: 100, fuelMax: 100, weapons: 1}, {type: RANGE_FUEL, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: RANGE_AMMO, autoPatrol: true}]\n";

    private static CampaignSnapshot AmmunitionRangeSnapshot(RuntimeContent content, int stock, bool fuel)
    {
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var gun = template with
        {
            RuleId = "RANGE_CONSUMER",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REARMING",
                Fuel = 100,
                Weapons = [new("RANGE_GUN", 9, true)],
            }
        };
        var target = fuel ? template with
        {
            RuleId = "RANGE_FUEL",
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            }
        } : template;
        return initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Crafts = [gun, target], Soldiers = [],
            Items = new Dictionary<string, int> { ["RANGE_AMMO"] = stock },
        } ]
        };
    }
}
