using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
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
}
