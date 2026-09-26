using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldCraftShieldTests
{
    [Theory]
    [InlineData(false, "grounded")]
    [InlineData(true, "grounded")]
    [InlineData(false, "hourly")]
    [InlineData(true, "hourly")]
    [InlineData(false, "takeoff")]
    [InlineData(true, "takeoff")]
    [InlineData(false, "return")]
    [InlineData(true, "return")]
    public void GeoscapeRechargeIncludesWeaponBonusesAndSurvivesReload(bool fromCache, string phase)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var rules = content.RuntimeRules;
        var rule = rules.Crafts[rules.Crafts.GetRequired("SHIELD_CRAFT")].Value;
        Assert.Equal(125, rule.ShieldRechargeInGeoscape);
        var snapshot = CreateSnapshot(content, "Craft shields") with
        { Time = new CampaignTime(1, 1, 1, 1999, 1, phase == "hourly" ? 59 : 1, phase == "hourly" ? 55 : 0), RandomState = 17 };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            RuleId = "SHIELD_CRAFT",
            Logistics = craft.Logistics! with
            {
                Status = phase is "grounded" or "hourly" ? "STR_READY" : "STR_OUT",
                Fuel = 100,
                Shield = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Takeoff = phase == "takeoff" ? 60 : 0,
                Weapons = [new CraftWeaponSnapshot("SHIELD_BOOST", 0, Disabled: true)],
                Destination = phase == "return" ? new WorldTargetReference(WorldTargetKind.Base,
                    WorldTargetReference.BaseType, snapshot.Bases[0].Id, 0.2, 0.1) : null,
            },
        });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var random = new SplitMix64RandomSource(17);
        var expected = 0;
        for (var tick = 0; tick < 20; tick++)
        {
            if (phase == "hourly" && tick == 0) expected += 2; // Base recharge precedes this pass.
            // Reference: 150 / 100, plus one when generate(0,99) < 50, clamped to 15.
            if (expected < 15) expected = Math.Min(15, expected + 1 + (random.NextInclusive(0, 99) < 50 ? 1 : 0));
            AdvanceOne(campaign);
            Assert.Equal(expected, Shield(campaign.Capture()));
            Assert.Equal(random.State, campaign.Capture().RandomState);
            if (tick == 0) campaign = Reload(content, campaign, 99, "craft-shield.sav");
        }
        Assert.Equal(15, expected);
        if (phase == "return") Assert.Equal("STR_READY", State(campaign.Capture()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalUfoArrivalDefersRechargeWithinOneBatchedCommand(bool batched)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0];
        var trajectory = content.RuntimeRules.UfoTrajectories[
            content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            RuleId = "SHIELD_CRAFT",
            Logistics = craft.Logistics! with { Status = "STR_READY", Weapons = [], Shield = 0 },
        }) with
        {
            RandomState = 17,
            World = snapshot.World with
            {
                Ufos = [ufo with
            {
                Status = UfoStatus.Flying, Altitude = WorldAltitudes.VeryLow,
                TrajectoryPoint = trajectory.Waypoints.Count - 1, Speed = 2200,
                Longitude = ufo.Destination!.Longitude, Latitude = ufo.Destination.Latitude,
            }]
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        if (batched)
            Assert.Equal(2, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(
                campaign.Execute(new AdvanceCampaignTime(2)).Events)).Summary.TickCount);
        else
        {
            AdvanceOne(campaign);
            Assert.Equal(0, Shield(campaign.Capture()));
            Assert.Equal(17UL, campaign.Capture().RandomState);
            campaign = Reload(content, campaign, 99, "deferred-shield.sav");
            AdvanceOne(campaign);
        }
        var random = new SplitMix64RandomSource(17);
        Assert.Equal(1 + (random.NextInclusive(0, 99) < 25 ? 1 : 0), Shield(campaign.Capture()));
        Assert.Equal(random.State, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShieldStatOverflowBlocksOwnedAndArrivingCraftBeforeMutation(bool incoming)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Shield overflow") with
        { Time = new CampaignTime(1, 1, 1, 1999, 1, 59, 55) };
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts.First(c => c.RuleId == "SHIP") with { Id = 9, RuleId = "SHIELD_CRAFT" };
        craft = craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_READY", Weapons = [new CraftWeaponSnapshot("SHIELD_EXCESS", 0)] }
        };
        snapshot = snapshot with
        {
            Bases = [owner with
        {
            Crafts = incoming ? owner.Crafts : [.. owner.Crafts, craft],
            Transfers = incoming ? [new TransferSnapshot(77, 1, CampaignTransferKind.Craft,
                craft.RuleId, 1, Craft: craft)] : [],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)),
            "Craft geoscape shield stats exceed the supported range.");
    }

    private static CraftLogisticsState State(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "SHIELD_CRAFT").Logistics!;

    private static int Shield(CampaignSnapshot snapshot) => State(snapshot).Shield;
}
