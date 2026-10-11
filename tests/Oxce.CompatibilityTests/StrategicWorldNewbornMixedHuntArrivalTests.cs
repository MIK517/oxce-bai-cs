using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornMixedHuntArrivalTests
{
    private static readonly CampaignTimeSpeed[] Speeds = [CampaignTimeSpeed.FiveSeconds, CampaignTimeSpeed.OneDay];

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    public void ConstructorsChooseBetweenSafePursuitAndOrdinaryFirstArrival(int huntSpeed)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicWorldNewbornFirstArrivalTests.Content(false, hunterPercentage: 50,
            radarRange: 300, huntSpeed: huntSpeed);
        HashSet<int> patterns = [];
        foreach (var speed in Speeds)
            for (ulong seed = 1; seed <= 64; seed++)
            {
                var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed).WithCraft("SHIP", craft => craft with
                {
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_OUT",
                        Speed = 0,
                        Fuel = 300,
                        Longitude = Degrees(8) + WorldGeometry.Nautical(100),
                        Latitude = Degrees(2),
                        Destination = WorldTargetReference.ForWaypoint(5, new(Degrees(30), Degrees(2))),
                    },
                });
                var expectedRandom = new SplitMix64RandomSource(seed);
                List<(bool Hunter, int Mode, int Behavior)> choices = [];
                var pattern = 0;
                for (var wave = 0; wave < 3; wave++)
                {
                    var hunter = expectedRandom.NextInclusive(0, 99) < 50;
                    choices.Add((hunter, hunter ? expectedRandom.NextInclusive(0, 1) : 0,
                        hunter ? expectedRandom.NextInclusive(0, 1) : 0));
                    if (hunter) pattern |= 1 << wave;
                    for (var draw = 0; draw < 6; draw++) expectedRandom.NextUnit();
                    if (wave < 2) expectedRandom.NextUnit();
                }
                // Detection precedes retargeting; only ordinary births select another waypoint.
                for (var draw = 0; draw < 6 + choices.Count(choice => !choice.Hunter) * 6; draw++) expectedRandom.NextUnit();
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                var result = campaign.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = speed });
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.Equal(expectedRandom.State, after.RandomState);
                Assert.Equal(choices.Count(choice => choice.Hunter), result.Events.OfType<UfoStartedHunting>().Count());
                Assert.Equal(3, after.World.Ufos.Count);
                var target = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP");
                for (var wave = 0; wave < 3; wave++)
                {
                    var born = after.World.Ufos[wave];
                    var choice = choices[wave];
                    Assert.Equal(choice, (born.HunterKiller, born.HuntMode, born.HuntBehavior));
                    Assert.Equal(choice.Hunter, born.Hunting);
                    Assert.False(born.Escorting);
                    Assert.Equal(choice.Hunter ? 0 : 1, born.TrajectoryPoint);
                    Assert.Equal(wave, born.MissionWaveNumber);
                    Assert.Equal(choice.Hunter ? huntSpeed == 0 ? 2200 : 3300 : 1760, born.Speed);
                    Assert.Equal(10, born.Shield);
                    if (choice.Hunter)
                    {
                        Assert.True(born.Detected);
                        Assert.True(born.Id > 0);
                        Assert.Equal(WorldTargetKind.Craft, born.Destination!.Kind);
                        Assert.Equal(target.Id, born.Destination.Id);
                        Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), born.OriginalWaypoint);
                        var moved = WorldGeometry.Move(new(Degrees(8), Degrees(2)),
                            new(target.Logistics!.Longitude, target.Logistics.Latitude), WorldGeometry.RadianSpeed(born.Speed));
                        Assert.Equal(moved.Longitude, born.Longitude, 12);
                        Assert.Equal(moved.Latitude, born.Latitude, 12);
                    }
                    else
                    {
                        Assert.Null(born.OriginalWaypoint);
                        Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
                        Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), born.Position);
                    }
                }
                patterns.Add(pattern);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "mixed-hunter-first-arrival.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(ufo.HunterKiller ? 0 : 2, ufo.TrajectoryPoint));
            }
        Assert.Equal(8, patterns.Count);
    }
}
