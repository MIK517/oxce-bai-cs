using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>Ufo::think and GeoscapeState::time5Seconds before mission arrival.</summary>
public sealed class StrategicWorldUfoTransitTests
{
    [Fact]
    public void RestoredUfoMovesThroughThreeReferenceStepsAndSaveReload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        var row = Assert.Single(TestFixtures.Rows(oracle.RootElement, "movement"),
            candidate => candidate[0].GetInt32() == 3200);

        AssertStep(campaign, row, 0);
        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            content, seed: 41, name: "ufo-transit.sav").Campaign;
        AssertStep(reloaded, row, 1);
        AssertStep(reloaded, row, 2);
        Assert.Equal(0, Assert.Single(reloaded.Capture().World.Ufos).Shield);

        static void AssertStep(CampaignState current, System.Text.Json.JsonElement expected, int step)
        {
            var result = current.Execute(new AdvanceCampaignTime(1));
            Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events)).Summary.TickCount);
            var ufo = Assert.Single(current.Capture().World.Ufos);
            Assert.InRange(Math.Abs(expected[5 + step * 2].GetDouble() - ufo.Longitude), 0, 1e-12);
            Assert.InRange(Math.Abs(expected[6 + step * 2].GetDouble() - ufo.Latitude), 0, 1e-12);
            Assert.Equal("STR_SOUTH_EAST", ufo.Direction);
        }
    }

    [Fact]
    public void UnsupportedArrivalStopsWhileOrdinaryHalfHourTransitScoresActivity()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var near = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo with { Longitude = 0.99999, Latitude = 0.6 }],
            },
        }, content, new SplitMix64RandomSource(42));
        AssertBlockedWithoutMutation(near, "UFO waypoint arrival requires mission simulation.");

        var boundary = CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        }, content, new SplitMix64RandomSource(43));
        var before = boundary.Capture();

        var elapsed = boundary.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
        Assert.Equal(0, Assert.Single(boundary.Capture().World.Ufos).TrajectoryPoint);
        Assert.NotEqual(ufo.Position, Assert.Single(boundary.Capture().World.Ufos).Position);
        Assert.Equal(before.World.Missions[0].SpawnCountdown,
            Assert.Single(boundary.Capture().World.Missions).SpawnCountdown);
    }

    [Fact]
    public void OrdinaryUfoCrossesTwoTenMinuteBoundariesWithoutRetargeting()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 9, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var restored = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            content, seed: 67, name: "ufo-ten-minute.sav").Campaign;
        var before = restored.Capture();
        var ufo = Assert.Single(before.World.Ufos);
        Assert.Equal("UFO_SCOUT", ufo.RuleId);
        Assert.False(ufo.HunterKiller);
        var expected = WorldGeometry.Move(ufo.Position, ufo.Destination!.Position,
            WorldGeometry.RadianSpeed(ufo.Speed));

        var first = restored.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(first.Events)).Summary.TickCount);
        Assert.Equal(new CampaignTime(1, 1, 1, 1999, 1, 10, 0), restored.Capture().Time);
        Assert.Equal(expected, Assert.Single(restored.Capture().World.Ufos).Position);
        Assert.Equal(before.World.Missions, restored.Capture().World.Missions);
        Assert.Equal(before.RandomState, restored.Capture().RandomState);
        var firstDistance = WorldGeometry.Distance(expected, ufo.Destination.Position);

        var second = restored.Execute(new AdvanceCampaignTime(120));

        Assert.Equal(120, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(second.Events)).Summary.TickCount);
        Assert.Equal(new CampaignTime(1, 1, 1, 1999, 1, 20, 0), restored.Capture().Time);
        var after = Assert.Single(restored.Capture().World.Ufos);
        Assert.False(after.Detected);
        var distanceAdvanced = firstDistance - WorldGeometry.Distance(after.Position, ufo.Destination.Position);
        var step = WorldGeometry.RadianSpeed(ufo.Speed);
        Assert.InRange(distanceAdvanced, 119 * step, 121 * step);
        Assert.Equal(before.RandomState, restored.Capture().RandomState);
    }

    [Fact]
    public void PreviouslyDetectedOrdinaryUfoLosesContactAndScoresAtHalfHour()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo with { Detected = true, HyperDetected = true, Id = 7 }],
            },
        }, content, new SplitMix64RandomSource(81));
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events)).Summary.TickCount);
        var after = campaign.Capture();
        var moved = Assert.Single(after.World.Ufos);
        Assert.False(moved.Detected);
        Assert.False(moved.HyperDetected);
        Assert.Equal(7, moved.Id);
        Assert.Equal(3, Assert.Single(after.Regions).ActivityAlien[^1] -
            Assert.Single(before.Regions).ActivityAlien[^1]);
        Assert.Equal(3, Assert.Single(after.Countries).ActivityAlien[^1] -
            Assert.Single(before.Countries).ActivityAlien[^1]);
    }

    [Fact]
    public void InstantRetaliationUfoSkipsHalfHourScoringAndDetection()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200,
            missionRuleId: "MISSION_INSTANT_RETALIATION", missionWaveNumber: 0);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        campaign = CampaignState.Restore(snapshot with
        {
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities,
                    new FacilitySnapshot("RADAR_HYPER_TEST", 1, 0, 0, 0, false, false, false)],
            }],
        }, content, new SplitMix64RandomSource(83));
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events)).Summary.TickCount);
        var after = campaign.Capture();
        Assert.NotEqual(Assert.Single(before.World.Ufos).Position, Assert.Single(after.World.Ufos).Position);
        Assert.False(Assert.Single(after.World.Ufos).Detected);
        Assert.Equal(Assert.Single(before.Regions).ActivityAlien, Assert.Single(after.Regions).ActivityAlien);
        Assert.Equal(Assert.Single(before.Countries).ActivityAlien, Assert.Single(after.Countries).ActivityAlien);
    }

    [Theory]
    [InlineData("STR_READY")]
    [InlineData("STR_REFUELLING")]
    public void GroundedAutoPatrolFlagDoesNotBlockUfoDetection(string status)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var craftRuleId = status == "STR_REFUELLING" ? "SHIP_SLOW_REFUEL" : "SHIP";
        var flagged = craft with
        {
            RuleId = craftRuleId,
            Logistics = craft.Logistics! with
            {
                Status = status,
                Fuel = status == "STR_REFUELLING" ? 0 : craft.Logistics!.Fuel,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            },
        };
        campaign = CampaignState.Restore(snapshot with
        {
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(candidate => candidate.RuleId == "SHIP" ? flagged : candidate)],
                Facilities = [.. owner.Facilities,
                    new FacilitySnapshot("RADAR_HYPER_TEST", 1, 0, 0, 0, false, false, false)],
            }],
        }, content, new SplitMix64RandomSource(84));

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
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var flagged = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 0,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            },
        };
        campaign = CampaignState.Restore(snapshot with
        {
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(candidate => candidate.RuleId == "SHIP" ? flagged : candidate)],
            }],
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with { Longitude = 0.21, Latitude = 0.11 }],
            },
        }, content, new SplitMix64RandomSource(85));

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
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var flagged = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_REARMING",
                Fuel = 0,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            },
        };
        var ids = snapshot.NextIds.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal);
        ids["STR_UFO"] = int.MaxValue;
        campaign = CampaignState.Restore(snapshot with
        {
            NextIds = ids,
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(candidate => candidate.RuleId == "SHIP" ? flagged : candidate)],
            }],
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with
                {
                    Id = 0, Longitude = 0.21, Latitude = 0.11,
                }],
            },
        }, content, new SplitMix64RandomSource(88));

        AssertBlockedWithoutMutation(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
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
            AssertBlockedWithoutMutation(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
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
    public void GroundedCraftDoesNotGateRadarWhenFuelCannotRelaunchIt(string source)
    {
        foreach (var invalidRadar in new[] { false, true })
        {
            var campaign = CreateItemFuelPatrolCampaign(source, exhausted: true, invalidRadar);

            var result = campaign.Execute(new AdvanceCampaignTime(1));

            Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked or UfoContactDetected);
            var after = campaign.Capture();
            Assert.Equal("STR_READY", Assert.Single(after.Bases[0].Crafts,
                candidate => candidate.RuleId == "SHIP_FUEL_ITEM").Logistics!.Status);
            Assert.False(Assert.Single(after.World.Ufos).Detected);
            Assert.Equal(int.MaxValue, after.NextIds["STR_UFO"]);
        }
    }

    private static CampaignState CreateItemFuelPatrolCampaign(string source, bool exhausted, bool invalidRadar = false)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var daily = source.Contains("research", StringComparison.Ordinal);
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, daily ? 23 : 1, 59, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var flagged = craft with
        {
            RuleId = "SHIP_FUEL_ITEM",
            Logistics = craft.Logistics! with
            {
                Status = source == "returned-research" ? "STR_REFUELLING" : "STR_READY",
                Fuel = 0,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
                Weapons = invalidRadar ? [new CraftWeaponSnapshot("RADAR_EXCESS", 1), null] : [null, null],
            },
        };
        var ids = snapshot.NextIds.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal);
        if (exhausted) ids["STR_UFO"] = int.MaxValue;
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
        return CampaignState.Restore(snapshot with
        {
            NextIds = ids,
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(candidate => candidate.RuleId == "SHIP" ? flagged : candidate)],
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
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with
                {
                    Id = 0, Longitude = 0.21, Latitude = 0.11,
                }],
            },
        }, content, new SplitMix64RandomSource(89));
    }

    [Fact]
    public void ArrivingAutoPatrolCraftRetainsTransferGateBeforeMutation()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var incoming = craft with
        {
            Id = 4,
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 0,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = 0.1,
            },
        };
        var ids = snapshot.NextIds.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal);
        ids["STR_UFO"] = int.MaxValue;
        campaign = CampaignState.Restore(snapshot with
        {
            NextIds = ids,
            Bases = [owner with
            {
                Transfers = [new TransferSnapshot(77, 1, CampaignTransferKind.Craft, "SHIP", 1,
                    Craft: incoming)],
            }],
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with
                {
                    Id = 0, Longitude = 0.21, Latitude = 0.11,
                }],
            },
        }, content, new SplitMix64RandomSource(90));

        AssertBlockedWithoutMutation(campaign, "Craft auto-patrol requires world simulation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstalledWeaponRadarBonusControlsCraftContact(bool installed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var radarCraft = craft with
        {
            RuleId = "SHIP_SLOW_REFUEL",
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Longitude = 0.21,
                Latitude = 0.11,
                Weapons = installed ? [new CraftWeaponSnapshot("RADAR_BOOST", 0), null] : [null, null],
            },
        };
        campaign = CampaignState.Restore(snapshot with
        {
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(candidate => candidate.RuleId == "SHIP" ? radarCraft : candidate)],
            }],
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with { Longitude = 0.21, Latitude = 0.11 }],
            },
        }, content, new SplitMix64RandomSource(86));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(installed, result.Events.Any(item => item is UfoContactDetected));
        Assert.Equal(installed, Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidCraftRadarStopsBeforeScoringOnlyWhenTheCraftIsActive(bool destroyed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            ufoRuleId: "UFO_SCOUT", speed: 2200);
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var craft = Assert.Single(owner.Crafts, candidate => candidate.RuleId == "SHIP");
        var radarCraft = craft with
        {
            RuleId = "SHIP_SLOW_REFUEL",
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Damage = destroyed ? 100 : 0,
                Longitude = 0.21,
                Latitude = 0.11,
                Weapons = [new CraftWeaponSnapshot("RADAR_EXCESS", 0), null],
            },
        };
        campaign = CampaignState.Restore(snapshot with
        {
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(candidate => candidate.RuleId == "SHIP" ? radarCraft : candidate)],
            }],
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with { Longitude = 0.21, Latitude = 0.11 }],
            },
        }, content, new SplitMix64RandomSource(87));
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        if (!destroyed)
        {
            Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            Assert.Equal("Craft radar stats are outside the supported range.",
                Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
        else
        {
            Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            Assert.DoesNotContain(result.Events, item => item is UfoContactDetected);
            Assert.False(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Assert.Equal(3, Assert.Single(campaign.Capture().Regions).ActivityAlien[^1] -
                Assert.Single(before.Regions).ActivityAlien[^1]);
        }
    }

    [Fact]
    public void AirborneWaypointArrivalUsesSavedTrajectoryAndContinuesAfterReload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0),
            ufoRuleId: "UFO_SCOUT", speed: 2200, missionRuleId: "MISSION_AIRBORNE_FLAGS",
            trajectoryId: "TRAJ_AIRBORNE", missionWaveNumber: 0);
        var baseId = campaign.Capture().Bases[0].Id;
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.5, 0.1)).Events));
        var snapshot = campaign.Capture();
        var source = Assert.Single(snapshot.World.Ufos);
        var sourceDestination = source.Destination!;
        var mission = Assert.Single(snapshot.World.Missions);
        var missionRule = content.RuntimeRules.AlienMissions[
            content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
        var currentWave = missionRule.Waves[source.MissionWaveNumber];
        var savedTrajectory = content.RuntimeRules.UfoTrajectories[
            content.RuntimeRules.UfoTrajectories.GetRequired(source.TrajectoryId)].Value;
        Assert.True(currentWave.Objective);
        Assert.True(currentWave.ObjectiveOnTheLandingSite);
        Assert.True(currentWave.ObjectiveOnXcomBase);
        Assert.NotEqual(currentWave.TrajectoryId, source.TrajectoryId);
        Assert.NotEqual(missionRule.SpawnZone, savedTrajectory.Zone(1));
        var craftBefore = Assert.Single(snapshot.Bases[0].Crafts,
            craft => craft.RuleId == "SHIP").Logistics!;
        var arriving = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [mission with { MissionSiteZoneArea = 0 }],
                Ufos = [source with
                {
                    Longitude = sourceDestination.Longitude,
                    Latitude = sourceDestination.Latitude,
                    LandId = 7,
                }],
            },
        }, content, new SplitMix64RandomSource(69));
        var before = arriving.Capture();
        var expectedRandom = new SplitMix64RandomSource(before.RandomState);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        var rawDestination = WorldGeometry.RandomPoint(region, 3, -1, expectedRandom);
        var expectedDestination = WorldPosition.Create(rawDestination.Longitude, rawDestination.Latitude);
        var expectedSpeedRadian = WorldGeometry.RadianSpeed(1760);
        var expectedVector = WorldGeometry.SpeedVector(sourceDestination.Position,
            expectedDestination, expectedSpeedRadian);

        var arrival = arriving.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(arrival.Events)).Summary.TickCount);
        var afterArrival = arriving.Capture();
        var ufo = Assert.Single(afterArrival.World.Ufos);
        Assert.Equal(1, ufo.TrajectoryPoint);
        Assert.Equal(WorldAltitudes.Low, ufo.Altitude);
        Assert.Equal(1760, ufo.Speed);
        Assert.Equal(0, ufo.LandId);
        Assert.Equal(sourceDestination.Position, ufo.Position);
        Assert.Equal(expectedDestination, ufo.Destination!.Position);
        Assert.True(rawDestination.Longitude >= 2 * Math.PI);
        Assert.True(ufo.Destination.Longitude < 2 * Math.PI);
        Assert.Equal(expectedSpeedRadian, ufo.SpeedRadian);
        Assert.Equal(expectedVector.Longitude, ufo.SpeedLongitude);
        Assert.Equal(expectedVector.Latitude, ufo.SpeedLatitude);
        Assert.Equal(WorldAltitudes.Direction(expectedVector.Longitude, expectedVector.Latitude), ufo.Direction);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        Assert.Equal(0, ufo.Shield);
        Assert.Equal(expectedRandom.State, afterArrival.RandomState);
        Assert.Equal(before.World.Missions, afterArrival.World.Missions);
        Assert.Equal(craftBefore.Takeoff - 1, Assert.Single(afterArrival.Bases[0].Crafts,
            craft => craft.RuleId == "SHIP").Logistics!.Takeoff);

        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(afterArrival),
            content, seed: 70, name: "ufo-airborne-waypoint.sav").Campaign;
        var expectedPosition = WorldGeometry.Move(ufo.Position, expectedDestination,
            WorldGeometry.RadianSpeed(ufo.Speed));

        var movement = reloaded.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(movement.Events)).Summary.TickCount);
        Assert.Equal(expectedPosition, Assert.Single(reloaded.Capture().World.Ufos).Position);
    }

    [Fact]
    public void ObjectiveWaveArrivalInMissionSpawnZoneRemainsBlocked()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0),
            ufoRuleId: "UFO_SCOUT", speed: 2200, missionRuleId: "MISSION_AIRBORNE_FLAGS",
            trajectoryId: "TRAJ_AIRBORNE_SPAWN", missionWaveNumber: 0);
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions);
        var ufo = Assert.Single(snapshot.World.Ufos);
        var destination = ufo.Destination!;
        var arriving = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [mission with { MissionSiteZoneArea = 0 }],
                Ufos = [ufo with
                {
                    Longitude = destination.Longitude,
                    Latitude = destination.Latitude,
                }],
            },
        }, content, new SplitMix64RandomSource(73));

        AssertBlockedWithoutMutation(arriving, "UFO waypoint arrival requires mission simulation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterTrajectoryPointStillBlocksTenMinuteBaseDetection(bool destroyed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 9, 55));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo with
                {
                    TrajectoryPoint = 2,
                    Status = destroyed ? UfoStatus.Destroyed : UfoStatus.Flying,
                }],
            },
        }, content, new SplitMix64RandomSource(68));

        AssertBlockedWithoutMutation(restored, "UFO detection and retargeting require world simulation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalOrInterruptedWaypointArrivalDefersCleanupAndCraftMovement(bool interrupted)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var baseId = campaign.Capture().Bases[0].Id;
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.5, 0.1)).Events));
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions);
        var ufo = Assert.Single(snapshot.World.Ufos);
        var trajectory = content.RuntimeRules.UfoTrajectories[
            content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        var arriving = ufo with
        {
            Longitude = 0.99999,
            Latitude = 0.6,
            TrajectoryPoint = interrupted ? 0 : trajectory.Waypoints.Count - 1,
            Detected = true,
        };
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [mission with { Interrupted = interrupted }],
                Ufos = [arriving],
            },
        }, content, new SplitMix64RandomSource(56));
        var continuous = CampaignState.Restore(restored.Capture(), content,
            new SplitMix64RandomSource(59));
        var craftBefore = Assert.Single(restored.Capture().Bases[0].Crafts,
            craft => craft.RuleId == "SHIP").Logistics!;

        var arrivalTick = restored.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(arrivalTick.Events)).Summary.TickCount);
        var afterArrival = restored.Capture();
        var stopped = Assert.Single(afterArrival.World.Ufos);
        Assert.Equal(UfoStatus.Destroyed, stopped.Status);
        Assert.Equal(1.0, stopped.Longitude);
        Assert.Equal(0.6, stopped.Latitude);
        Assert.Equal(0, stopped.Speed);
        Assert.Equal("STR_NONE_UC", stopped.Direction);
        Assert.False(stopped.Detected);
        Assert.Equal(-1, stopped.Shield);
        Assert.Equal(1, Assert.Single(afterArrival.World.Missions).LiveUfos);
        Assert.Equal(craftBefore, Assert.Single(afterArrival.Bases[0].Crafts,
            craft => craft.RuleId == "SHIP").Logistics);
        Assert.Single(afterArrival.World.Waypoints);

        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(afterArrival),
            content, seed: 57, name: "ufo-final-arrival.sav").Campaign;
        var cleanupTick = reloaded.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(cleanupTick.Events)).Summary.TickCount);
        Assert.Empty(reloaded.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(reloaded.Capture().World.Missions).LiveUfos);
        Assert.Equal(craftBefore.Takeoff - 1, Assert.Single(reloaded.Capture().Bases[0].Crafts,
            craft => craft.RuleId == "SHIP").Logistics!.Takeoff);

        var twoTicks = continuous.Execute(new AdvanceCampaignTime(2));
        Assert.Equal(2, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(twoTicks.Events)).Summary.TickCount);
        Assert.Empty(continuous.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(continuous.Capture().World.Missions).LiveUfos);
        Assert.Equal(craftBefore.Takeoff - 1, Assert.Single(continuous.Capture().Bases[0].Crafts,
            craft => craft.RuleId == "SHIP").Logistics!.Takeoff);
    }

    [Fact]
    public void FinalArrivalStopsProcessingLaterUfosInTheSameTick()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var lastPoint = content.RuntimeRules.UfoTrajectories[
            content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value.Waypoints.Count - 1;
        var arriving = ufo with { Longitude = 0.99999, Latitude = 0.6, TrajectoryPoint = lastPoint };
        var later = ufo with { UniqueId = 10, Id = 4 };
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [Assert.Single(snapshot.World.Missions) with { LiveUfos = 2 }],
                Ufos = [arriving, later],
            },
        }, content, new SplitMix64RandomSource(58));

        restored.Execute(new AdvanceCampaignTime(1));

        var after = restored.Capture();
        Assert.Equal(UfoStatus.Destroyed, after.World.Ufos[0].Status);
        Assert.Equal(later.Position, after.World.Ufos[1].Position);
        Assert.Equal(2, Assert.Single(after.World.Missions).LiveUfos);
    }

    [Fact]
    public void LaterNonterminalArrivalBlocksBeforeAnEarlierTerminalArrival()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var lastPoint = content.RuntimeRules.UfoTrajectories[
            content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value.Waypoints.Count - 1;
        var arriving = ufo with { Longitude = 0.99999, Latitude = 0.6, TrajectoryPoint = lastPoint };
        var later = ufo with { UniqueId = 10, Id = 4, Longitude = 0.99999, Latitude = 0.6 };
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [Assert.Single(snapshot.World.Missions) with { LiveUfos = 2 }],
                Ufos = [arriving, later],
            },
        }, content, new SplitMix64RandomSource(62));

        AssertBlockedWithoutMutation(restored, "UFO waypoint arrival requires mission simulation.");
    }

    [Theory]
    [InlineData("landed", "UFO state requires world simulation.")]
    [InlineData("hunter", "UFO state requires world simulation.")]
    [InlineData("shield", "UFO shield handling requires world simulation.")]
    [InlineData("speed", "UFO speed is invalid.")]
    public void UnsupportedUfoStateReportsItsPreflightReason(string condition, string reason)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        ufo = condition switch
        {
            "landed" => ufo with { Status = UfoStatus.Landed },
            "hunter" => ufo with { HunterKiller = true },
            "shield" => ufo with { Shield = 1 },
            "speed" => ufo with { Speed = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Ufos = [ufo] },
        }, content, new SplitMix64RandomSource(51));

        AssertBlockedWithoutMutation(restored, reason);
    }

    [Fact]
    public void RaceDerivedShieldCapacityReportsItsPreflightReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions);
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Missions = [mission with { Race = "RACE_B" }] },
        }, content, new SplitMix64RandomSource(63));

        AssertBlockedWithoutMutation(restored, "UFO shield handling requires world simulation.");
    }

    [Fact]
    public void MissingMissionLinkIsRejectedForOrdinarySave()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);

        var error = Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Ufos = [ufo with { MissionId = 999 }] },
        }, content, new SplitMix64RandomSource(52)));
        Assert.Equal("Unknown UFO mission; the save is corrupt.", error.Message);
    }

    [Fact]
    public void MissionWaveOutsideRestoredRuleIsRejectedDuringRestore()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var mission = Assert.Single(snapshot.World.Missions);
        var waveCount = content.RuntimeRules.AlienMissions[
            content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value.Waves.Count;

        var error = Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Ufos = [ufo with { MissionWaveNumber = waveCount }] },
        }, content, new SplitMix64RandomSource(71)));

        Assert.Equal("UFO mission wave is outside its mission rule.", error.Message);
    }

    [Fact]
    public void InvalidRegionalWaypointIsRejectedBeforeTimeOrRandomStateChanges()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-invalid-area.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0),
            ufoRuleId: "UFO_SCOUT", speed: 2200, missionRuleId: "MISSION_INVALID_AREA",
            trajectoryId: "TRAJ_INVALID_AREA", missionWaveNumber: 0);
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var destination = ufo.Destination!;
        var arriving = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo with
                {
                    Longitude = destination.Longitude,
                    Latitude = destination.Latitude,
                }],
            },
        }, content, new SplitMix64RandomSource(72));
        var before = arriving.Capture();

        var error = Assert.Throws<InvalidDataException>(() => arriving.Execute(new AdvanceCampaignTime(1)));

        Assert.Equal("A mission area can generate an invalid UFO waypoint.", error.Message);
        Assert.Equivalent(before, arriving.Capture(), strict: true);
    }

    [Fact]
    public void MissingMissionLinkInPreCampaignStateReportsItsPreflightReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var restored = CampaignState.Restore(snapshot with
        {
            MonthsPassed = -1,
            World = snapshot.World with { Ufos = [ufo with { MissionId = 999 }] },
        }, content, new SplitMix64RandomSource(53));

        AssertBlockedWithoutMutation(restored, "UFO mission link requires world simulation.");
    }

    [Fact]
    public void DestroyedUfoReleasesItsMissionAndCompletedMissionExpires()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var destroyed = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Ufos = [ufo with { Status = UfoStatus.Destroyed }] },
        }, content, new SplitMix64RandomSource(47));

        destroyed.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(destroyed.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(destroyed.Capture().World.Missions).LiveUfos);

        destroyed.Execute(new AdvanceCampaignTime(359));
        Assert.Empty(destroyed.Capture().World.Missions);

        var malformed = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [Assert.Single(snapshot.World.Missions) with { LiveUfos = 0 }],
                Ufos = [ufo with { Status = UfoStatus.Destroyed }],
            },
        }, content, new SplitMix64RandomSource(48));
        AssertBlockedWithoutMutation(malformed, "Destroyed UFO count exceeds its mission's live count.");
    }

    [Fact]
    public void TerminalArrivalRechecksRestoredMissionLiveCountBeforeCleanup()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var snapshot = campaign.Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var lastPoint = content.RuntimeRules.UfoTrajectories[
            content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value.Waypoints.Count - 1;
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [Assert.Single(snapshot.World.Missions) with { LiveUfos = 0 }],
                Ufos = [ufo with { Longitude = 0.99999, Latitude = 0.6, TrajectoryPoint = lastPoint }],
            },
        }, content, new SplitMix64RandomSource(64));

        var arrival = restored.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(arrival.Events)).Summary.TickCount);
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(restored.Capture().World.Ufos).Status);
        AssertBlockedWithoutMutation(restored, "Destroyed UFO count exceeds its mission's live count.");
    }

    private static void AssertBlockedWithoutMutation(CampaignState campaign, string reason)
    {
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(reason, Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static CampaignState CreateTransitCampaign(RuntimeContent content, CampaignTime time,
        string ufoRuleId = "UFO_HUNTER", int speed = 3200, string missionRuleId = "MISSION_SCOUT",
        string trajectoryId = "TRAJ_PATROL", int missionWaveNumber = 1)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "UFO transit", CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        var mission = new AlienMissionSnapshot(
            4, missionRuleId, "REGION", "RACE_A", missionWaveNumber + 1, 0, 1500, 1, -1);
        var ufo = new UfoSnapshot(9, ufoRuleId, 4, trajectoryId, 0,
            0.4, 0.2, UfoStatus.Flying, "STR_HIGH_UC")
        {
            Id = 3,
            Speed = speed,
            MissionWaveNumber = missionWaveNumber,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, 1.0, 0.6),
        };
        return CampaignState.Restore(snapshot with
        {
            MonthsPassed = 0,
            Time = time,
            World = snapshot.World with { Missions = [mission], Ufos = [ufo] },
        }, content, new SplitMix64RandomSource(40));
    }
}
