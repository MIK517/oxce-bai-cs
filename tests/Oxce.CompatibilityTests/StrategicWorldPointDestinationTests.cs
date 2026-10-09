using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldPointDestinationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedPointDestinationsConsumeReferenceDrawsAcrossReloads(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-point-destinations.rul"], cache);
        var seen = new HashSet<int>();
        foreach (var seed in new ulong[] { 1, 17, 42, 84 })
        {
            var initial = CreateSnapshot(content, "Point destinations");
            var snapshot = initial with
            {
                Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                RandomState = seed,
                World = initial.World with { Missions = [new AlienMissionSnapshot(4, "MULTI_DESTINATION", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
            };
            var random = new SplitMix64RandomSource(seed);
            var regions = content.RuntimeRules.Regions;
            var region = regions[regions.GetRequired("REGION")].Value;
            var origin = WorldGeometry.RandomPoint(region, 4, -1, random);
            var selected = random.NextInclusive(0, 1);
            seen.Add(selected);
            random.NextUnit(); random.NextUnit();
            WorldTrajectory.SpawnCountdown(9000, random);
            random.NextInclusive(0, 99);
            var row = Assert.Single(TestFixtures.Rows(oracle.RootElement, "pointDestinationRows"), row => row[0].GetInt32() == selected);
            Assert.Equal(6, row[3].GetInt32());
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(seed));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(row[1].GetString(), ufo.Destination!.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[2].GetString(), ufo.Destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            var moved = WorldGeometry.Move(origin, ufo.Destination.Position, WorldGeometry.RadianSpeed(2200));
            Assert.Equal(moved, ufo.Position);
            Assert.Equal(random.State, after.RandomState);
            Reload(content, campaign, seed, "point-destination.sav");
        }
        Assert.Equal(2, seen.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedPointCandidatesPreserveSelectedFirstArrivalAndContinuingFlight(bool cache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-point-destinations.rul"], cache);
        var initial = CreateSnapshot(content, "Mixed first-arrival candidates");
        var arrivedSeen = false;
        var continuingSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var selected = initial with
            {
                RandomState = seed,
                Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                World = initial.World with { Missions = [new(4, "UNSAFE_DESTINATION", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
            };
            var arrived = AssertSelectedNewbornArrival(content, selected, new SplitMix64RandomSource(seed), new SplitMix64RandomSource(seed));
            arrivedSeen |= arrived;
            continuingSeen |= !arrived;
        }
        Assert.True(arrivedSeen);
        Assert.True(continuingSeen);
    }
}
