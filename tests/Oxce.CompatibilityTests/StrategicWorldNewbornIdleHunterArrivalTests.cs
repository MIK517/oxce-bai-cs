using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornIdleHunterArrivalTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void NoTargetConstructorsPreserveRandomChoicesAndFirstArrival(int percentage)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        HashSet<(bool Hunter, int Mode, int Behavior)> outcomes = [];
        for (var craftKind = 0; craftKind < 6; craftKind++)
        {
            var content = StrategicWorldNewbornFirstArrivalTests.Content(false,
                hunterPercentage: percentage, extraRules: craftKind == 2 ? "crafts: [{type: SHIP, undetectable: true}]" : "",
                radarRange: craftKind == 4 ? 300 : craftKind == 5 ? int.MinValue : 0);
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
                if (craftKind == 0)
                    initial = initial with { Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [] }] };
                else if (craftKind >= 2)
                    initial = initial.WithCraft("SHIP", craft => craft with
                    {
                        Logistics = craft.Logistics! with
                        {
                            Status = "STR_OUT",
                            Speed = 0,
                            Fuel = 100,
                            Longitude = Degrees(20),
                            Latitude = Degrees(2),
                            Destination = WorldTargetReference.ForWaypoint(5, new(Degrees(30), Degrees(2))),
                        },
                    });
                var expectedRandom = new SplitMix64RandomSource(seed);
                List<(bool Hunter, int Mode, int Behavior)> expected = [];
                for (var wave = 0; wave < 3; wave++)
                {
                    var hunter = expectedRandom.NextInclusive(0, 99) < percentage;
                    expected.Add((hunter, hunter ? expectedRandom.NextInclusive(0, 1) : 0,
                        hunter ? expectedRandom.NextInclusive(0, 1) : 0));
                    for (var draw = 0; draw < 6; draw++) expectedRandom.NextUnit();
                    if (wave < 2) expectedRandom.NextUnit();
                }
                // Detection draws at zero chance for each base and outbound craft;
                // being undetectable to hunters does not remove the craft radar caller.
                // Every arrival then makes two native waypoint selections.
                for (var draw = 0; draw < 3 * (craftKind >= 2 ? 8 : 7); draw++) expectedRandom.NextUnit();
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                Assert.Empty(result.Events.OfType<UfoStartedHunting>());
                var after = campaign.Capture();
                Assert.Equal(expectedRandom.State, after.RandomState);
                Assert.Equal(3, after.World.Ufos.Count);
                for (var wave = 0; wave < 3; wave++)
                {
                    var ufo = after.World.Ufos[wave];
                    var actual = (ufo.HunterKiller, ufo.HuntMode, ufo.HuntBehavior);
                    Assert.Equal(expected[wave], actual);
                    outcomes.Add(actual);
                    Assert.False(ufo.Hunting);
                    Assert.False(ufo.Escort);
                    Assert.False(ufo.Escorting);
                    Assert.Null(ufo.OriginalWaypoint);
                    Assert.Equal(WorldTargetKind.Waypoint, ufo.Destination!.Kind);
                    Assert.Equal(1, ufo.TrajectoryPoint);
                    Assert.Equal(wave, ufo.MissionWaveNumber);
                    Assert.Equal(1760, ufo.Speed);
                    Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), ufo.Position);
                    Assert.Equal(10, ufo.Shield);
                }
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "idle-hunter-first-arrival.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2, ufo.TrajectoryPoint));
            }
        }
        Assert.Equal(percentage == 100 ? 4 : 5, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombatAndEscortCapacityRetainAtomicBoundaries(bool escort)
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false, hunterPercentage: 50, escort: escort, radarRange: 2000);
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17);
        if (escort)
            initial = (initial with { Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [] }] })
                .WithNextId("STR_UFO_UNIQUE", int.MaxValue - 2);
        else
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Speed = 0,
                    Fuel = 100,
                    Longitude = Degrees(8),
                    Latitude = Degrees(2),
                    Destination = WorldTargetReference.ForWaypoint(5, new(Degrees(30), Degrees(2))),
                },
            });
        var restored = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        if (escort) AssertTimeBlocked(restored, "UFO unique IDs are exhausted or collide with a saved UFO.");
        else AssertNewbornHuntRuns(restored);
    }

    [Fact]
    public void NoTargetArrivalCannotHideInvalidCraftAttraction()
    {
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false, hunterPercentage: 100,
            extraRules: "crafts: [{type: SHIP, soldiers: 2147483647}]");
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 17).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Speed = 0,
                Fuel = 100,
                Longitude = Degrees(20),
                Latitude = Degrees(2),
                Destination = WorldTargetReference.ForWaypoint(5, new(Degrees(30), Degrees(2))),
            },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft hunter attraction exceeds the supported range.");
    }
}
