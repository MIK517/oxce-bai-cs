using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CraftServicingTests
{
    [Fact]
    public void HourlyRepairRearmAndHalfHourlyRefuelSurviveEveryReload()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var initial = CampaignLogisticsTests.Create(content).Capture();
        var rules = content.RuntimeRules;
        var ship = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0)
            with
        { Status = "STR_REPAIRS", Damage = 1, Fuel = 99, Weapons = [new("FIXED", 9, true), null] };
        var campaign = CampaignState.Restore(initial with
        {
            Bases = [initial.Bases[0] with
        { Crafts = [new("SHIP", 1) { Logistics = ship }], Items = new Dictionary<string, int> { ["BULKY"] = 1 } }]
        }, content, new SplitMix64RandomSource(0));
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        Assert.Equal("STR_REARMING", State().Status);
        Assert.Equal(0, State().Damage);
        Reload();
        campaign.Execute(new AdvanceCampaignTime(720));
        Assert.Equal(10, State().Weapons[0]!.Ammo);
        Assert.False(State().Weapons[0]!.Rearming);
        Assert.Equal("STR_REARMING", State().Status); // No early transition on completing the last weapon.
        Reload();
        campaign.Execute(new AdvanceCampaignTime(720));
        Assert.Equal("STR_READY", State().Status);
        Assert.Equal(100, State().Fuel);
        Reload();

        CraftLogisticsState State() => campaign.Capture().Bases[0].Crafts[0].Logistics!;
        void Reload()
        {
            var snapshot = campaign.Capture();
            campaign = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(snapshot), "service.sav", content,
                new SplitMix64RandomSource(7), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" })).Campaign;
            Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
        }
    }

    [Fact]
    public void AmmunitionShortageClearsOnlyFirstWeaponAndDoesNotSkipItsHour()
    {
        var rules = CampaignLogisticsTests.LoadFixture().RuntimeRules;
        var state = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0)
            with
        { Status = "STR_REARMING", Weapons = [new("FIXED", 0, true), new("FIXED", 0, true)] };
        var result = CraftServicing.Rearm(state, rules, new Dictionary<string, int>(), new SplitMix64RandomSource(1));
        Assert.True(result.MissingAmmo);
        Assert.False(result.State.Weapons[0]!.Rearming);
        Assert.True(result.State.Weapons[1]!.Rearming);
        Assert.Equal("STR_REARMING", result.State.Status);
        Assert.Equal(0, result.ClipsUsed);
    }

    [Fact]
    public void InvalidServiceValuesStopBeforeTickAndStockMutation()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var snapshot = CampaignLogisticsTests.Create(content).Capture();
        var ship = CraftLogistics.Purchase(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value,
            content.RuntimeRules, 0, 0) with
        { Weapons = [new("FIXED", int.MaxValue, true), null], Status = "STR_REARMING" };
        var campaign = CampaignState.Restore(snapshot with { Bases = [snapshot.Bases[0] with { Crafts = [new("SHIP", 1) { Logistics = ship }] }] },
            content, new SplitMix64RandomSource(0));
        var before = campaign.Capture();
        Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
