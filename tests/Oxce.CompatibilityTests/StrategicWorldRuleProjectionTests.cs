using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Checks the runtime projection of the world entity rules a strategic campaign needs:
/// globe textures, region mission zones, UFOs and their race bonuses, trajectories,
/// alien missions and alien deployments.
/// </summary>
public sealed class StrategicWorldRuleProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldEntityRulesProjectIntoTheRuntimeCatalog(bool fromCache)
    {
        var rules = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache).RuntimeRules;

        var globe = rules.Campaign.Globe;
        Assert.Equal(3, globe.Polygons.Count);
        Assert.Equal<string>(["SITE_DEPLOY", "SITE_DEPLOY_B"], [.. globe.Textures[10].Deployments.Keys]);
        Assert.Equal(3UL, globe.Textures[10].Deployments["SITE_DEPLOY"]);
        Assert.True(globe.Textures[11].FakeUnderwater);
        Assert.True(globe.Textures[12].IsOcean);
        Assert.Empty(globe.Textures[12].Deployments);

        var region = rules.Regions[rules.Regions.GetRequired("REGION")].Value;
        Assert.Equal(3, region.MissionZones.Count);
        Assert.Equal(10UL, region.RegionWeight);
        Assert.Equal(4UL, region.MissionWeights["MISSION_SCOUT"]);
        Assert.False(region.MissionZones[0].Areas[0].IsPoint);
        var cities = region.MissionZones[1].Areas;
        Assert.All(cities, area => Assert.True(area.IsPoint));
        Assert.Equal<string>(["CITY_ALPHA", "CITY_BETA"], [.. cities.Select(area => area.Name)]);
        Assert.Equal(10, cities[0].Texture);

        var scout = rules.Ufos[rules.Ufos.GetRequired("UFO_SCOUT")].Value;
        Assert.Equal("STR_SMALL", scout.Size);
        // RuleUfo::getDefaultVisibility uses the vanilla size ladder when visibility is unset.
        Assert.Equal(-15, scout.DefaultVisibility);
        Assert.Equal(3, scout.MissionScore);
        Assert.Equal(20, scout.FakeWaterLandingChance);
        Assert.Equal(2200, scout.Stats.SpeedMaximum);
        Assert.Equal(2500, scout.StatsForRace("RACE_B").SpeedMaximum);
        Assert.Equal(60, scout.StatsForRace("RACE_B").DamageMaximum);
        Assert.Equal(2200, scout.StatsForRace("RACE_A").SpeedMaximum);
        Assert.Equal(0, scout.HunterKillerPercentage);

        var hunter = rules.Ufos[rules.Ufos.GetRequired("UFO_HUNTER")].Value;
        Assert.Equal(22, hunter.DefaultVisibility);
        Assert.Equal(100, hunter.HunterKillerPercentage);
        Assert.Equal(1, hunter.HuntMode);
        Assert.Equal(0, hunter.HuntBehavior);
        Assert.Equal(80, hunter.HuntSpeed);
        Assert.True(hunter.Unmanned);
        Assert.True(hunter.NoAlert);

        var patrol = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired("TRAJ_PATROL")].Value;
        Assert.Equal(12, patrol.GroundTimer);
        Assert.Equal(4, patrol.Waypoints.Count);
        Assert.Equal(1, patrol.Zone(1));
        Assert.Equal(2, patrol.Altitude(1));
        Assert.Equal(1760, patrol.Speed(1, 2200));
        Assert.Equal(0, patrol.Altitude(2));
        Assert.True(rules.UfoTrajectories.TryGet(RuntimeUfoTrajectoryRule.RetaliationAssaultRun, out _));

        var scoutMission = rules.AlienMissions[rules.AlienMissions.GetRequired("MISSION_SCOUT")].Value;
        Assert.Equal(RuntimeMissionObjective.Score, scoutMission.Objective);
        Assert.Equal(2, scoutMission.Points);
        Assert.Equal(2, scoutMission.Waves.Count);
        Assert.Equal(2UL, scoutMission.Waves[0].UfoCount);
        Assert.Equal(9000UL, scoutMission.Waves[0].SpawnTimer);
        Assert.NotNull(scoutMission.Waves[0].Ufo);
        Assert.Null(scoutMission.Waves[0].Deployment);
        Assert.NotNull(scoutMission.Waves[0].Trajectory);
        Assert.True(scoutMission.HasRaceWeights);
        Assert.False(scoutMission.HasRegionWeights);
        Assert.Equal(10UL, scoutMission.RaceWeights[0].Weights["RACE_A"]);
        Assert.True(scoutMission.Waves[1].Escort);
        Assert.Equal(50, scoutMission.Waves[1].InterruptPercentage);

        var siteMission = rules.AlienMissions[rules.AlienMissions.GetRequired("MISSION_SITE")].Value;
        Assert.Equal(RuntimeMissionObjective.Site, siteMission.Objective);
        Assert.Equal(1, siteMission.SpawnZone);
        Assert.Equal("SITE_DEPLOY", siteMission.SiteTypeId);
        Assert.NotNull(siteMission.SiteType);
        Assert.True(siteMission.Waves[0].Objective);

        var baseMission = rules.AlienMissions[rules.AlienMissions.GetRequired("MISSION_BASE")].Value;
        Assert.Equal(RuntimeMissionObjective.Base, baseMission.Objective);
        Assert.True(baseMission.Waves[0].ObjectiveOnTheLandingSite);

        var retaliation = rules.AlienMissions[rules.AlienMissions.GetRequired("MISSION_RETALIATION")].Value;
        Assert.Equal(RuntimeMissionObjective.Retaliation, retaliation.Objective);
        Assert.NotNull(retaliation.SpawnUfo);
        Assert.NotNull(retaliation.InterruptResearch);

        var supply = rules.AlienMissions[rules.AlienMissions.GetRequired("MISSION_SUPPLY")].Value;
        Assert.Equal(RuntimeMissionObjective.Supply, supply.Objective);
        Assert.Equal(RuntimeMissionOperationType.RegionExistingBase, supply.OperationType);
        Assert.Equal(0, supply.OperationSpawnZone);
        Assert.NotNull(supply.OperationBaseType);

        var site = rules.AlienDeployments[rules.AlienDeployments.GetRequired("SITE_DEPLOY")].Value;
        Assert.Equal("STR_TERROR_SITE", site.MarkerName);
        Assert.Equal(8, site.MarkerIcon);
        Assert.Equal(3, site.DurationMinimum);
        Assert.Equal(6, site.DurationMaximum);
        Assert.Equal(4, site.Points);
        Assert.Equal(9, site.DespawnPenalty);
        Assert.False(site.IsAlienBase);
        Assert.NotNull(site.UnlockedResearchOnDespawn);
        Assert.Equal("sitesDespawned", site.CounterDespawn);
        Assert.Equal("sitesAll", site.CounterAll);
        Assert.Equal("sitesWon", site.DecreaseCounterFailure);
        Assert.Equal(5UL, site.DespawnEvents["EVENT_DESPAWN"]);

        var alienBase = rules.AlienDeployments[rules.AlienDeployments.GetRequired("ALIEN_BASE_DEPLOY")].Value;
        Assert.True(alienBase.IsAlienBase);
        Assert.Equal(25, alienBase.FakeUnderwaterSpawnChance);
        Assert.Equal(600, alienBase.BaseDetectionRange);
        Assert.Equal(40, alienBase.BaseDetectionChance);
        Assert.Equal(90, alienBase.HuntMissionMaxFrequency);
        Assert.Equal(6, alienBase.GenMissionFrequency);
        Assert.Equal(3, alienBase.GenMissionLimit);
        Assert.Equal(10UL, alienBase.GenMission["MISSION_SUPPLY"]);
        Assert.NotNull(alienBase.BaseSelfDestructCode);
        var evolution = Assert.Single(alienBase.AlienRaceEvolution);
        Assert.Equal(new RuntimeAlienRaceEvolution(6, "RACE_A", "RACE_B"), evolution);
        // AlienDeployment::generateHuntMission picks the newest timeline entry at or below the month.
        Assert.Equal(5UL, RuntimeAlienDeploymentRule.Timeline(alienBase.HuntMissions, 0)!["MISSION_SCOUT"]);
        Assert.Equal(9UL, RuntimeAlienDeploymentRule.Timeline(alienBase.HuntMissions, 7)!["MISSION_SITE"]);
        Assert.Equal(100UL, RuntimeAlienDeploymentRule.Timeline(alienBase.AlienBaseUpgrades, 2)!["ALIEN_BASE_DEPLOY_UPGRADED"]);
        Assert.Null(RuntimeAlienDeploymentRule.Timeline(site.HuntMissions, 3));

        Assert.True(rules.AlienRaces.TryGet("RACE_A", out _));
        Assert.True(rules.AlienRaces.TryGet("RACE_B", out _));
    }
}
