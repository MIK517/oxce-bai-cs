using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSupplyEscortFollowerTests
{
    [Theory]
    [InlineData(false, false, 0, false)]
    [InlineData(true, false, 0, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(false, false, 5, false)]
    [InlineData(true, false, 5, false)]
    [InlineData(false, true, 5, false)]
    [InlineData(true, true, 5, false)]
    [InlineData(false, false, 10, false)]
    [InlineData(true, false, 10, false)]
    [InlineData(false, true, 10, false)]
    [InlineData(true, true, 10, false)]
    [InlineData(false, false, 0, true)]
    [InlineData(true, false, 0, true)]
    [InlineData(false, true, 0, true)]
    [InlineData(true, true, 0, true)]
    [InlineData(false, false, 5, true)]
    [InlineData(true, false, 5, true)]
    [InlineData(false, true, 5, true)]
    [InlineData(true, true, 5, true)]
    [InlineData(false, false, 10, true)]
    [InlineData(true, false, 10, true)]
    [InlineData(false, true, 10, true)]
    [InlineData(true, true, 10, true)]
    public void SourceAlternativesComposeWithOrderedFollowerPursuitAndTakeoff(bool cache, bool reverse, int timer, bool regional)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        Assert.Equal(6, TestFixtures.Rows(oracle.RootElement, "idleEscortFollowerMovement").Length);
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var kind = regional ? 2 : 0;
        var content = StrategicWorldOptionalSupplySourceTests.Content(cache, kind, nonObjectiveRegional: regional, craftRadarRange: 20000);
        var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, kind, "CORE", nonObjectiveRegional: regional, craftRadarRange: 20000);
        var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, kind, "", nonObjectiveRegional: regional, craftRadarRange: 20000);
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var initial = Pursuit(Snapshot(content, seed, reverse, timer), content, seed, false);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var source = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
            var attached = regional || !deleted;
            Assert.Equal(attached, source.Escorting);
            Assert.False(source.Hunting);
            Assert.Equal(attached ? 0 : 1, source.TrajectoryPoint);
            if (attached)
                Assert.Equal(deleted ? 1 : 0, after.World.Ufos.Single(ufo => ufo.UniqueId == source.Destination!.UniqueId).MissionWaveNumber);
            var positions = new Dictionary<int, WorldPosition>();
            foreach (var actor in initial.World.Ufos)
            {
                if (actor.UniqueId == 9) { positions.Add(9, source.Position); continue; }
                var targetId = actor.Destination!.UniqueId;
                var target = positions.TryGetValue(targetId, out var moved) ? moved : initial.World.Ufos.Single(ufo => ufo.UniqueId == targetId).Position;
                positions.Add(actor.UniqueId, actor.Status == UfoStatus.Landed ? actor.Position :
                    WorldGeometry.Move(actor.Position, target, WorldGeometry.RadianSpeed(2200)));
                var actual = after.World.Ufos.Single(ufo => ufo.UniqueId == actor.UniqueId);
                Assert.Equal(positions[actor.UniqueId], actual.Position);
                Assert.True(actual.Escorting);
                Assert.Equal(timer == 10 && actor.UniqueId == 10 ? UfoStatus.Landed : UfoStatus.Flying, actual.Status);
                if (timer == 5 && actor.UniqueId == 10)
                {
                    var vector = WorldGeometry.SpeedVector(actor.Position, target, WorldGeometry.RadianSpeed(2200));
                    Assert.Equal(vector.Longitude, actual.SpeedLongitude);
                    Assert.Equal(vector.Latitude, actual.SpeedLatitude);
                    Assert.Equal(WorldAltitudes.Direction(vector.Longitude, vector.Latitude), actual.Direction);
                }
            }
            Assert.Equal(after.World.Ufos.Count, Assert.Single(after.World.Missions).LiveUfos);
            var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Equal(11, craft.Destination!.UniqueId);
            Assert.Equal(positions[11], craft.Destination.Position);
            Assert.Equal(WorldGeometry.Move(new(1, 0.6), positions[11], WorldGeometry.RadianSpeed(6000)), new(craft.Longitude, craft.Latitude));
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-supply-escort-chain.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseTransitivePursuitAndMalformedFollowerStopBeforeResearch(bool cache)
    {
        var content = StrategicWorldOptionalSupplySourceTests.Content(cache, 0);
        var initial = Pursuit(Snapshot(content, 17, false, 0), content, 17, true);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Loaded idle escorts require same-tick newborn craft pursuit forecasts.");
        initial = Snapshot(content, 17, false, 10);
        initial = initial with { World = initial.World with { Ufos = initial.World.Ufos.Select(ufo => ufo.UniqueId == 10 ? ufo with { SecondsRemaining = 0 } : ufo).ToArray() } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Regional newborn leader followers require flying escort forecasts.");
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool reverse, int timer)
    {
        var initial = StrategicWorldOptionalSupplyEscortSourceTests.Snapshot(content, seed, true);
        var source = initial.World.Ufos[0] with { Detected = true };
        var follower = source with
        {
            UniqueId = 10,
            Id = 10,
            Longitude = 0.4003,
            Latitude = 0.2001,
            TrajectoryPoint = 2,
            Escorting = true,
            OriginalWaypoint = new(1, 0.6),
            Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, source.Longitude, source.Latitude) { UniqueId = 9 },
        };
        var last = follower with
        {
            UniqueId = 11,
            Id = 11,
            Longitude = 0.4006,
            Latitude = 0.2002,
            Destination = follower.Destination! with { Id = 10, UniqueId = 10, Longitude = follower.Longitude, Latitude = follower.Latitude },
        };
        if (timer != 0) follower = follower with { Status = UfoStatus.Landed, Altitude = WorldAltitudes.Ground, Speed = 0, SecondsRemaining = timer };
        return initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 3 }], Ufos = reverse ? [last, follower, source] : [source, follower, last] } };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RadarLossRetainsPatrolResponseAcrossBothSourceOutcomes(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicWorldOptionalSupplySourceTests.Content(cache, 0);
        var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, 0, "CORE");
        var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, 0, "");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var initial = Pursuit(Snapshot(content, seed, false, 0), content, seed, false);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.False(Assert.Single(result.Events.OfType<CraftTargetLost>()).ReturningToBase);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Null(craft.Destination);
            Assert.Equal(3000, craft.Speed);
            Assert.Equal(new WorldPosition(1, 0.6), new(craft.Longitude, craft.Latitude));
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-supply-escort-contact-loss.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    private static CampaignSnapshot Pursuit(CampaignSnapshot initial, RuntimeContent content, ulong seed, bool close)
    {
        var target = initial.World.Ufos.Single(ufo => ufo.UniqueId == 11);
        var from = close ? target.Position : new WorldPosition(1, 0.6);
        initial = initial with { Bases = [initial.Bases[0] with { Crafts = StrategicWorldOptionalSupplySourceTests.Snapshot(content, seed).Bases[0].Crafts }] };
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Damage = 0,
                Fuel = 1000,
                Speed = 6000,
                Takeoff = 0,
                Longitude = from.Longitude,
                Latitude = from.Latitude,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id, target.Longitude, target.Latitude) { UniqueId = target.UniqueId },
            },
        });
    }
}
