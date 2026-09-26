using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>AlienMission::getLandPoint/ufoReachedWaypoint/ufoLifting and GeoscapeState's ground handlers.</summary>
public sealed class StrategicWorldUfoLandingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AreaLandingRejectsWaterAndFliesToTheSelectedLandAfterReload(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var rules = content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired("REGION")].Value;
        var globe = rules.Campaign.Globe;
        Assert.False(Assert.Single(region.MissionZones[0].Areas).IsPoint);

        // Controlled choices for AlienMission::getLandPoint: prefer real land,
        // reject the first point north of the land polygon, then accept the second.
        // ufoReachedWaypoint discards this whole selection and repeats getWaypoint.
        // Build both candidate sequences independently of the land-selection helper.
        var choices = new SplitMix64RandomSource(604);
        Assert.Equal(53, choices.NextInclusive(0, 99)); // UFO_SCOUT's fake-water chance is 20%.
        var rejected = WorldGeometry.RandomPoint(region, 0, -1, choices);
        var discarded = WorldGeometry.RandomPoint(region, 0, -1, choices);
        Assert.False(WorldGeometry.InsideLand(globe, rejected));
        Assert.True(WorldGeometry.InsideLand(globe, discarded));
        Assert.Equal(62, choices.NextInclusive(0, 99)); // second selection also prefers real land
        var secondRejected = WorldGeometry.RandomPoint(region, 0, -1, choices);
        Assert.False(WorldGeometry.InsideLand(globe, secondRejected));
        var thirdRejected = WorldGeometry.RandomPoint(region, 0, -1, choices);
        Assert.False(WorldGeometry.InsideLand(globe, thirdRejected));
        var fakeWaterRejected = WorldGeometry.RandomPoint(region, 0, -1, choices);
        Assert.True(WorldGeometry.InsideLand(globe, fakeWaterRejected));
        Assert.True(WorldGeometry.InsideFakeUnderwaterTexture(globe, fakeWaterRejected));
        var accepted = WorldGeometry.RandomPoint(region, 0, -1, choices);
        Assert.True(WorldGeometry.InsideLand(globe, accepted));
        Assert.False(WorldGeometry.InsideFakeUnderwaterTexture(globe, accepted));
        Assert.True(WorldGeometry.InsideRegion(region, accepted));

        var snapshot = CreateCampaign(content).Capture();
        var campaign = CampaignState.Restore(snapshot with
        {
            RandomState = 604,
            World = snapshot.World with
            {
                Ufos = [Assert.Single(snapshot.World.Ufos) with { TrajectoryId = "TRAJ_LANDING_AREA" }],
            },
        }, content, new SplitMix64RandomSource(604));

        AdvanceOne(campaign);

        var selected = campaign.Capture();
        var flying = Assert.Single(selected.World.Ufos);
        Assert.Equal(1, flying.TrajectoryPoint);
        Assert.Equal(UfoStatus.Flying, flying.Status);
        Assert.Equal(accepted, flying.Destination!.Position);
        Assert.Equal(choices.State, selected.RandomState);
        campaign = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(selected),
            content, seed: 605, name: "ufo-area-landing.sav").Campaign;
        Assert.Equivalent(selected.World, campaign.Capture().World, strict: true);

        // The selected point is close enough for a short bounded flight.
        // Exercise actual movement and landing after reloading the selected destination.
        for (var tick = 0; tick < 400 && campaign.Capture().World.Ufos[0].Status == UfoStatus.Flying; tick++)
            AdvanceOne(campaign);
        var landed = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(UfoStatus.Landed, landed.Status);
        Assert.Equal(accepted, landed.Position);
        Assert.Equal(10, landed.SecondsRemaining);
        Assert.Equal(40, landed.LandId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FixedPointWaveSpawnsDetectsLandsDepartsAndExpires(bool fromCache, bool longLanding)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var snapshot = CreateCampaign(content, halfHour: true, radar: true).Capture();
        var campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [],
                Missions = [Assert.Single(snapshot.World.Missions) with
                    { RuleId = longLanding ? "MISSION_LONG_LANDING" : "MISSION_LANDING",
                        NextWave = 0, SpawnCountdown = 30, LiveUfos = 0 }],
            },
        }, content, new SplitMix64RandomSource(97));
        var contactSeen = false;
        var landingSeen = false;
        var takeoffSeen = false;
        var departureSeen = false;
        for (var tick = 0; tick < (longLanding ? 721 : 361); tick++)
        {
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            contactSeen |= result.Events.Any(item => item is UfoContactDetected);
            var ufo = campaign.Capture().World.Ufos.SingleOrDefault();
            if (ufo is null) continue;
            if (ufo.Status == UfoStatus.Landed)
            {
                landingSeen = true;
                Assert.True(ufo.Detected);
                Assert.Equal(40, ufo.LandId);
            }
            takeoffSeen |= ufo.Status == UfoStatus.Flying && ufo.Altitude == WorldAltitudes.VeryLow;
            departureSeen |= ufo.Status == UfoStatus.Destroyed;
        }
        Assert.True(contactSeen);
        Assert.True(landingSeen);
        Assert.True(takeoffSeen);
        Assert.True(departureSeen);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(longLanding ? 16 : 10, Assert.Single(campaign.Capture().Regions).ActivityAlien[^1]);
    }

    [Fact]
    public void OrdinaryLandingCycleSurvivesEveryTransitionAndExpiresItsMission()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateCampaign(content);
        var batched = CampaignState.Restore(campaign.Capture(), content, new SplitMix64RandomSource(91));

        StepAndReload();
        Assert.Equal(1, Ufo().TrajectoryPoint);
        Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), Ufo().Destination!.Position);
        StepAndReload();
        Assert.Equal(UfoStatus.Landed, Ufo().Status);
        Assert.Equal(WorldAltitudes.Ground, Ufo().Altitude);
        Assert.Equal(10, Ufo().SecondsRemaining);
        Assert.Equal(40, Ufo().LandId);
        Assert.Equal(0, Ufo().Speed);
        Assert.Equal("STR_NONE_UC", Ufo().Direction);
        var groundPosition = Ufo().Position;
        StepAndReload();
        Assert.Equal(5, Ufo().SecondsRemaining);
        StepAndReload();
        Assert.Equal(UfoStatus.Flying, Ufo().Status);
        Assert.Equal(WorldAltitudes.VeryLow, Ufo().Altitude);
        Assert.Equal(1100, Ufo().Speed);
        Assert.Equal(groundPosition, Ufo().Position);
        Assert.Equal(40, Ufo().LandId);
        Assert.Equal(7, Assert.Single(campaign.Capture().Regions).ActivityAlien[^1]);
        StepAndReload();
        Assert.Equal(3, Ufo().TrajectoryPoint);
        Assert.Equal(0, Ufo().LandId);
        StepAndReload();
        Assert.Equal(UfoStatus.Destroyed, Ufo().Status);
        Assert.Equal(1, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
        StepAndReload();
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.Missions).LiveUfos);

        Assert.Equal(7, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(
            batched.Execute(new AdvanceCampaignTime(7)).Events)).Summary.TickCount);
        Assert.Equivalent(batched.Capture(), campaign.Capture(), strict: true);
        Assert.Equal(353, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(
            campaign.Execute(new AdvanceCampaignTime(353)).Events)).Summary.TickCount);
        Assert.Empty(campaign.Capture().World.Missions);

        UfoSnapshot Ufo() => Assert.Single(campaign.Capture().World.Ufos);
        void StepAndReload()
        {
            AdvanceOne(campaign);
            var snapshot = campaign.Capture();
            campaign = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot),
                content, seed: 92, name: "ufo-landing.sav").Campaign;
            Assert.Equivalent(snapshot.World, campaign.Capture().World, strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingAlertPausesAfterCurrentTickAndPersistsOption(bool enabled)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content).Capture();
        var campaign = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot with
        {
            Options = snapshot.Options with { UfoLandingAlert = enabled },
        }), content, seed: 93, name: "ufo-landing-alert.sav").Campaign;

        var result = campaign.Execute(new AdvanceCampaignTime(7));

        Assert.Equal(enabled, campaign.Options.UfoLandingAlert);
        Assert.Equal(enabled ? 2 : 7, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        if (enabled)
        {
            Assert.Equal(new UfoLanded(9, 40), Assert.IsType<UfoLanded>(result.Events[1]));
            Assert.Equal(10, Assert.Single(campaign.Capture().World.Ufos).SecondsRemaining);
        }
        else Assert.Single(result.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandedHalfHourDetectionAndTakeoffScoreCompose(bool noAlert)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateCampaign(content, landed: true, halfHour: true, radar: true, noAlert: noAlert);

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        Assert.Equal(13, Assert.Single(after.Regions).ActivityAlien[^1]);
        Assert.Equal(13, Assert.Single(after.Countries).ActivityAlien[^1]);
        Assert.True(ufo.Detected);
        Assert.Equal(noAlert ? 0 : 30, ufo.Id);
        Assert.Equal(noAlert ? 0 : 40, ufo.LandId);
        Assert.Equal(!noAlert, result.Events.Any(item => item is UfoContactDetected));
    }

    [Fact]
    public void LandedContactLostWithoutRadarRetainsItsMarker()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: true, halfHour: true).Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var campaign = RestoreUfos(content, snapshot,
            ufo with { Detected = true, HyperDetected = true, LandId = 12, SecondsRemaining = 20 });

        AdvanceOne(campaign);

        var after = Assert.Single(campaign.Capture().World.Ufos);
        Assert.False(after.Detected);
        Assert.False(after.HyperDetected);
        Assert.Equal(12, after.LandId);
        Assert.Equal(15, after.SecondsRemaining);
        Assert.Equal(6, Assert.Single(campaign.Capture().Regions).ActivityAlien[^1]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LandingMarkerCapacityIsCheckedBeforeDetectionOrArrival(bool onGround, bool collision)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: onGround, halfHour: true, radar: true).Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        if (!onGround) ufo = ufo with { TrajectoryPoint = 1, Detected = false };
        snapshot = snapshot with
        {
            NextIds = new Dictionary<string, int>(snapshot.NextIds)
            { ["STR_LANDING_SITE"] = collision ? 40 : int.MaxValue },
        };
        var campaign = collision
            ? RestoreUfos(content, snapshot, ufo, ufo with { UniqueId = 10, LandId = 40 })
            : RestoreUfos(content, snapshot, ufo);

        AssertBlocked(campaign, "UFO landing marker IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoAlertSkipsGroundContactMarkersButStillNeedsAMarkerOnArrival(bool alreadyLanded)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: alreadyLanded, halfHour: true,
            radar: true, noAlert: true).Capture();
        snapshot = snapshot with
        {
            NextIds = new Dictionary<string, int>(snapshot.NextIds)
            { ["STR_UFO"] = int.MaxValue, ["STR_LANDING_SITE"] = int.MaxValue },
        };
        var campaign = RestoreUfos(content, snapshot,
            Assert.Single(snapshot.World.Ufos) with { TrajectoryPoint = 1, Detected = false });

        if (!alreadyLanded)
            AssertBlocked(campaign, "UFO landing marker IDs are exhausted or collide with a saved UFO.");
        else
        {
            AdvanceOne(campaign);
            var ufo = Assert.Single(campaign.Capture().World.Ufos);
            Assert.True(ufo.Detected);
            Assert.Equal(0, ufo.Id);
            Assert.Equal(0, ufo.LandId);
        }
    }

    [Theory]
    [InlineData(false, 1, 6)]
    [InlineData(false, 2, 10)]
    [InlineData(true, 1, 10)]
    public void TakeoffAndHalfHourAwardsCannotOverflowTogether(bool halfHour, int count, int room)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: true, halfHour: halfHour).Capture();
        var region = Assert.Single(snapshot.Regions);
        snapshot = snapshot with { Regions = [region with { ActivityAlien = [int.MaxValue - room] }] };
        var ufo = Assert.Single(snapshot.World.Ufos);
        var campaign = RestoreUfos(content, snapshot,
            [.. Enumerable.Range(0, count).Select(index => ufo with { UniqueId = 9 + index })]);

        AssertBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(0, "STR_GROUND")]
    [InlineData(1, "STR_GROUND")]
    [InlineData(7, "STR_GROUND")]
    [InlineData(5, "STR_HIGH_UC")]
    public void InvalidGroundTimerOrAltitudeStopsBeforeMutation(int seconds, string altitude)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: true).Capture();
        var campaign = RestoreUfos(content, snapshot,
            Assert.Single(snapshot.World.Ufos) with { SecondsRemaining = seconds, Altitude = altitude });

        AssertBlocked(campaign, "Landed UFO timer or altitude is invalid.");
    }

    [Fact]
    public void UnsupportedLandedMissionStopsBeforeMutation()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: true).Capture();
        var campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            { Missions = [Assert.Single(snapshot.World.Missions) with { RuleId = "MISSION_SITE" }] },
        }, content, new SplitMix64RandomSource(94));

        AssertBlocked(campaign, "UFO takeoff requires mission simulation.");
    }

    [Theory]
    [InlineData("TRAJ_LANDING_ZERO")]
    [InlineData("TRAJ_LANDING_OVERFLOW")]
    public void InvalidTrajectoryTimerBlocksBeforeGroundArrival(string trajectory)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content).Capture();
        var campaign = RestoreUfos(content, snapshot,
            Assert.Single(snapshot.World.Ufos) with { TrajectoryId = trajectory });

        AssertBlocked(campaign, "UFO waypoint arrival requires mission simulation.");
    }

    [Theory]
    [InlineData(26)]
    [InlineData(50)]
    public void GroundArrivalThatWouldCrashRemainsGuarded(int damage)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content).Capture();
        var campaign = RestoreUfos(content, snapshot,
            Assert.Single(snapshot.World.Ufos) with { TrajectoryPoint = 1, Damage = damage });

        AssertBlocked(campaign, "UFO waypoint arrival requires mission simulation.");
    }

    [Fact]
    public void TerminalArrivalDefersLaterGroundTimerUntilTheFollowingTick()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content, landed: true).Capture();
        var landed = Assert.Single(snapshot.World.Ufos);
        var terminal = landed with
        {
            UniqueId = 10,
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.VeryLow,
            TrajectoryPoint = 2,
            Speed = 2200,
            Longitude = landed.Destination!.Longitude,
        };
        var campaign = RestoreUfos(content, snapshot, terminal, landed);
        AdvanceOne(campaign);
        Assert.Equal(landed, campaign.Capture().World.Ufos[1]);
        AdvanceOne(campaign);
        var lifted = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(landed.UniqueId, lifted.UniqueId);
        Assert.Equal(UfoStatus.Flying, lifted.Status);
        Assert.Equal(7, Assert.Single(campaign.Capture().Regions).ActivityAlien[^1]);
    }

    [Theory]
    [InlineData(50, "UFO_SCOUT", false)]
    [InlineData(25, "UFO_SCOUT", true)]
    [InlineData(25, "UFO_SPAWN", false)]
    public void ForcedWaterDestinationUsesOneTickUnlessFakeWaterLandingIsAllowed(
        double longitude, string ruleId, bool allowed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateCampaign(content).Capture();
        var ufo = Assert.Single(snapshot.World.Ufos);
        var campaign = RestoreUfos(content, snapshot, ufo with
        {
            RuleId = ruleId,
            TrajectoryPoint = 1,
            Longitude = Degrees(longitude),
            Destination = ufo.Destination! with { Longitude = Degrees(longitude) },
        });

        AdvanceOne(campaign);
        var landed = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(UfoStatus.Landed, landed.Status);
        Assert.Equal(allowed ? 10 : 5, landed.SecondsRemaining);
        Assert.Equal(allowed ? 40 : 0, landed.LandId);
        AdvanceOne(campaign);
        Assert.Equal(allowed ? UfoStatus.Landed : UfoStatus.Flying,
            Assert.Single(campaign.Capture().World.Ufos).Status);
    }

    private static CampaignState CreateCampaign(RuntimeContent content, bool landed = false,
        bool halfHour = false, bool radar = false, bool noAlert = false)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "UFO landing", CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        var longitude = Degrees(landed ? 8 : 8.02);
        var latitude = Degrees(2);
        var ufo = new UfoSnapshot(9, noAlert ? "UFO_LANDING_SILENT" : "UFO_SCOUT", 4,
            landed ? "TRAJ_LANDING_EARLY" : "TRAJ_LANDING", landed ? 1 : 0,
            longitude, latitude, landed ? UfoStatus.Landed : UfoStatus.Flying,
            landed ? WorldAltitudes.Ground : WorldAltitudes.High)
        {
            Id = landed || noAlert ? 0 : 3,
            Speed = landed ? 0 : 2200,
            SecondsRemaining = landed ? 5 : 0,
            Detected = !landed,
            MissionWaveNumber = 0,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, landed ? Degrees(8.01) : longitude, latitude),
        };
        return CampaignState.Restore(snapshot with
        {
            MonthsPassed = 0,
            Time = new CampaignTime(1, 1, 1, 1999, 1, halfHour ? 29 : 0, halfHour ? 55 : 0),
            NextIds = new Dictionary<string, int>(snapshot.NextIds) { ["STR_UFO"] = 30, ["STR_LANDING_SITE"] = 40 },
            Bases = [radar ? owner with { Facilities = [.. owner.Facilities,
                new FacilitySnapshot("RADAR_HYPER_TEST", 1, 0, 0, 0, false, false, false)] } : owner],
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_LANDING", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [ufo],
            },
        }, content, new SplitMix64RandomSource(90));
    }

    private static CampaignState RestoreUfos(RuntimeContent content, CampaignSnapshot snapshot, params UfoSnapshot[] ufos) =>
        CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = ufos,
                Missions = [Assert.Single(snapshot.World.Missions) with { LiveUfos = ufos.Length }],
            },
        }, content, new SplitMix64RandomSource(96));

    private static double Degrees(double value) => value * Math.PI / 180;

    private static void AdvanceOne(CampaignState campaign) => Assert.Equal(1,
        Assert.IsType<CampaignTimeAdvanced>(Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events)).Summary.TickCount);

    private static void AssertBlocked(CampaignState campaign, string reason)
    {
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(reason, Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
