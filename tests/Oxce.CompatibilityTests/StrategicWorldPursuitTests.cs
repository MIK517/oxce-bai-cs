using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldPursuitTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FuelReturnPrecedesGuardedArrivalInTheSameTick(bool fromCache, bool landed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var snapshot = landed ? CreateLifecycleSnapshot(content) : PursuitCampaign(content).Capture();
        var ufo = snapshot.World.Ufos[0] with { Detected = true, Speed = 0 };
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 9, 55),
            World = snapshot.World with { Ufos = [ufo] },
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 1,
                LowFuel = false,
                Speed = 6000,
                Longitude = ufo.Longitude - 0.00001,
                Latitude = ufo.Latitude,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, ufo.Longitude, ufo.Latitude)
                { UniqueId = ufo.UniqueId },
            },
        });
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with { Soldiers =
            [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } },
                .. owner.Soldiers.Skip(1)] }, .. snapshot.Bases.Skip(1)]
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        campaign = Reload(content, campaign, 92, "fuel-arrival.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(12));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Single(result.Events.OfType<CraftLowFuel>());
        Assert.Equal(WorldTargetKind.Base, Ship(campaign.Capture()).Destination!.Kind);
        Assert.True(Ship(campaign.Capture()).LowFuel);
        Assert.Equal(0, Ship(campaign.Capture()).Fuel);
        Reload(content, campaign, 92, "fuel-return.sav");
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(55, 300)]
    public void ArrivalStillBlocksWhenFuelReturnIsNotDue(int second, int fuel)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = PursuitCampaign(content).Capture();
        var ufo = snapshot.World.Ufos[0] with { Speed = 0, Longitude = 0.20001, Latitude = 0.1 };
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 9, second),
            World = snapshot.World with { Ufos = [ufo] },
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Fuel = fuel } });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        AssertTimeBlocked(campaign, "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PursuitMovesAfterUfoAndMatchesReferenceAcrossReloads(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var campaign = PursuitCampaign(content);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        var actual = new List<double[]>();
        var expected = TestFixtures.Rows(oracle.RootElement, "pursuit").Select(row =>
            row.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
        foreach (var _ in expected)
        {
            AdvanceOne(campaign);
            var snapshot = campaign.Capture();
            var ufo = Assert.Single(snapshot.World.Ufos);
            var craft = Ship(snapshot);
            actual.Add([Math.Round(ufo.Longitude, 12), Math.Round(ufo.Latitude, 12),
                Math.Round(craft.Longitude, 12), Math.Round(craft.Latitude, 12)]);
            Assert.Equal(ufo.Longitude, craft.Destination!.Longitude);
            Assert.Equal(9, craft.Destination.UniqueId);
            campaign = Reload(content, campaign, 92, "pursuit.sav");
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hidden")]
    [InlineData("marker-only")]
    [InlineData("self")]
    public void DispatchRejectsStaleHiddenOrAmbiguousTargetsWithoutMutation(string variant)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = PursuitCampaign(content);
        var before = campaign.Capture();
        var target = Ship(before).Destination!;
        if (variant == "hidden")
        {
            campaign = CampaignState.Restore(before with
            {
                World = before.World with
                { Ufos = [before.World.Ufos[0] with { Detected = false }] }
            }, content, new SplitMix64RandomSource(92));
        }
        target = variant switch
        {
            "missing" => target with { UniqueId = 99 },
            "marker-only" => target with { UniqueId = 0 },
            "self" => new(WorldTargetKind.Craft, "SHIP", 1, 0, 0),
            _ => target,
        };
        before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new DispatchCraftToTarget(before.Bases[0].Id, "SHIP", 1, target)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void StableIdentitySurvivesLandingMarkerChangeAndQueryIncludesPlayerState()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with { Detected = true, LandId = 42, SecondsRemaining = 100 };
        snapshot = snapshot with { World = snapshot.World with { Ufos = [ufo] } };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Status = "STR_READY" } });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        var query = campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld();
        var target = Assert.Single(query.Targets);
        Assert.Equal(42, target.Id);
        Assert.Equal(9, target.Reference.UniqueId);
        Assert.Equal(3, target.Reference.Id);
        Assert.Contains("branch 5", target.UnavailableActionReason);
        Assert.Contains(query.Bases, owner => owner.Id == snapshot.Bases[0].Id && owner.Name == "Alpha");
        Assert.Null(Assert.Single(query.Crafts, craft => craft.TypeId == "SHIP").DispatchUnavailableReason);
        var result = campaign.Execute(new DispatchCraftToTarget(snapshot.Bases[0].Id, "SHIP", 1, target.Reference));
        Assert.IsType<CraftDestinationChanged>(Assert.Single(result.Events));
        Assert.Equal(9, Ship(campaign.Capture()).Destination!.UniqueId);
        Assert.Equal(60, Ship(campaign.Capture()).Takeoff);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterceptionGuardStopsBeforeAnyTickMutation(bool takingOff)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = PursuitCampaign(content);
        var snapshot = campaign.Capture();
        var ufo = snapshot.World.Ufos[0] with { Speed = 0, Longitude = 0.2, Latitude = 0.1 };
        snapshot = snapshot with { World = snapshot.World with { Ufos = [ufo] } };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Takeoff = takingOff ? 60 : 0 } });
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        AssertTimeBlocked(campaign, "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Fact]
    public void LossPatrolsAtCurrentPositionAndNotifiesWithLastTargetPosition()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = PursuitCampaign(content);
        var snapshot = campaign.Capture();
        campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            { Ufos = [snapshot.World.Ufos[0] with { Detected = false }] }
        }, content, new SplitMix64RandomSource(92));
        var result = campaign.Execute(new AdvanceCampaignTime(12));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var lost = Assert.Single(result.Events.OfType<CraftTargetLost>());
        Assert.False(lost.ReturningToBase);
        Assert.Equal(campaign.Capture().World.Ufos[0].Longitude, lost.Target.Longitude);
        var state = Ship(campaign.Capture());
        Assert.Null(state.Destination);
        Assert.Equal(3000, state.Speed);
        Assert.Equal(0.2, state.Longitude);
        Reload(content, campaign, 92, "lost-target.sav");
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 5)]
    public void LandingAndTakeoffUseProjectedUfoStatusBeforeTheTick(bool suppressEmptyLanding, int timer)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with { Detected = true, SecondsRemaining = timer };
        snapshot = snapshot with
        {
            Options = snapshot.Options with { SuppressLandingWithoutEquipment = suppressEmptyLanding },
            World = snapshot.World with { Ufos = [ufo] },
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Speed = 6000,
                Longitude = ufo.Longitude,
                Latitude = ufo.Latitude,
                Items = new Dictionary<string, int>(StringComparer.Ordinal),
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, ufo.Longitude, ufo.Latitude)
                { UniqueId = ufo.UniqueId },
            }
        });
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with { Soldiers =
            [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } },
                .. owner.Soldiers.Skip(1)] }, .. snapshot.Bases.Skip(1)]
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        campaign = Reload(content, campaign, 92, "landing-option.sav");
        if (timer == 5)
            AssertTimeBlocked(campaign, "Craft interception requires strategic combat (Phase 6 branch 5).");
        else if (!suppressEmptyLanding)
            AssertTimeBlocked(campaign, "Craft landing requires deployment (Phase 6 branch 5).");
        else
        {
            AdvanceOne(campaign);
            Assert.Equal(WorldTargetKind.Ufo, Ship(campaign.Capture()).Destination!.Kind);
            Assert.Equal(5, campaign.Capture().World.Ufos[0].SecondsRemaining);
        }
    }

    [Fact]
    public void FollowingCraftReturnsAfterLeaderArrivesAndUsesCurrentTargetCoordinatesInSaves()
    {
        var content = StrategicReadinessTestContent.Load("strategic-escort.rul");
        var snapshot = CreateSnapshot(content, "Escort return") with { Time = new(1, 1, 1, 1999, 1, 1, 0) };
        var ship = snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = ship with
        {
            Id = 2,
            PreservationKey = "",
            Logistics = ship.Logistics! with
            {
                Status = "STR_OUT",
                Speed = 2000,
                Longitude = 0.20001,
                Latitude = 0.1,
                Fuel = 300,
                Destination = new(WorldTargetKind.Base, WorldTargetReference.BaseType, snapshot.Bases[0].Id, 0.2, 0.1)
            }
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 300,
                Speed = 6000,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = new(WorldTargetKind.Craft, "SHIP", 2, 0.20001, 0.1)
            }
        });
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Crafts = [leader, .. snapshot.Bases[0].Crafts] }, .. snapshot.Bases.Skip(1)] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        AdvanceOne(campaign);
        Assert.Equal(WorldTargetKind.Base, Ship(campaign.Capture()).Destination!.Kind);
        campaign = Reload(content, campaign, 92, "escort-return.sav");
        AdvanceOne(campaign);
        Assert.Null(Ship(campaign.Capture()).Destination);
        Assert.NotEqual("STR_OUT", Ship(campaign.Capture()).Status);
    }

    [Fact]
    public void TerminalUfoSkipsCraftPassThenReturnsFollowerBeforeCleanup()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = PursuitCampaign(content);
        var snapshot = campaign.Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { Interrupted = true }],
                Ufos = [snapshot.World.Ufos[0] with { Destination = WorldTargetReference.ForWaypoint(0, snapshot.World.Ufos[0].Position) }],
            }
        };
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        AdvanceOne(campaign);
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(campaign.Capture().World.Ufos).Status);
        Assert.Equal(WorldTargetKind.Ufo, Ship(campaign.Capture()).Destination!.Kind);
        campaign = Reload(content, campaign, 92, "terminal-pursuit.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.True(Assert.Single(result.Events.OfType<CraftTargetLost>()).ReturningToBase);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Null(Ship(campaign.Capture()).Destination); // Already at home, checkup runs in this tick.
        Assert.NotEqual("STR_OUT", Ship(campaign.Capture()).Status);
    }

    [Theory]
    [InlineData(false, 0.2001, 30)]
    [InlineData(true, 0.2001, 30)]
    [InlineData(false, 0.3, 60)]
    public void CraftEscortUsesReferenceRangeAndFuelFloor(bool fromCache, double leaderLongitude, int consumption)
    {
        var content = StrategicReadinessTestContent.Load("strategic-escort.rul", fromCache);
        Assert.Equal(42, content.RuntimeRules.Campaign.EscortRange);
        var snapshot = CreateSnapshot(content, "Escort fuel") with
        { Time = new CampaignTime(1, 1, 1, 1999, 1, 9, 55) };
        var ship = snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = ship with
        {
            Id = 2,
            PreservationKey = "",
            Logistics = ship.Logistics! with
            { Status = "STR_OUT", Speed = 2000, Longitude = leaderLongitude, Latitude = 0.1, Fuel = 300 }
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 300,
                Speed = 6000,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = new(WorldTargetKind.Craft, "SHIP", 2, leaderLongitude, 0.1)
            }
        });
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts, leader] }, .. snapshot.Bases.Skip(1)] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        AdvanceOne(campaign);
        Assert.Equal(300 - consumption, Ship(campaign.Capture()).Fuel);
        Assert.Equal(WorldTargetKind.Craft, Ship(campaign.Capture()).Destination!.Kind);
        Reload(content, campaign, 92, "escort.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalArrivalElsewhereSkipsEveryPursuerInTheCraftPass(bool terminal)
    {
        // The first pursuer is far from its target and would not arrive; the second sits on it and
        // would intercept. A terminal arrival by an unrelated, later UFO skips the whole craft pass,
        // so the answer computed for the first craft must also apply to the second. Without it, the
        // second craft must still be checked after the first craft's non-arrival.
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = TwoPursuerSnapshot(content, terminal);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        if (!terminal)
        {
            AssertTimeBlocked(campaign, "Craft interception requires strategic combat (Phase 6 branch 5).");
            return;
        }
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(UfoStatus.Destroyed, after.World.Ufos.Single(ufo => ufo.UniqueId == 10).Status);
        // The skipped craft pass leaves the would-be interceptor on its target, unchanged.
        var before = snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP" && craft.Id == 2).Logistics!;
        var interceptor = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP" && craft.Id == 2).Logistics!;
        Assert.Equal((before.Longitude, before.Latitude, WorldTargetKind.Ufo),
            (interceptor.Longitude, interceptor.Latitude, interceptor.Destination!.Kind));
        Reload(content, campaign, 92, "two-pursuer-terminal.sav");
    }

    [Fact]
    public void RepeatedRefusedTicksLeaveNoPreflightStateBehind()
    {
        // A refused tick is retried, the cause is removed by a command, and time then advances.
        // The result must match a campaign that never attempted the refused tick.
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = TwoPursuerSnapshot(content, terminal: false);
        var retried = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        AssertTimeBlocked(retried, "Craft interception requires strategic combat (Phase 6 branch 5).");
        AssertTimeBlocked(retried, "Craft interception requires strategic combat (Phase 6 branch 5).");
        var direct = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
        foreach (var campaign in new[] { retried, direct })
        {
            Assert.DoesNotContain(campaign.Execute(new RecallCraft(snapshot.Bases[0].Id, "SHIP", 2)).Events,
                notification => notification is CampaignActionBlocked);
            AdvanceUnblocked(campaign, 12);
        }
        Assert.Equivalent(direct.Capture(), retried.Capture(), strict: true);
    }

    private static CampaignSnapshot TwoPursuerSnapshot(RuntimeContent content, bool terminal)
    {
        var snapshot = PursuitCampaign(content).Capture();
        var target = snapshot.World.Ufos[0] with { Speed = 0, Longitude = 0.6, Latitude = 0.3 };
        // A later UFO of another mission stops on its own waypoint this tick. Its mission is
        // interrupted only in the terminal case, which destroys the UFO and ends the flight pass.
        var other = snapshot.World.Ufos[0] with
        {
            UniqueId = 10,
            Id = 4,
            MissionId = 5,
            Detected = false,
            Longitude = 0.9,
            Latitude = -0.2,
            Destination = WorldTargetReference.ForWaypoint(0, new(0.9, -0.2)),
        };
        var otherMission = snapshot.World.Missions[0] with { Id = 5, Interrupted = terminal };
        if (!terminal) other = other with { Destination = WorldTargetReference.ForWaypoint(0, new(2.5, -0.2)) };
        var ship = snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var pursuit = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id,
            target.Longitude, target.Latitude)
        { UniqueId = target.UniqueId };
        var interceptor = ship with
        {
            Id = 2,
            PreservationKey = "",
            Logistics = ship.Logistics! with { Longitude = target.Longitude, Latitude = target.Latitude, Destination = pursuit },
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Destination = pursuit } });
        return snapshot.WithNextId("STR_UFO", 5) with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0], otherMission],
                Ufos = [target, other],
            },
            Bases = [snapshot.Bases[0] with { Crafts = [.. snapshot.Bases[0].Crafts, interceptor] }, .. snapshot.Bases.Skip(1)],
        };
    }

    private static CampaignState PursuitCampaign(RuntimeContent content)
    {
        var campaign = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0), "UFO_SCOUT", 2200);
        var snapshot = campaign.Capture();
        var ufo = snapshot.World.Ufos[0] with { Detected = true };
        snapshot = snapshot with { World = snapshot.World with { Ufos = [ufo] } };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 6000,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, ufo.Longitude, ufo.Latitude) { UniqueId = ufo.UniqueId }
            }
        });
        return CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));
    }

    private static CraftLogisticsState Ship(CampaignSnapshot snapshot) =>
        snapshot.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP" && craft.Id == 1).Logistics!;
}
