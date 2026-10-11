using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldPointOriginTests
{
    [Fact]
    public void OriginsChooseOrderedPointsAndScoreOnlyTheFirstContainingCountry()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load();
        var seen = new HashSet<int>();
        foreach (var seed in new ulong[] { 1, 17, 42, 84 })
        {
            var snapshot = Snapshot(content) with { RandomState = seed };
            var random = new SplitMix64RandomSource(seed);
            var selected = random.NextInclusive(0, 1);
            seen.Add(selected);
            random.NextUnit(); random.NextUnit();
            var row = Assert.Single(TestFixtures.Rows(oracle.RootElement, "pointOriginRows"), row => row[0].GetInt32() == selected);
            var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
            var origin = new WorldPosition(Degrees(selected == 0 ? 8 : 12), Degrees(selected == 0 ? 2 : -2));
            Assert.Equal(row[1].GetString(), origin.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[2].GetString(), origin.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(6, row[3].GetInt32());
            var destination = WorldGeometry.RandomPoint(region, 4, -1, random);
            WorldTrajectory.SpawnCountdown(9000, random);
            random.NextInclusive(0, 99);
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(seed));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Equal(WorldGeometry.Move(origin, destination, WorldGeometry.RadianSpeed(2200)), Assert.Single(after.World.Ufos).Position);
            Assert.Equal(3, after.Countries[selected].ActivityAlien[0]);
            Assert.Equal(0, after.Countries[1 - selected].ActivityAlien[0]);
            Assert.Equal(3, after.Regions[0].ActivityAlien[0]);
            Assert.Equal(random.State, after.RandomState);
            Reload(content, campaign, seed, "point-origin.sav");
        }
        Assert.Equal(2, seen.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryPossibleCountryAwardIsReservedBeforeSelectingAnOrigin(int country)
    {
        var content = Load();
        var snapshot = Snapshot(content);
        var countries = snapshot.Countries.ToArray();
        countries[country] = countries[country] with { ActivityAlien = [int.MaxValue - 2] };
        var campaign = CampaignState.Restore(snapshot with { Countries = countries }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    private static RuntimeContent Load() => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-point-origins.rul"]);

    private static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = CreateSnapshot(content, "Point origins");
        return initial with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Countries = [new("NORTH", [1], [0], [0], false, false, false, []), new("SOUTH", [1], [0], [0], false, false, false, [])],
            World = initial.World with { Missions = [new AlienMissionSnapshot(4, "MULTI_ORIGIN", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }
}
