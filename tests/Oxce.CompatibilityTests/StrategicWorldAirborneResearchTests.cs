using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldAirborneResearchTests
{
    [Fact]
    public void UncertainAirborneArrivalPreservesSelectedMovementFallthrough()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content();
        var interruptedContent = Content("CORE");
        var continuedContent = Content("");
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var interrupted = Assert.Single(after.World.Missions).Interrupted;
            Assert.Equal(after.CompletedResearch.Contains("BONUS"), interrupted);
            Assert.Equal(interrupted ? UfoStatus.Destroyed : UfoStatus.Flying, after.World.Ufos[0].Status);
            Assert.Equal(interrupted ? 0 : 1, after.World.Ufos[0].TrajectoryPoint);
            Assert.Equal(interrupted, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
            Assert.Equal(interrupted, after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude ==
                initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude);
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture().World, after.World, strict: true);
            Assert.Equivalent(control.Capture().Bases, after.Bases, strict: true);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Equivalent(control.Capture().NextIds, after.NextIds, strict: true);
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "airborne-research-arrival.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(interrupted ? 1 : 2, campaign.Capture().World.Ufos.Count);
            Assert.Equal(interrupted ? 1 : 2, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
            Reload(content, campaign, seed, "airborne-research-next-tick.sav");
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    [Fact]
    public void PossibleContinuationStillRequiresSafeWaypointAndInterceptionProofs()
    {
        var content = Content(nextSpeed: -100);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17), content, new SplitMix64RandomSource(17)),
            "UFO waypoint arrival requires mission simulation.");
        content = Content();
        var initial = Snapshot(content, 17);
        var ufo = initial.World.Ufos[0] with { Detected = true };
        initial = initial with { World = initial.World with { Ufos = [ufo, initial.World.Ufos[1]] } };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Longitude = ufo.Longitude,
                Latitude = ufo.Latitude,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, ufo.Longitude, ufo.Latitude)
                { UniqueId = ufo.UniqueId },
            },
        });
        // The earlier guard refused conservatively. The UFO reaches its last waypoint and is destroyed,
        // which ends the flight pass before the craft pass, so no interception starts.
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.DoesNotContain(result.Events, static evt => evt is CampaignActionBlocked or InterceptionStarted);
        Assert.Empty(campaign.GetQuery<ICampaignInterceptionQuery>()!.QueryInterceptions());
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed)
    {
        var initial = StrategicWorldSiteResearchUfoTests.Arrival(content) with { RandomState = seed };
        var first = initial.World.Ufos[0] with { TrajectoryId = "TRAJ_UNCERTAIN", TrajectoryPoint = 0 };
        var second = first with
        {
            UniqueId = first.UniqueId + 1,
            Id = first.Id + 1,
            Longitude = 0,
            Latitude = 0,
            Destination = WorldTargetReference.ForWaypoint(0, new(0.6, 0.2)),
        };
        initial = initial with
        {
            World = initial.World with
            { Ufos = [first, second], Missions = [initial.World.Missions[0] with { LiveUfos = 2 }] }
        };
        var owner = initial.Bases[0];
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 6000,
                SpeedRadian = WorldGeometry.RadianSpeed(6000),
                Longitude = owner.Longitude + 0.1,
                Latitude = owner.Latitude + 0.1,
                Destination = new(WorldTargetKind.Base, WorldTargetReference.BaseType, owner.Id, owner.Longitude, owner.Latitude),
            },
        });
    }

    internal static RuntimeContent Content(string interrupt = "BONUS", bool landing = false, int nextSpeed = 100) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienMissions:
          - {type: MISSION_SCOUT, interruptResearch: {{{interrupt}}}}
        ufoTrajectories:
          - id: TRAJ_UNCERTAIN
            groundTimer: 12
            waypoints: [[4, 3, 100], [4, {{{(landing ? 0 : 3)}}}, {{{nextSpeed}}}], [4, 3, 100]]
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment);
}
