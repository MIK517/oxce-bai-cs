using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CraftLogisticsTests
{
    [Fact]
    public void StartingCraftLoadsSuppliedStateWithoutInitializingFixedWeapons()
    {
        var rules = CampaignLogisticsTests.LoadFixture().RuntimeRules;
        var rule = Assert.Single(rules.Crafts.Rules).Value;
        var empty = CraftLogistics.LoadStarting(rule, null);
        Assert.All(empty.Weapons, Assert.Null);
        Assert.Equal("STR_READY", empty.Status);
        Assert.Equal(0, empty.Fuel);
        var loaded = CraftLogistics.LoadStarting(rule, new("Named craft", 45, 3, "STR_REPAIRS", 2, true,
            [null, new("FIXED", 7, true, false)], new Dictionary<string, int> { ["SUPPLY"] = 4 }, []));
        Assert.Equal("Named craft", loaded.Name);
        Assert.Equal(45, loaded.Fuel);
        Assert.Equal(3, loaded.Damage);
        Assert.Equal(2, loaded.ExcessFuel);
        Assert.True(loaded.LowFuel);
        Assert.Null(loaded.Weapons[0]);
        Assert.Equal(7, loaded.Weapons[1]!.Ammo);
        Assert.True(loaded.Weapons[1]!.Rearming);
        Assert.Equal(4, loaded.Items["SUPPLY"]);
    }

    [Fact]
    public void PurchasedCraftKeepsEmptySlotsAndArrivalChecksDamageWeaponsThenFuel()
    {
        var rules = CampaignLogisticsTests.LoadFixture().RuntimeRules;
        var rule = Assert.Single(rules.Crafts.Rules).Value;
        var purchased = CraftLogistics.Purchase(rule, rules, 1, 0.5);
        Assert.Equal("STR_REFUELLING", purchased.Status);
        Assert.Equal(2, purchased.Weapons.Count);
        Assert.Equal("FIXED", purchased.Weapons[0]!.RuleId);
        Assert.Null(purchased.Weapons[1]);
        Assert.Equal(0, purchased.Weapons[0]!.Ammo);
        Assert.False(purchased.Weapons[0]!.Rearming);
        var arrived = CraftLogistics.Arrive(purchased, rule, rules, 2, -0.5);
        Assert.Equal("STR_REARMING", arrived.Status);
        Assert.True(arrived.Weapons[0]!.Rearming);
        Assert.Equal(2, arrived.Longitude);
        Assert.Equal(-0.5, arrived.Latitude);
        var damaged = CraftLogistics.Arrive(purchased with { Damage = 1 }, rule, rules, 0, 0);
        Assert.Equal("STR_REPAIRS", damaged.Status);
        Assert.True(damaged.Weapons[0]!.Rearming);
        var disabled = purchased with { Weapons = [purchased.Weapons[0]! with { Disabled = true }, null] };
        Assert.Equal("STR_REFUELLING", CraftLogistics.Arrive(disabled, rule, rules, 0, 0).Status);
        Assert.Equal("STR_READY", CraftLogistics.Arrive(disabled with { Fuel = 100 }, rule, rules, 0, 0).Status);
    }
}
