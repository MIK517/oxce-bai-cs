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
    public void ArmorVariantsSharingAStoreItemPreserveMaximumStock()
    {
        var content = StrategicReadinessTestContent.Load();
        var campaign = CreateCampaign(content, Personal(), new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue });

        Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
            new EquipSoldierArmor(0, 1, "LARGE_ARMOR")).Events));

        Assert.Equal(int.MaxValue, campaign.Capture().Bases[0].Items["SUPPLY"]);
    }

    [Fact]
    public void ReadinessIgnoresUnrequestedSoldierCapacityOverflow()
    {
        var content = StrategicReadinessTestContent.Load();
        var rules = content.RuntimeRules;
        var logistics = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        { Weapons = [new("SOLDIER_OVERFLOW_WEAPON", 0), null] };
        var campaign = CreateCampaign(content, Personal(), crafts: [new("SHIP", 1) { Logistics = logistics }]);

        var readiness = Assert.Single(campaign.QueryReadiness(0).Crafts);

        Assert.Equal(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value.FuelMaximum, readiness.FuelMaximum);
    }

    [Theory]
    [InlineData("OVERFLOW_WEAPON", false)]
    [InlineData("SHIELD_OVERFLOW_WEAPON", true)]
    public void ReadinessReportsAndClampsServiceCapacityOverflow(string weaponId, bool shield)
    {
        var content = StrategicReadinessTestContent.Load();
        var rules = content.RuntimeRules;
        var logistics = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        { Weapons = shield ? [new(weaponId, 0), new(weaponId, 0)] : [new(weaponId, 0), null] };
        var campaign = CreateCampaign(content, Personal(), crafts: [new("SHIP", 1) { Logistics = logistics }]);

        var readiness = campaign.QueryReadiness(0);

        var craft = Assert.Single(readiness.Crafts);
        Assert.Equal(int.MaxValue, shield ? craft.ShieldMaximum : craft.FuelMaximum);
        Assert.Equal("Craft service capacity exceeds the supported range.", readiness.ServiceLimitation);
    }

    [Fact]
    public void PersonnelAssignmentIgnoresUnrequestedFuelCapacityOverflow()
    {
        var content = StrategicReadinessTestContent.Load();
        var rules = content.RuntimeRules;
        var logistics = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        { Weapons = [new("OVERFLOW_WEAPON", 0), null] };
        var campaign = CreateCampaign(content, Personal(), crafts: [new("SHIP", 1) { Logistics = logistics }]);

        Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
            new AssignSoldierToCraft(0, 1, "SHIP", 1)).Events));
    }

    [Fact]
    public void PersonnelAssignmentBlocksRelevantCapacityOverflowAtomically()
    {
        var content = StrategicReadinessTestContent.Load();
        var rules = content.RuntimeRules;
        var logistics = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        { Weapons = [new("SOLDIER_OVERFLOW_WEAPON", 0), null] };
        var campaign = CreateCampaign(content, Personal(), crafts: [new("SHIP", 1) { Logistics = logistics }]);
        var before = campaign.Capture();

        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new AssignSoldierToCraft(0, 1, "SHIP", 1)).Events));

        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void AssignedArmorChangeBlocksRelevantCapacityOverflowAtomically()
    {
        var content = StrategicReadinessTestContent.Load();
        var rules = content.RuntimeRules;
        var logistics = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        { Weapons = [new("SOLDIER_OVERFLOW_WEAPON", 0), null] };
        var personal = Personal() with { CraftType = "SHIP", CraftId = 1 };
        var campaign = CreateCampaign(content, personal, new Dictionary<string, int> { ["SUPPLY"] = 1 },
            [new("SHIP", 1) { Logistics = logistics }]);
        var before = campaign.Capture();

        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new EquipSoldierArmor(0, 1, "LARGE_ARMOR")).Events));

        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void VehicleAdditionBlocksRelevantCapacityOverflowAtomically()
    {
        var content = StrategicReadinessTestContent.Load();
        var rules = content.RuntimeRules;
        var logistics = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        { Weapons = [new("SOLDIER_OVERFLOW_WEAPON", 0), null] };
        var campaign = CreateCampaign(content, Personal(), new Dictionary<string, int> { ["VEHICLE"] = 1 },
            [new("SHIP", 1) { Logistics = logistics }]);
        var before = campaign.Capture();

        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new ChangeCraftVehicle(0, "SHIP", 1, "VEHICLE", true)).Events));

        Assert.Equivalent(before, campaign.Capture(), strict: true);
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
    public void ItemProducingTransformationIgnoresUnusedSoldierCountersAndArmorStock()
    {
        var content = StrategicReadinessTestContent.Load();
        var personal = Personal() with
        {
            Armor = "LARGE_ARMOR",
            PreviousTransformations = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["RETIRE_TRANSFORMATION"] = int.MaxValue,
            },
            TransformationBonuses = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["PILOT_BONUS"] = int.MaxValue,
            },
        };
        var campaign = CreateCampaign(content, personal, new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue });

        Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
            new TransformCampaignSoldier(0, 1, "RETIRE_TRANSFORMATION")).Events));
        var state = campaign.Capture().Bases[0];
        Assert.Empty(state.Soldiers);
        Assert.Equal(int.MaxValue, state.Items["SUPPLY"]);
        var transfer = Assert.Single(state.Transfers);
        Assert.Equal(CampaignTransferKind.Item, transfer.Kind);
        Assert.Equal("SUPPLY", transfer.RuleId);
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
    public void ImmediateTransformationDoesNotConsumeATransferIdentity()
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = CreateCampaign(content, Personal()).Capture();
        var nextIds = new Dictionary<string, int>(initial.NextIds, StringComparer.Ordinal)
        {
            ["oxcePortTransfer"] = int.MaxValue,
        };
        var campaign = CampaignState.Restore(initial with { NextIds = nextIds }, content, new SplitMix64RandomSource(42));

        Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
            new TransformCampaignSoldier(0, 1, "IMMEDIATE_TYPE_TRANSFORMATION")).Events));

        var state = campaign.Capture();
        Assert.Empty(state.Bases[0].Transfers);
        Assert.Equal(int.MaxValue, state.NextIds["oxcePortTransfer"]);
    }

    [Fact]
    public void BaseManagementMarksOrdinaryAccessLiftsUnavailable()
    {
        var campaign = CreateCampaign(StrategicReadinessTestContent.Load(), Personal());

        var facilities = campaign.QueryBaseManagement(0).Facilities;

        Assert.NotNull(facilities.Single(facility => facility.RuleId == "LIFT").UnavailableReason);
        Assert.Null(facilities.Single(facility => facility.RuleId == "UPGRADE_LIFT").UnavailableReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseCreationBlocksExhaustedIdentityRangeAtomically(bool maximumExistingId)
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = CreateCampaign(content, Personal()).Capture();
        var nextIds = new Dictionary<string, int>(initial.NextIds, StringComparer.Ordinal);
        if (!maximumExistingId) nextIds["oxcePortBase"] = int.MaxValue;
        var campaign = CampaignState.Restore(initial with
        {
            NextIds = nextIds,
            Bases = maximumExistingId ? [initial.Bases[0] with { Id = int.MaxValue }] : initial.Bases,
        }, content, new SplitMix64RandomSource(42));
        var site = campaign.QueryBaseSites(false).First(candidate => !candidate.FakeUnderwater);
        var before = campaign.Capture();

        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new CreateCampaignBase("Beta", site.Longitude, site.Latitude, "LIFT", 2, 2)).Events));

        Assert.Equal("Base identity range is exhausted.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void BaseCreationBlocksAccountingOverflowAtomically()
    {
        var content = StrategicReadinessTestContent.Load();
        var initial = CreateCampaign(content, Personal()).Capture();
        var campaign = CampaignState.Restore(initial with { Expenditures = [long.MaxValue] }, content,
            new SplitMix64RandomSource(42));
        var site = campaign.QueryBaseSites(false).First(candidate => !candidate.FakeUnderwater);
        var before = campaign.Capture();

        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new CreateCampaignBase("Beta", site.Longitude, site.Latitude, "LIFT", 2, 2)).Events));

        Assert.Equal("Base accounting exceeds the supported range.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static CampaignState CreateCampaign(RuntimeContent content, SoldierPersonalState personal,
        IReadOnlyDictionary<string, int>? items = null, IReadOnlyList<CraftSnapshot>? crafts = null)
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
                Crafts = crafts ?? [],
                Items = items ?? new Dictionary<string, int>(),
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
