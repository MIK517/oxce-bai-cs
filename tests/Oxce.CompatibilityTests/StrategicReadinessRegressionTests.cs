using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicReadinessRegressionTests
{
    [Fact]
    public void ArmorVariantsSharingAStoreItemCanBeSwappedWithoutLooseStock()
    {
        var content = StrategicReadinessTestContent.Load();
        var campaign = CreateCampaign(content, Personal() with { Armor = "ARMOR" });

        Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
            new EquipSoldierArmor(0, 1, "LARGE_ARMOR")).Events));
        var state = campaign.Capture().Bases[0];
        Assert.Equal("LARGE_ARMOR", Assert.Single(state.Soldiers).Personal!.Armor);
        Assert.Empty(state.Items);
    }

    [Fact]
    public void ResetTransformationClearsMaximumCountersBeforeAwardingNewValues()
    {
        var content = StrategicReadinessTestContent.Load();
        var personal = Personal() with
        {
            PreviousTransformations = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["RESET_COUNTER_TRANSFORMATION"] = int.MaxValue,
                ["LEGACY"] = 4,
            },
            TransformationBonuses = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["PILOT_BONUS"] = int.MaxValue,
            },
        };
        var campaign = CreateCampaign(content, personal);

        Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
            new TransformCampaignSoldier(0, 1, "RESET_COUNTER_TRANSFORMATION")).Events));
        var transformed = Assert.Single(campaign.Capture().Bases[0].Soldiers).Personal!;
        Assert.Equal(1, Assert.Single(transformed.PreviousTransformations).Value);
        Assert.Equal(1, Assert.Single(transformed.TransformationBonuses).Value);
    }

    [Fact]
    public void CloneAwardsBonusToNewSoldierWhenSourceCounterIsMaximum()
    {
        var content = StrategicReadinessTestContent.Load();
        var campaign = CreateCampaign(content, Personal() with
        {
            TransformationBonuses = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["PILOT_BONUS"] = int.MaxValue,
            },
        });

        Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
            new TransformCampaignSoldier(0, 1, "CLONE_BONUS_TRANSFORMATION")).Events));
        var state = campaign.Capture().Bases[0];
        Assert.Equal(int.MaxValue, Assert.Single(state.Soldiers).Personal!.TransformationBonuses["PILOT_BONUS"]);
        Assert.Equal(1, Assert.Single(state.Transfers).Soldier!.Personal!.TransformationBonuses["PILOT_BONUS"]);
    }

    [Fact]
    public void TypeChangingTransformationNormalizesNationalityForDestinationPools()
    {
        var content = StrategicReadinessTestContent.Load();
        var campaign = CreateCampaign(content, Personal() with { Nationality = 7 });

        Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
            new TransformCampaignSoldier(0, 1, "IMMEDIATE_TYPE_TRANSFORMATION")).Events));

        Assert.Equal(0, Assert.Single(campaign.Capture().Bases[0].Soldiers).Personal!.Nationality);
    }

    [Fact]
    public void BaseManagementMarksOrdinaryAccessLiftsUnavailable()
    {
        var campaign = CreateCampaign(StrategicReadinessTestContent.Load(), Personal());

        var facilities = campaign.QueryBaseManagement(0).Facilities;

        Assert.NotNull(facilities.Single(facility => facility.RuleId == "LIFT").UnavailableReason);
        Assert.Null(facilities.Single(facility => facility.RuleId == "UPGRADE_LIFT").UnavailableReason);
    }

    private static CampaignState CreateCampaign(RuntimeContent content, SoldierPersonalState personal)
    {
        var initial = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Readiness regressions", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance).Capture();
        return CampaignState.Restore(initial with
        {
            Funds = [10_000],
            Bases = [initial.Bases[0] with
            {
                Name = "Alpha",
                Soldiers = [new("RECRUIT", 1) { Personal = personal }],
                Items = new Dictionary<string, int>(),
            }],
        }, content, new SplitMix64RandomSource(42));
    }

    private static SoldierPersonalState Personal()
    {
        var stats = new Dictionary<string, short>(StringComparer.Ordinal)
        {
            ["tu"] = 50,
            ["stamina"] = 60,
            ["health"] = 30,
            ["mana"] = 20,
        };
        return new("Regression", "", 0, 0, 0, 0, "ARMOR", stats, stats);
    }

}
