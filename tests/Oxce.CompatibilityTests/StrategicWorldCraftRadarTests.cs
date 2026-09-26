using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>GeoscapeState::ufoDetection with Craft::detect, and the rearm, refuel and
/// auto-patrol handlers that decide which crafts are active before detection.</summary>
public sealed class StrategicWorldCraftRadarTests
{
    [Theory]
    [InlineData("STR_READY")]
    [InlineData("STR_REFUELLING")]
    public void GroundedAutoPatrolFlagDoesNotBlockUfoDetection(string status)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var craftRuleId = status == "STR_REFUELLING" ? "SHIP_SLOW_REFUEL" : "SHIP";
        var snapshot = campaign.Capture().WithCraft("SHIP", craft => AutoPatrolling(
            craft with { RuleId = craftRuleId }, status, status == "STR_REFUELLING" ? 0 : craft.Logistics!.Fuel));
        campaign = CampaignState.Restore(snapshot.WithFacility("RADAR_HYPER_TEST"), content,
            new SplitMix64RandomSource(84));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: true });
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.NotEqual("STR_OUT", Assert.Single(campaign.Capture().Bases[0].Crafts,
            candidate => candidate.RuleId == craftRuleId).Logistics!.Status);
    }

    [Fact]
    public void AutoPatrolRelaunchDetectsUfoAtTheSameHalfHourBoundary()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture().WithCraft("SHIP", craft => AutoPatrolling(craft, "STR_REFUELLING", 0));
        campaign = CampaignState.Restore(UfoNearBase(snapshot), content, new SplitMix64RandomSource(85));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.Equal("STR_OUT", Assert.Single(campaign.Capture().Bases[0].Crafts,
            candidate => candidate.RuleId == "SHIP").Logistics!.Status);
    }

    [Fact]
    public void HourlyRearmingRelaunchReservesContactMarkerBeforeServicing()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture().WithCraft("SHIP", craft => AutoPatrolling(craft, "STR_REARMING", 0))
            .WithNextId("STR_UFO", int.MaxValue);
        campaign = CampaignState.Restore(UfoNearBase(snapshot, untracked: true), content, new SplitMix64RandomSource(88));

        AssertTimeBlocked(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData("transfer", false)]
    [InlineData("transfer", true)]
    [InlineData("production", false)]
    [InlineData("production", true)]
    [InlineData("random-production", false)]
    [InlineData("random-production", true)]
    [InlineData("fallback-production", false)]
    [InlineData("fallback-production", true)]
    [InlineData("research", false)]
    [InlineData("research", true)]
    [InlineData("research-reward", false)]
    [InlineData("research-reward", true)]
    [InlineData("returned-research", false)]
    [InlineData("returned-research", true)]
    public void FuelDeliveryReservesContactMarkerAndRelaunchesWhenAvailable(string source, bool exhausted)
    {
        var campaign = CreateItemFuelPatrolCampaign(source, exhausted);
        if (exhausted)
        {
            AssertTimeBlocked(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
            return;
        }

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        var after = campaign.Capture();
        var craft = Assert.Single(after.Bases[0].Crafts, candidate => candidate.RuleId == "SHIP_FUEL_ITEM");
        Assert.Equal("STR_OUT", craft.Logistics!.Status);
        // The same tick's ten-minute handler consumes one unit of item-based fuel after launch.
        Assert.Equal(99, craft.Logistics.Fuel);
        var ufo = Assert.Single(after.World.Ufos);
        Assert.True(ufo.Detected);
        Assert.True(ufo.Id > 0);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("late-transfer")]
    [InlineData("unrelated-transfer")]
    [InlineData("unfinished-production")]
    [InlineData("sold-production")]
    [InlineData("delayed-production")]
    [InlineData("unrelated-production")]
    [InlineData("unfinished-research")]
    [InlineData("returned-research-ready")]
    public void GroundedCraftStaysDownWhenNoEarlierHandlerDeliversItsFuel(string source)
    {
        foreach (var boostedRadar in new[] { false, true })
        {
            var campaign = CreateItemFuelPatrolCampaign(source, exhausted: false, boostedRadar);
            var before = campaign.Capture();

            var result = campaign.Execute(new AdvanceCampaignTime(1));

            Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked or UfoContactDetected);
            var after = campaign.Capture();
            Assert.Equal("STR_READY", Assert.Single(after.Bases[0].Crafts,
                candidate => candidate.RuleId == "SHIP_FUEL_ITEM").Logistics!.Status);
            Assert.False(Assert.Single(after.World.Ufos).Detected);
            Assert.Equal(before.NextIds.GetValueOrDefault("STR_UFO"), after.NextIds.GetValueOrDefault("STR_UFO"));
            Assert.Equal(0, Assert.Single(after.World.Ufos).Id);
        }
    }

    [Fact]
    public void ExhaustedContactMarkersBlockWheneverAnUndetectedUfoIsScanned()
    {
        // Preflight reserves a marker for every undetected alerting UFO instead of
        // forecasting radar coverage; only an exhausted ID range can make this block.
        var campaign = CreateItemFuelPatrolCampaign("none", exhausted: true);

        AssertTimeBlocked(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
    }

    /// <summary><paramref name="boostedRadar"/> installs RADAR_EXCESS, whose 101% chance is legal.</summary>
    private static CampaignState CreateItemFuelPatrolCampaign(string source, bool exhausted, bool boostedRadar = false)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var daily = source.Contains("research", StringComparison.Ordinal);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, daily ? 23 : 1, 59, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture().WithCraft("SHIP", craft =>
        {
            var flagged = AutoPatrolling(craft, source == "returned-research" ? "STR_REFUELLING" : "STR_READY", 0);
            return flagged with
            {
                RuleId = "SHIP_FUEL_ITEM",
                Logistics = flagged.Logistics! with
                {
                    Weapons = boostedRadar ? [new CraftWeaponSnapshot("RADAR_EXCESS", 1), null] : [null, null],
                },
            };
        });
        if (exhausted) snapshot = snapshot.WithNextId("STR_UFO", int.MaxValue);
        var owner = snapshot.Bases[0];
        var productionRule = source switch
        {
            "production" or "unfinished-production" or "sold-production" or "fallback-production" => "FUEL_PRODUCTION",
            "random-production" => "FUEL_RANDOM_PRODUCTION",
            "delayed-production" => "FUEL_DELAYED_PRODUCTION",
            "unrelated-production" => "OTHER_PRODUCTION",
            _ => null,
        };
        var researchRule = source switch
        {
            "research" or "unfinished-research" => "FUEL_RESEARCH",
            "research-reward" => "FUEL_RESEARCH_REWARD",
            "returned-research" or "returned-research-ready" => "FUEL_RESEARCH_RETURN",
            _ => null,
        };
        return CampaignState.Restore(UfoNearBase(snapshot with
        {
            Bases = [owner with
            {
                Items = owner.Items.Where(static pair => pair.Key != "SUPPLY")
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
                Transfers = source is "transfer" or "late-transfer" or "unrelated-transfer"
                    ? [new TransferSnapshot(77, source == "late-transfer" ? 2 : 1,
                        CampaignTransferKind.Item, source == "unrelated-transfer" ? "BULKY" : "SUPPLY", 1)] : [],
                Productions = productionRule is null ? [] : [new ProductionSnapshot(productionRule,
                    source == "fallback-production" ? 0 : 1, source == "unfinished-production" ? 0 : 1,
                    1, false, source == "sold-production", source == "fallback-production", new Dictionary<string, int>())],
                Engineers = source == "fallback-production" ? 1 : 0,
                Facilities = [.. owner.Facilities, new FacilitySnapshot("FUEL_WORKSHOP", 1, 0, 0, 0, false, false, false)],
                Research = researchRule is null ? [] : [new ResearchProjectSnapshot(researchRule,
                    1, 0, source == "unfinished-research" ? 2 : 1)],
            }],
        }, untracked: true), content, new SplitMix64RandomSource(89));
    }

    [Fact]
    public void ArrivingAutoPatrolCraftRefuelsRelaunchesAndDetectsInTheSameHour()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = snapshot.Bases[0];
        var incoming = AutoPatrolling(Ship(snapshot), "STR_REFUELLING", 0) with { Id = 4 };
        campaign = CampaignState.Restore(UfoNearBase(snapshot with
        {
            Bases = [owner with
            {
                Transfers = [new TransferSnapshot(77, 1, CampaignTransferKind.Craft, "SHIP", 1,
                    Craft: incoming)],
            }],
        }, untracked: true), content, new SplitMix64RandomSource(90));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        // Transfer::advance checks the craft up at the hour; time30Minutes refuels and
        // relaunches it before updateActiveCrafts, so its radar sees the UFO immediately.
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        var arrived = Assert.Single(campaign.Capture().Bases[0].Crafts, craft => craft.Id == 4).Logistics!;
        Assert.Equal("STR_OUT", arrived.Status);
        Assert.True(arrived.IsAutoPatrolling);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstalledWeaponRadarBonusControlsCraftContact(bool installed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        // SHIP_SLOW_REFUEL has no radar of its own; only the installed weapon's bonus can detect.
        var snapshot = campaign.Capture().WithCraft("SHIP", craft => AirborneRadarCraft(craft,
            installed ? new CraftWeaponSnapshot("RADAR_BOOST", 0) : null));
        campaign = CampaignState.Restore(UfoNearBase(snapshot), content, new SplitMix64RandomSource(86));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(installed, result.Events.Any(item => item is UfoContactDetected));
        Assert.Equal(installed, Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RadarChanceAboveHundredDetectsUnlessTheCraftIsDestroyed(bool destroyed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture().WithCraft("SHIP", craft =>
        {
            var radarCraft = AirborneRadarCraft(craft, new CraftWeaponSnapshot("RADAR_EXCESS", 0));
            return radarCraft with { Logistics = radarCraft.Logistics! with { Damage = destroyed ? 100 : 0 } };
        });
        campaign = CampaignState.Restore(UfoNearBase(snapshot), content, new SplitMix64RandomSource(87));
        if (destroyed)
        {
            // time5Seconds would delete a destroyed craft before it could fly again.
            AssertTimeBlocked(campaign, "Destroyed craft removal requires world simulation.");
            return;
        }
        var before = campaign.Capture();

        // RNG::percent treats any chance of 100 or more as a certain detection.
        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.Equal(3, Assert.Single(campaign.Capture().Regions).ActivityAlien[^1] -
            Assert.Single(before.Regions).ActivityAlien[^1]);
    }

    [Fact]
    public void BaseRadarChanceAboveHundredAlwaysDetects()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        campaign = CampaignState.Restore(UfoNearBase(campaign.Capture().WithFacility("RADAR_OVER_TEST")), content,
            new SplitMix64RandomSource(91));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    private static CraftSnapshot Ship(CampaignSnapshot snapshot) =>
        Assert.Single(snapshot.Bases[0].Crafts, craft => craft.RuleId == "SHIP");

    /// <summary>A grounded craft whose auto-patrol returns to (0.3, 0.1) once it is ready.</summary>
    private static CraftSnapshot AutoPatrolling(CraftSnapshot craft, string status, int fuel) => craft with
    {
        Logistics = craft.Logistics! with
        {
            Status = status,
            Fuel = fuel,
            IsAutoPatrolling = true,
            AutoPatrolLongitude = 0.3,
            AutoPatrolLatitude = 0.1,
        },
    };

    /// <summary>An airborne SHIP_SLOW_REFUEL, which has no radar, at the UFO's position near the base.</summary>
    private static CraftSnapshot AirborneRadarCraft(CraftSnapshot craft, CraftWeaponSnapshot? weapon) => craft with
    {
        RuleId = "SHIP_SLOW_REFUEL",
        Logistics = craft.Logistics! with
        {
            Status = "STR_OUT",
            Longitude = 0.21,
            Latitude = 0.11,
            Weapons = [weapon, null],
        },
    };

    /// <summary>Moves the UFO next to base Alpha; <paramref name="untracked"/> also clears its contact marker.</summary>
    private static CampaignSnapshot UfoNearBase(CampaignSnapshot snapshot, bool untracked = false)
    {
        var ufo = Assert.Single(snapshot.World.Ufos);
        return snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo with { Id = untracked ? 0 : ufo.Id, Longitude = 0.21, Latitude = 0.11 }],
            },
        };
    }
}
