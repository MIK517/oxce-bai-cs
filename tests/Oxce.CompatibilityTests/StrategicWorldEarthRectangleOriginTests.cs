using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldEarthRectangleOriginTests
{
    [Fact]
    public void EarthBaseOverridesRectangularOriginAfterReferenceRegionalDraws()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-earth-rectangle-origins.rul"]);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "earthRectangleRows"))
        {
            var operation = row[0].GetInt32();
            var groundNext = row[1].GetInt32();
            var initial = CreateSnapshot(content, "Earth rectangular origin");
            var mission = new AlienMissionSnapshot(4, $"EARTH_RECT_{operation}_{groundNext}", "REGION", "RACE_A", 0, 0, 0, 0, -1)
            { AlienBase = new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 7, 0.2, 0.1) };
            var snapshot = initial with
            {
                RandomState = 17,
                Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                World = initial.World with { Missions = [mission], AlienBases = [new(7, "ALIEN_BASE_DEPLOY", "RACE_A", 0.2, 0.1)] },
            };
            var random = new SplitMix64RandomSource(17);
            var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
            if (groundNext != 0) random.NextInclusive(0, 99);
            var regionalOrigin = WorldGeometry.RandomPoint(region, 0, -1, random);
            WorldGeometry.RandomPoint(region, 5, -1, random);
            WorldTrajectory.SpawnCountdown(9000, random);
            random.NextInclusive(0, 99);
            Assert.Equal(6 + groundNext, row[6].GetInt32());
            var origin = new WorldPosition(0.2, 0.1);
            Assert.Equal(row[2].GetString(), origin.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[3].GetString(), origin.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(row[4].GetString(), ufo.Destination!.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[5].GetString(), ufo.Destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(WorldGeometry.Move(origin, ufo.Destination.Position, WorldGeometry.RadianSpeed(2200)), ufo.Position);
            Assert.Equal(random.State, after.RandomState);
            Reload(content, campaign, 17, "earth-rectangle-origin.sav");
            var space = CampaignState.Restore(snapshot with
            {
                World = snapshot.World with
                { Missions = [mission with { RuleId = $"EARTH_RECT_0_{groundNext}" }] }
            }, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(space, 1);
            var spaceAfter = space.Capture();
            var spaceUfo = Assert.Single(spaceAfter.World.Ufos);
            var destination = new WorldPosition(Degrees(8), Degrees(2));
            var arrived = WorldGeometry.Distance(regionalOrigin, destination) <= WorldGeometry.RadianSpeed(2200);
            var spaceRandom = new SplitMix64RandomSource(random.State);
            if (arrived)
                for (var draw = 0; draw < 6; draw++) spaceRandom.NextUnit();
            Assert.Equal(spaceRandom.State, spaceAfter.RandomState);
            Assert.Equal(WorldGeometry.Move(regionalOrigin, destination, WorldGeometry.RadianSpeed(2200)), spaceUfo.Position);
            Assert.Equal(arrived ? 1 : 0, spaceUfo.TrajectoryPoint);
            Assert.Equal(arrived && groundNext != 0 ? UfoStatus.Landed : UfoStatus.Flying, spaceUfo.Status);
            Assert.Equal(1, Assert.Single(spaceAfter.World.Missions).LiveUfos);
            Reload(content, space, 17, "space-rectangle-origin.sav");
        }
    }
}
