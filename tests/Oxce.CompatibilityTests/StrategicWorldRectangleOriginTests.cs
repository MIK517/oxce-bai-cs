using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRectangleOriginTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneRectangleOriginsMatchReferenceDrawsAndWrappedRawBounds(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rectangleOriginRows"))
        {
            if (row[1].GetDouble() == 1) continue;
            var reversed = row[0].GetInt32() != 0;
            var campaign = CampaignState.Restore(Snapshot(content, reversed ? "RECT_ORIGIN_REVERSED" : "RECT_ORIGIN") with
            { RandomState = 0 }, content, new FractionChoices(row[1].GetDouble()));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            var origin = new WorldPosition(double.Parse(row[2].GetString()!, CultureInfo.InvariantCulture),
                double.Parse(row[3].GetString()!, CultureInfo.InvariantCulture));
            var destination = ufo.Destination!;
            Assert.Equal(row[4].GetString(), destination.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[5].GetString(), destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            var exactOrigin = WorldPosition.Create(Degrees(12 + 4 * row[1].GetDouble()), Degrees(1 + 2 * row[1].GetDouble()));
            Assert.InRange(WorldGeometry.Distance(origin, exactOrigin), 0, 1e-7);
            Assert.Equal(WorldGeometry.Move(exactOrigin, destination.Position, WorldGeometry.RadianSpeed(2200)).Longitude, ufo.Longitude, 12);
            Assert.Equal(WorldGeometry.Move(exactOrigin, destination.Position, WorldGeometry.RadianSpeed(2200)).Latitude, ufo.Latitude, 12);
            Assert.Equal((ulong)(row[6].GetInt32() + 2), after.RandomState);
            Assert.Equal(3, after.Countries[0].ActivityAlien[0]);
            Assert.Equal(0, after.Countries[1].ActivityAlien[0]);
            Reload(content, campaign, 17, "rectangle-origin.sav");
        }
    }

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.75, 0)]
    public void CrossingRectangleScoresOnlyItsActualFirstContainingCountry(double fraction, int country)
    {
        var content = Load(false);
        var campaign = CampaignState.Restore(Snapshot(content, "RECT_ORIGIN_CROSSING") with { RandomState = 0 },
            content, new FractionChoices(fraction));
        AdvanceOne(campaign);
        Assert.Equal(3, campaign.Capture().Countries[country].ActivityAlien[0]);
        Assert.Equal(0, campaign.Capture().Countries[1 - country].ActivityAlien[0]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void PossiblePositiveAndNegativeAwardsBlockBeforeAnyDraw(int country, bool negative)
    {
        var content = Load(false);
        var snapshot = Snapshot(content, negative ? "RECT_ORIGIN_NEGATIVE" : "RECT_ORIGIN_CROSSING");
        var countries = snapshot.Countries.ToArray();
        countries[country] = countries[country] with { ActivityAlien = [negative ? int.MinValue + 2 : int.MaxValue - 2] };
        var campaign = CampaignState.Restore(snapshot with { Countries = countries }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void AnOriginRectangleThatCanReachItsDestinationBlocksAtomically()
    {
        var content = Load(false);
        var campaign = CampaignState.Restore(Snapshot(content, "RECT_ORIGIN_UNSAFE"), content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Alien mission wave spawning requires world simulation.");
    }

    private static RuntimeContent Load(bool cache) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-rectangle-origins.rul"], cache);

    private static CampaignSnapshot Snapshot(RuntimeContent content, string mission)
    {
        var initial = CreateSnapshot(content, "Rectangle origins");
        return initial with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Countries = [new("NORTH", [1], [0], [0], false, false, false, []), new("SOUTH", [1], [0], [0], false, false, false, [])],
            World = initial.World with { Missions = [new AlienMissionSnapshot(4, mission, "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }

    private sealed class FractionChoices(double fraction) : IStatefulRandomSource
    {
        public ulong State { get; set; }
        public int NextInclusive(int minimum, int maximum) { State++; return minimum; }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public void Restore(ulong state) => State = state;
        public double NextUnit() { State++; return fraction; }
    }
}
