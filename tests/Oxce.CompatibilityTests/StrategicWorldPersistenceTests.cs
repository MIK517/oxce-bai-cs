using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// The strategic target graph: alien missions, UFOs, mission sites, alien bases, waypoints,
/// scheduled events and the alien strategy table survive capture, save and reload with their
/// identities and cross-references intact, and a broken graph fails instead of being published.
/// </summary>
public sealed class StrategicWorldPersistenceTests
{
    [Fact]
    public void WorldGraphSurvivesCaptureSaveAndReload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = PlacedCampaign(content) with { World = SampleWorld() };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7));
        var captured = campaign.Capture();
        Assert.Equivalent(snapshot.World, captured.World, strict: true);

        var yaml = OxceSaveAdapter.EmitNewCampaign(captured);
        Assert.Contains("alienMissions:", yaml, StringComparison.Ordinal);
        Assert.Contains("uniqueID: 4", yaml, StringComparison.Ordinal);
        Assert.Contains("missionSites:", yaml, StringComparison.Ordinal);
        Assert.Contains("alienStrategy:", yaml, StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 3, name: "world.sav");
        var reloaded = loaded.Campaign.Capture();
        Assert.Equivalent(captured.World, reloaded.World, strict: true);

        var rewritten = TestFixtures.LoadLogisticsSave(
            OxceSaveAdapter.EmitLoadedCampaign(reloaded, loaded.Source), content, seed: 4, name: "world.sav");
        Assert.Equivalent(reloaded.World, rewritten.Campaign.Capture().World, strict: true);
    }

    [Fact]
    public void UnknownWorldRulesAreDroppedLikeTheReference()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var world = SampleWorld();
        var snapshot = PlacedCampaign(content) with
        {
            World = world with
            {
                MissionSites = [.. world.MissionSites, world.MissionSites[0] with { Id = 77, DeploymentId = "GONE_DEPLOY" }],
                AlienBases = [.. world.AlienBases, world.AlienBases[0] with { Id = 78, DeploymentId = "GONE_DEPLOY" }],
                Events = [.. world.Events, new GeoscapeEventSnapshot("GONE_EVENT", 60)],
                Strategy = world.Strategy with
                {
                    RegionMissions = [.. world.Strategy.RegionMissions,
                        new("GONE_REGION", [new KeyValuePair<string, ulong>("MISSION_SCOUT", 4)])],
                },
            },
        };

        var restored = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7)).Capture().World;

        Assert.Equal(world.MissionSites.Count, restored.MissionSites.Count);
        Assert.Equal(world.AlienBases.Count, restored.AlienBases.Count);
        Assert.Equal(world.Events.Count, restored.Events.Count);
        Assert.DoesNotContain(restored.Strategy.RegionMissions, entry => entry.Key == "GONE_REGION");
        // The unknown region keeps its recorded weight, exactly like AlienStrategy::load.
        Assert.Equivalent(snapshot.World.Strategy.RegionChances, restored.Strategy.RegionChances, strict: true);
    }

    [Fact]
    public void BrokenWorldReferencesFailInsteadOfPublishing()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var placed = PlacedCampaign(content);
        var world = SampleWorld();

        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            Missions = [world.Missions[0] with { AlienBase = new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 99, 0, 0) }],
        }));
        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            Ufos = [world.Ufos[0] with { MissionId = 55 }],
            MissionSites = [],
        }));
        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            Ufos = [.. world.Ufos, world.Ufos[0]],
        }));
        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            Ufos = [world.Ufos[0] with { TrajectoryPoint = 9 }],
        }));
        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            Ufos = [world.Ufos[0] with { Altitude = "STR_ORBIT" }],
        }));
        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            MissionSites = [world.MissionSites[0] with { UfoUniqueId = 4242 }],
        }));
        Assert.Throws<InvalidDataException>(() => Restore(placed, content, world with
        {
            AlienBases = [world.AlienBases[0] with { Longitude = 42 }],
            Missions = [world.Missions[0] with { AlienBase = null }],
        }));
    }

    [Fact]
    public void LiveWorldStateStopsTimeWithoutPartialMutation()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CampaignState.Restore(
            PlacedCampaign(content) with { World = SampleWorld() }, content, new SplitMix64RandomSource(7));
        var before = campaign.Capture();

        var events = campaign.Execute(new AdvanceCampaignTime(12)).Events;

        var advanced = Assert.IsType<CampaignTimeAdvanced>(events[0]);
        Assert.Equal(0, advanced.Summary.TickCount);
        var blocked = Assert.IsType<CampaignActionBlocked>(events[^1]);
        Assert.Equal("UFO movement requires world simulation.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        var overview = campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld();
        Assert.Equal(1, overview.ActiveMissions);
        Assert.Equal(1, overview.ScheduledEvents);
        Assert.Contains(overview.Targets, target => target.Kind == WorldTargetKind.Ufo && target.Detected);
        Assert.Contains(overview.Targets, target => target.Kind == WorldTargetKind.MissionSite);
        Assert.Contains(overview.Targets, target => target.Kind == WorldTargetKind.AlienBase);
        Assert.Contains(overview.Targets, target => target.Kind == WorldTargetKind.Waypoint);
    }

    [Fact]
    public void CraftDestinationsAreResolvedAgainstTheRestoredGraph()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        // The veteran starting base is the fixture variant that comes with craft.
        var placed = PlacedCampaign(content, CampaignDifficulty.Veteran);
        var world = SampleWorld();
        var craft = placed.Bases[0].Crafts[0];
        var flying = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Destination = new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 3, 0.2, 0.1),
                Takeoff = 42,
                MissionComplete = true,
                InterceptionOrder = 2,
                AutoPatrolLongitude = 0.3,
                AutoPatrolLatitude = -0.1,
                Speed = 760,
                SpeedLongitude = 0.0004,
                SpeedLatitude = -0.0002,
                SpeedRadian = 0.0005,
            },
        };
        var snapshot = placed with
        {
            World = world,
            Bases = [placed.Bases[0] with { Crafts = [flying, .. placed.Bases[0].Crafts.Skip(1)] }],
        };

        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7));
        var reloaded = TestFixtures.LoadLogisticsSave(
            OxceSaveAdapter.EmitNewCampaign(campaign.Capture()), content, seed: 5, name: "dispatch.sav");
        var state = reloaded.Campaign.Capture().Bases[0].Crafts[0].Logistics!;
        Assert.Equal(flying.Logistics!.Destination, state.Destination);
        Assert.Equal(42, state.Takeoff);
        Assert.True(state.MissionComplete);
        Assert.Equal(2, state.InterceptionOrder);
        Assert.Equal(0.3, state.AutoPatrolLongitude);
        Assert.Equal(760, state.Speed);
        Assert.Equal(0.0005, state.SpeedRadian);

        // A destination whose target is gone is rejected rather than silently kept.
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(
            snapshot with
            {
                Bases = [snapshot.Bases[0] with
                {
                    Crafts = [flying with { Logistics = flying.Logistics! with
                    {
                        Destination = new WorldTargetReference(WorldTargetKind.Ufo, "STR_UFO", 1, 0, 0) { UniqueId = 404 },
                    } }],
                }],
            },
            content, new SplitMix64RandomSource(7)));
    }

    private static CampaignState Restore(CampaignSnapshot placed, RuntimeContent content, WorldSnapshot world) =>
        CampaignState.Restore(placed with { World = world }, content, new SplitMix64RandomSource(7));

    private static CampaignSnapshot PlacedCampaign(
        RuntimeContent content, CampaignDifficulty difficulty = CampaignDifficulty.Beginner)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "World", difficulty);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        return campaign.Capture();
    }

    private static WorldSnapshot SampleWorld() => new()
    {
        Missions =
        [
            new AlienMissionSnapshot(4, "MISSION_SITE", "REGION", "RACE_A", 1, 0, 1500, 1, 0)
            {
                AlienBase = new WorldTargetReference(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 2, 0.5, 0.2),
            },
        ],
        Ufos =
        [
            new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_SITE", 1, 0.4, 0.15, UfoStatus.Flying, "STR_HIGH_UC")
            {
                Id = 3,
                Detected = true,
                Speed = 2200,
                SpeedRadian = 0.0005,
                MissionWaveNumber = 0,
                Destination = new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, 0.45, 0.2),
            },
        ],
        Waypoints = [new WaypointSnapshot(3, 0.2, 0.1)],
        MissionSites =
        [
            new MissionSiteSnapshot(5, "MISSION_SITE", "SITE_DEPLOY", "RACE_A", 0.14, 0.03, 7200)
            {
                Detected = true,
                Texture = 10,
                City = "CITY_ALPHA",
                UfoUniqueId = 9,
            },
        ],
        AlienBases =
        [
            new AlienBaseSnapshot(2, "ALIEN_BASE_DEPLOY", "RACE_A", 0.5, 0.2)
            {
                Discovered = true,
                StartMonth = 1,
                GenMissionCount = 2,
                MinutesSinceLastHuntMissionGeneration = 30,
            },
        ],
        Events = [new GeoscapeEventSnapshot("EVENT_DESPAWN", 60)],
        Strategy = new AlienStrategySnapshot
        {
            RegionChances = [new("REGION", 10), new("GONE_REGION", 3)],
            RegionMissions = [new("REGION", [new KeyValuePair<string, ulong>("MISSION_SCOUT", 4)])],
            MissionRuns = [new("scouts", 2)],
            MissionLocations = [new("scouts", [new AlienStrategyState.MissionLocation("REGION", 1)])],
        },
    };
}
