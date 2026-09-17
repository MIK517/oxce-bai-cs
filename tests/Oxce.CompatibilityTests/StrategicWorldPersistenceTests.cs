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

        // Craft::load leaves a craft whose destination is gone without one instead of failing.
        var stale = CampaignState.Restore(
            snapshot with
            {
                Bases = [snapshot.Bases[0] with
                {
                    Crafts = [flying with { Logistics = flying.Logistics! with
                    {
                        Destination = new WorldTargetReference(WorldTargetKind.Ufo, "STR_UFO", 1, 0, 0) { UniqueId = 404 },
                    } }, .. snapshot.Bases[0].Crafts.Skip(1)],
                }],
            },
            content, new SplitMix64RandomSource(7));
        Assert.Null(stale.Capture().Bases[0].Crafts[0].Logistics!.Destination);

        // A craft bound for "STR_BASE" returns to its own base, whatever ID the save recorded.
        var home = CampaignState.Restore(
            snapshot with
            {
                Bases = [snapshot.Bases[0] with
                {
                    Crafts = [flying with { Logistics = flying.Logistics! with
                    {
                        Destination = new WorldTargetReference(WorldTargetKind.Base, WorldTargetReference.BaseType, 99, 0, 0),
                    } }, .. snapshot.Bases[0].Crafts.Skip(1)],
                }],
            },
            content, new SplitMix64RandomSource(7)).Capture().Bases[0].Crafts[0].Logistics!.Destination;
        Assert.Equal(WorldTargetKind.Base, home!.Kind);
        Assert.Equal(snapshot.Bases[0].Id, home.Id);
        Assert.Equal(snapshot.Bases[0].Longitude, home.Longitude);
    }

    [Fact]
    public void CraftTargetsSurviveTheSaveRoundTrip()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var placed = PlacedCampaign(content, CampaignDifficulty.Veteran);
        var crafts = placed.Bases[0].Crafts;
        var escorted = crafts[1];
        var escort = crafts[0] with
        {
            Logistics = crafts[0].Logistics! with
            {
                Status = "STR_OUT",
                // Craft::finishLoading resolves an escorting craft by craft type and ID.
                Destination = new WorldTargetReference(WorldTargetKind.Craft, escorted.RuleId, escorted.Id, 0.2, 0.1),
            },
        };
        var world = SampleWorld();
        var hunter = world.Ufos[0] with
        {
            Hunting = true,
            HunterKiller = true,
            // Ufo::finishLoading resolves a hunting UFO's destination to the craft it chases.
            Destination = new WorldTargetReference(WorldTargetKind.Craft, escort.RuleId, escort.Id, 0.2, 0.1),
        };
        var snapshot = placed with
        {
            World = world with { Ufos = [hunter] },
            Bases = [placed.Bases[0] with { Crafts = [escort, .. crafts.Skip(1)] }],
        };

        var captured = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7)).Capture();
        var reloaded = TestFixtures.LoadLogisticsSave(
            OxceSaveAdapter.EmitNewCampaign(captured), content, seed: 6, name: "hunt.sav").Campaign.Capture();

        var reloadedEscort = reloaded.Bases[0].Crafts[0].Logistics!.Destination;
        Assert.Equal(WorldTargetKind.Craft, reloadedEscort!.Kind);
        Assert.Equal(escorted.RuleId, reloadedEscort.TypeId);
        Assert.Equal(escorted.Id, reloadedEscort.Id);
        var reloadedHunter = reloaded.World.Ufos[0].Destination;
        Assert.Equal(WorldTargetKind.Craft, reloadedHunter!.Kind);
        Assert.Equal(escort.RuleId, reloadedHunter.TypeId);
        Assert.True(reloaded.World.Ufos[0].Hunting);
    }

    [Fact]
    public void PassiveUfoKeepsItsSavedDestinationAsAnAnonymousWaypoint()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var placed = PlacedCampaign(content, CampaignDifficulty.Veteran);
        var world = SampleWorld();
        var craft = placed.Bases[0].Crafts[0];
        var ufo = world.Ufos[0] with
        {
            Destination = new WorldTargetReference(WorldTargetKind.Craft, craft.RuleId, craft.Id, 0.31, 0.12),
        };
        var snapshot = placed with { World = world with { Ufos = [ufo] } };

        var captured = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7)).Capture();
        var destination = Assert.Single(captured.World.Ufos).Destination;
        Assert.Equal(WorldTargetKind.Waypoint, destination!.Kind);
        Assert.Equal(0, destination.Id);
        Assert.Equal(0.31, destination.Longitude);
        Assert.Equal(0.12, destination.Latitude);

        var reloaded = TestFixtures.LoadLogisticsSave(
            OxceSaveAdapter.EmitNewCampaign(snapshot), content, seed: 11, name: "passive-ufo.sav");
        Assert.Equal(destination, Assert.Single(reloaded.Campaign.Capture().World.Ufos).Destination);
    }

    [Fact]
    public void LegacyTerrorSitesAndTheirCraftDestinationsAreImported()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var placed = PlacedCampaign(content, CampaignDifficulty.Veteran);
        var crafts = placed.Bases[0].Crafts;
        var chasing = crafts[0] with
        {
            Logistics = crafts[0].Logistics! with
            {
                Status = "STR_OUT",
                Destination = new WorldTargetReference(WorldTargetKind.MissionSite, "STR_TERROR_SITE", 6, 0.14, 0.03),
            },
        };
        var snapshot = placed with { Bases = [placed.Bases[0] with { Crafts = [chasing, .. crafts.Skip(1)] }] };
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot)
            // A legacy save records the site under terrorSites and its destination under the old name.
            .Replace("type: STR_TERROR_SITE", "type: STR_ALIEN_TERROR", StringComparison.Ordinal) +
            """
            terrorSites:
              - lon: 0.14
                lat: 0.03
                id: 6
                race: RACE_A
                secondsRemaining: 3600
            """ + "\n";

        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 8, name: "legacy.sav");
        var restored = loaded.Campaign.Capture();

        var site = Assert.Single(restored.World.MissionSites);
        Assert.Equal(6, site.Id);
        Assert.Equal("STR_ALIEN_TERROR", site.MissionRuleId);
        Assert.Equal("STR_TERROR_MISSION", site.DeploymentId);
        Assert.Equal(3600, site.SecondsRemaining);
        var destination = restored.Bases[0].Crafts[0].Logistics!.Destination;
        Assert.Equal(WorldTargetKind.MissionSite, destination!.Kind);
        Assert.Equal("STR_TERROR_SITE", destination.TypeId);
        Assert.Equal(6, destination.Id);

        // The rewrite emits the site under missionSites and drops the legacy node.
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(restored, loaded.Source);
        Assert.DoesNotContain("terrorSites:", rewritten, StringComparison.Ordinal);
        Assert.Contains("missionSites:", rewritten, StringComparison.Ordinal);
        Assert.Equivalent(restored.World,
            TestFixtures.LoadLogisticsSave(rewritten, content, seed: 9, name: "legacy.sav").Campaign.Capture().World,
            strict: true);
    }

    [Fact]
    public void ModernMissionSiteStillRequiresItsType()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var yaml = OxceSaveAdapter.EmitNewCampaign(PlacedCampaign(content)) +
            "missionSites:\n  - id: 6\n    lon: 0.14\n    lat: 0.03\n";

        Assert.Throws<InvalidDataException>(() =>
            TestFixtures.LoadLogisticsSave(yaml, content, seed: 10, name: "untyped-site.sav"));
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
