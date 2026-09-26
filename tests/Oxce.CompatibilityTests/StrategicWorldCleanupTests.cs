using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>GeoscapeState::time5Seconds/time30Minutes, Ufo::~Ufo and AlienMission::isOver.</summary>
public sealed class StrategicWorldCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupReleasesEachMissionOnceAndKeepsSurvivors(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var snapshot = CreateLifecycleSnapshot(content);
        var mission = snapshot.World.Missions[0];
        var ufo = snapshot.World.Ufos[0];
        var campaign = Restore(content, snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 50),
            World = snapshot.World with
            {
                Missions = [mission with { LiveUfos = 3 }, mission with { Id = 5, Interrupted = true }],
                Ufos = [ufo with { Status = UfoStatus.Destroyed },
                    ufo with { UniqueId = 10, Id = 4, Status = UfoStatus.Destroyed },
                    ufo with { UniqueId = 11, Id = 5, SecondsRemaining = 1800 },
                    ufo with { UniqueId = 12, Id = 6, MissionId = 5, Status = UfoStatus.Destroyed }],
            },
        });
        AdvanceUnblocked(campaign, 1);
        var cleaned = campaign.Capture();
        Assert.Equal(11, Assert.Single(cleaned.World.Ufos).UniqueId);
        Assert.Equal<int>([1, 0], cleaned.World.Missions.Select(item => item.LiveUfos));
        campaign = Reload(content, campaign, 17, "cleanup.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(4, Assert.Single(campaign.Capture().World.Missions).Id);
        Assert.Equal(1, campaign.Capture().World.Missions[0].LiveUfos);
        Assert.Equal(11, Assert.Single(campaign.Capture().World.Ufos).UniqueId);
    }

    [Theory]
    [InlineData(9, 50)]
    [InlineData(29, 50)]
    public void ConsecutiveDeparturesDeferCraftsThenDrainAcrossBoundaries(int minute, int second)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        snapshot = snapshot.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Fuel = 100 } });
        var ufo = snapshot.World.Ufos[0];
        var arrived = ufo with
        {
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.VeryLow,
            TrajectoryPoint = 3,
            Speed = 2200,
            Longitude = ufo.Destination!.Longitude,
            Latitude = ufo.Destination.Latitude,
        };
        var campaign = Restore(content, snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, minute, second),
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = [arrived, arrived with { UniqueId = 10, Id = 4 }],
            },
        });
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(0, "SHIP", 1, 0.5, 0.1)).Events));
        var batched = Restore(content, campaign.Capture());
        var takeoff = Ship(campaign.Capture()).Takeoff;
        for (var index = 0; index < 2; index++)
        {
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(index + 1, after.World.Ufos.Count(item => item.Status == UfoStatus.Destroyed));
            Assert.Equal(2, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(takeoff, Ship(after).Takeoff);
            campaign = Reload(content, campaign, 17, "cleanup.sav");
        }
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
        Assert.Equal(takeoff - 1, Ship(campaign.Capture()).Takeoff);
        Assert.Single(campaign.Capture().World.Waypoints);
        AdvanceUnblocked(batched, 3);
        Assert.Equivalent(batched.Capture(), campaign.Capture(), strict: true);
    }

    [Fact]
    public void DeletedWorldSidecarsStayDeletedAcrossRepeatedLoadedRewrites()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 50),
            World = snapshot.World with { Ufos = [snapshot.World.Ufos[0] with { Status = UfoStatus.Destroyed }] },
        };
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        Assert.Contains("uniqueId: 9\n", yaml, StringComparison.Ordinal);
        Assert.Contains("uniqueID: 4\n", yaml, StringComparison.Ordinal);
        yaml = yaml.Replace("uniqueId: 9\n", "uniqueId: 9\n    futureUfo: departed\n", StringComparison.Ordinal)
            .Replace("uniqueID: 4\n", "uniqueID: 4\n    futureMission: expired\n", StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 17, name: "cleanup-sidecars.sav");
        AdvanceUnblocked(loaded.Campaign, 2);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
            Assert.DoesNotContain("futureUfo", rewritten, StringComparison.Ordinal);
            Assert.DoesNotContain("futureMission", rewritten, StringComparison.Ordinal);
            loaded = TestFixtures.LoadLogisticsSave(rewritten, content, seed: 17, name: "cleanup-sidecars.sav");
            Assert.Empty(loaded.Campaign.Capture().World.Ufos);
            Assert.Empty(loaded.Campaign.Capture().World.Missions);
        }
        var recreated = loaded.Campaign.Capture() with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { Id = 5 }],
                Ufos = [snapshot.World.Ufos[0] with { UniqueId = 10, MissionId = 5 }],
            }
        };
        var newYaml = OxceSaveAdapter.EmitLoadedCampaign(recreated, loaded.Source);
        Assert.DoesNotContain("futureUfo", newYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("futureMission", newYaml, StringComparison.Ordinal);
        Assert.Equal(10, Assert.Single(TestFixtures.LoadLogisticsSave(newYaml, content, seed: 17,
            name: "new-world-identities.sav").Campaign.Capture().World.Ufos).UniqueId);
    }

    [Fact]
    public void PursuitGuardPreventsDeletingAReferencedUfo()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with { Status = UfoStatus.Destroyed };
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts.Single(item => item.RuleId == "SHIP");
        var follower = craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                ufo.Id, ufo.Longitude, ufo.Latitude)
                { UniqueId = ufo.UniqueId },
            }
        };
        var campaign = Restore(content, snapshot with
        {
            Bases = [owner with { Crafts = owner.Crafts.Select(item => item == craft ? follower : item).ToArray() }],
            World = snapshot.World with { Ufos = [ufo] },
        });
        AssertTimeBlocked(campaign, "Craft pursuit and landing require world simulation.");
    }

    private static CraftLogisticsState Ship(CampaignSnapshot snapshot) =>
        snapshot.Bases[0].Crafts.Single(item => item.RuleId == "SHIP").Logistics!;

    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
}
