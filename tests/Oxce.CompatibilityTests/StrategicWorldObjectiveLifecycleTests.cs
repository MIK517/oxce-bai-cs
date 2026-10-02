using System.Globalization;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldObjectiveLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScoreOperationsFollowReferenceWaypointsAndReturnToTheirSource(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "transitionRows").Where(row => row[0].GetInt32() == 0))
            AssertTransition(content, row);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherObjectivesFollowOrdinaryAirborneWaypoints(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "transitionRows")
                     .Where(row => row[0].GetInt32() != 0 && row[3].GetInt32() != 1))
            AssertTransition(content, row);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryGroundArrivalsAcrossObjectivesPreserveLandingTimers(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "transitionRows")
                     .Where(row => row[0].GetInt32() != 0 && row[3].GetInt32() == 1))
            AssertTransition(content, row);
    }

    [Fact]
    public void GroundArrivalThatWouldCreateAnAlienBaseIsBlockedBeforeMutation()
    {
        var content = Load(false);
        var initial = CreateLifecycleSnapshot(content);
        var mission = initial.World.Missions[0] with { RuleId = "LIFECYCLE_BASE_EFFECT" };
        var ufo = initial.World.Ufos[0] with
        {
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.Low,
            TrajectoryPoint = 1,
            Destination = WorldTargetReference.ForWaypoint(0, initial.World.Ufos[0].Position),
        };
        var campaign = CampaignState.Restore(initial with
        { World = initial.World with { Missions = [mission], Ufos = [ufo] } }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "UFO waypoint arrival requires mission simulation.");
    }

    [Fact]
    public void AggressiveRetaliationArrivalsStayAtomicBeforeTheirAdditionalRandomChoices()
    {
        var content = Load(false);
        var initial = CreateLifecycleSnapshot(content);
        var mission = initial.World.Missions[0] with { RuleId = "LIFECYCLE_4_0" };
        var ufo = initial.World.Ufos[0] with
        {
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.High,
            TrajectoryPoint = 2,
            Destination = WorldTargetReference.ForWaypoint(0, initial.World.Ufos[0].Position),
        };
        var campaign = CampaignState.Restore(initial with
        { World = initial.World with { Missions = [mission], Ufos = [ufo] } }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "UFO waypoint arrival requires mission simulation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryTakeoffsUseReferenceObjectiveScoreFiltering(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "normalLiftRows"))
        {
            var initial = CreateLifecycleSnapshot(content);
            var objective = row[0].GetInt32();
            var operation = row[1].GetInt32();
            var excluded = objective is 1 or 2;
            var snapshot = initial with
            {
                Regions = [initial.Regions[0] with { ActivityAlien = [excluded ? int.MaxValue : 0] }],
                World = initial.World with
                {
                    Missions = [initial.World.Missions[0] with { RuleId = $"LIFECYCLE_{objective}_{operation}" }],
                    Ufos = [initial.World.Ufos[0] with { SecondsRemaining = 5 }],
                },
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var actual = Assert.Single(after.World.Ufos);
            Assert.Equal((UfoStatus)row[2].GetInt32(), actual.Status);
            Assert.Equal(row[3].GetString(), actual.Altitude);
            Assert.Equal(row[4].GetInt32(), actual.Speed);
            Assert.Equal(row[5].GetInt32(), actual.SecondsRemaining);
            Assert.Equal(excluded ? int.MaxValue : row[6].GetInt32(), after.Regions[0].ActivityAlien[0]);
            Assert.Equal(snapshot.RandomState, after.RandomState);
            Reload(content, campaign, 17, "objective-takeoff.sav");
        }
    }

    private static void AssertTransition(RuntimeContent content, JsonElement row)
    {
        var objective = row[0].GetInt32();
        var operation = row[1].GetInt32();
        var linked = row[2].GetInt32() != 0;
        var point = row[3].GetInt32();
        var initial = CreateLifecycleSnapshot(content);
        var mission = initial.World.Missions[0] with
        {
            RuleId = $"LIFECYCLE_{objective}_{operation}",
            AlienBase = linked ? new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 7, 0.2, 0.1) : null,
        };
        var ufo = initial.World.Ufos[0] with
        {
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.High,
            TrajectoryPoint = point,
            SecondsRemaining = 0,
            Speed = 2200,
            Destination = WorldTargetReference.ForWaypoint(0, initial.World.Ufos[0].Position),
        };
        var snapshot = initial with
        {
            Options = initial.Options with { AggressiveRetaliation = false },
            World = initial.World with
            {
                Missions = [mission],
                Ufos = [ufo],
                AlienBases = linked ? [new AlienBaseSnapshot(7, "ALIEN_BASE_DEPLOY", "RACE_A", 0.2, 0.1)] : [],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var actual = Assert.Single(after.World.Ufos);
        Assert.Equal((UfoStatus)row[4].GetInt32(), actual.Status);
        Assert.Equal(row[5].GetString(), actual.Altitude);
        Assert.Equal(row[6].GetString(), actual.Destination!.Longitude.ToString("F9", CultureInfo.InvariantCulture));
        Assert.Equal(row[7].GetString(), actual.Destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
        Assert.Equal(row[8].GetInt32(), actual.Speed);
        Assert.Equal(row[9].GetInt32(), actual.SecondsRemaining);
        var random = new SplitMix64RandomSource(17);
        for (var draw = 0; draw < row[10].GetInt32(); draw++) random.NextInclusive(0, 0);
        Assert.Equal(random.State, after.RandomState);
        Reload(content, campaign, 17, "objective-waypoint.sav");
    }

    private static RuntimeContent Load(bool cache) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-objective-lifecycle.rul"], cache);
}
