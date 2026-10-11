using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldGlobeQueryTests
{
    [Fact]
    public void WorldQueryCarriesTheRadarAndFlightPathInputsTheGlobeDraws()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var source = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0), "UFO_HUNTER").Capture();
        var owner = source.Bases[0];
        var campaign = CampaignState.Restore(source with
        {
            Bases =
            [
                owner with
                {
                    Facilities =
                    [
                        .. owner.Facilities,
                        new("RADAR_TEST", 5, 5, 0, 0, false, false, false),
                        // Under construction: Globe::drawRadars counts only completed facilities.
                        new("RADAR_OVER_TEST", 4, 5, 3, 0, false, false, false),
                    ],
                },
            ],
            World = source.World with
            {
                Ufos = [source.World.Ufos[0] with { Detected = true, HyperDetected = true, HunterKiller = true, Hunting = true }],
            },
        }, content, new SplitMix64RandomSource(1));

        var world = campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld();

        Assert.Equal(1000, Assert.Single(world.Bases).RadarRange);
        var ufo = Assert.Single(world.Targets, target => target.Kind == WorldTargetKind.Ufo);
        var rule = content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("UFO_HUNTER")].Value;
        Assert.Equal((true, true, true), (ufo.HunterKiller, ufo.HyperDetected, ufo.Hunting));
        Assert.Equal(rule.StatsForRace("RACE_A").RadarRange, ufo.RadarRange);
        Assert.True(ufo.RadarRange > 0);
        // An unresolved UFO destination keeps its recorded coordinates.
        Assert.Equal(new WorldPosition(1.0, 0.6), ufo.Destination);
        Assert.All(world.Crafts, craft =>
        {
            var craftRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired(craft.TypeId)].Value;
            Assert.Equal(craftRule.RadarRange, craft.RadarRange);
        });
    }
}
