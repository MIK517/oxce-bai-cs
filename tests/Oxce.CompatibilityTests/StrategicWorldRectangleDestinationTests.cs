using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRectangleDestinationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleDestinationsMatchReferenceBoundsAndDrawOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-rectangle-destinations.rul"], cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rectangleDestinationRows"))
        {
            if (row[0].GetInt32() != 0 || row[1].GetDouble() == 1) continue;
            var initial = CreateSnapshot(content, "Rectangle destination");
            var random = new FractionChoices(row[1].GetDouble());
            var campaign = CampaignState.Restore(initial with
            {
                RandomState = 0,
                Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                World = initial.World with { Missions = [new AlienMissionSnapshot(4, "RECT_DESTINATION", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
            }, content, random);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var destination = Assert.Single(after.World.Ufos).Destination!;
            Assert.Equal(row[2].GetString(), destination.Longitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal(row[3].GetString(), destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            Assert.Equal((ulong)(row[4].GetInt32() + 2), after.RandomState);
            Reload(content, campaign, 17, "rectangle-destination.sav");
        }
    }

    [Fact]
    public void ARectangleContainingTheOriginBlocksTheWholeTick()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-rectangle-destinations.rul"]);
        var initial = CreateSnapshot(content, "Unsafe rectangle");
        var campaign = CampaignState.Restore(initial with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = initial.World with { Missions = [new AlienMissionSnapshot(4, "RECT_UNSAFE", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Alien mission wave spawning requires world simulation.");
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
