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
    public void ArrivalAndDetectionBoundariesStopBeforeMutation()
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
            Time = new CampaignTime(1, 1, 1, 1999, 1, 9, 55),
        }, content, new SplitMix64RandomSource(43));
        AssertBlockedWithoutMutation(boundary, "UFO detection and retargeting require world simulation.");
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

    private static CampaignState CreateTransitCampaign(RuntimeContent content, CampaignTime time)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "UFO transit", CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        var mission = new AlienMissionSnapshot(4, "MISSION_SCOUT", "REGION", "RACE_A", 2, 0, 1500, 1, -1);
        var ufo = new UfoSnapshot(9, "UFO_HUNTER", 4, "TRAJ_PATROL", 0,
            0.4, 0.2, UfoStatus.Flying, "STR_HIGH_UC")
        {
            Id = 3,
            Speed = 3200,
            MissionWaveNumber = 1,
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
