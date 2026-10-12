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
                AlienBases = [new AlienBaseSnapshot(5, "ALIEN_BASE_DEPLOY", "RACE_A", owner.Longitude, owner.Latitude) { Discovered = true }],
            },
        }, content, new SplitMix64RandomSource(1));

        var world = campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld();

        Assert.Equal(1000, Assert.Single(world.Bases).RadarRange);
        Assert.Equal(campaign.Capture().Countries.Select(country => country.RuleId), world.CountryIds);
        Assert.Equal(campaign.Capture().Regions.Select(region => region.RuleId), world.RegionIds);
        Assert.NotEmpty(world.RegionIds);
        var ufo = Assert.Single(world.Targets, target => target.Kind == WorldTargetKind.Ufo);
        var rule = content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("UFO_HUNTER")].Value;
        Assert.Equal((true, true, true), (ufo.HunterKiller, ufo.HyperDetected, ufo.Hunting));
        Assert.Equal(rule.StatsForRace("RACE_A").RadarRange, ufo.RadarRange);
        Assert.True(ufo.RadarRange > 0);
        // UfoDetectedState's lists: rule size, heading and speed, then the mission's race, rule and region.
        var stored = campaign.Capture().World.Ufos[0];
        var mission = campaign.Capture().World.Missions.Single(entry => entry.Id == stored.MissionId);
        Assert.Equal(new CampaignWorldUfoDetails(rule.Size, stored.Status == UfoStatus.Flying ? stored.Direction : "STR_NONE_UC",
            stored.Speed, mission.Race, mission.RuleId, mission.RegionId), ufo.Ufo);
        // AlienBaseState: the first country and region containing the base, in saved order.
        var alienBase = Assert.Single(world.Targets, target => target.Kind == WorldTargetKind.AlienBase);
        var position = new WorldPosition(owner.Longitude, owner.Latitude);
        var rules = content.RuntimeRules;
        Assert.Equal(world.RegionIds.FirstOrDefault(id =>
            WorldGeometry.InsideRegion(rules.Regions[rules.Regions.GetRequired(id)].Value, position)) ?? "", alienBase.RegionId);
        Assert.Equal(world.CountryIds.FirstOrDefault(id =>
            WorldGeometry.InsideCountry(rules.Countries[rules.Countries.GetRequired(id)].Value, position)) ?? "", alienBase.CountryId);
        Assert.NotEqual("", alienBase.RegionId);
        // An unresolved UFO destination keeps its recorded coordinates.
        Assert.Equal(new WorldPosition(1.0, 0.6), ufo.Destination);
        Assert.All(world.Crafts, craft =>
        {
            var craftRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired(craft.TypeId)].Value;
            Assert.Equal(craftRule.RadarRange, craft.RadarRange);
        });
    }
}
