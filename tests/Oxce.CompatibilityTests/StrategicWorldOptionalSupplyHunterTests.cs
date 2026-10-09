using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSupplyHunterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void ConditionalSupplyBirthsDoNotCreateLeaderDependenciesForSavedHunters(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-origin");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "supplyRows"), row =>
            row[0].GetInt32() == 1 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0);
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        foreach (var kind in new[] { 0, 2 })
        {
            var content = StrategicWorldOptionalSupplySourceTests.Content(cache, kind, radarRange: mode == 2 ? 20000 : 0);
            var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, kind, "CORE", radarRange: mode == 2 ? 20000 : 0);
            var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, kind, "", radarRange: mode == 2 ? 20000 : 0);
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldOptionalSupplySourceTests.Snapshot(content, seed, kind);
                var position = new WorldPosition(0.4, 0.2);
                var waypoint = WorldTargetReference.ForWaypoint(0, mode == 1 ? position : new(1, 0.6));
                var source = new UfoSnapshot(9, "OPTIONAL_SUPPLY_UFO", 4, "OPTIONAL_SUPPLY_AIR", 0,
                    position.Longitude, position.Latitude, UfoStatus.Flying, WorldAltitudes.High)
                {
                    Id = 9,
                    Speed = 2200,
                    HunterKiller = true,
                    MissionWaveNumber = 0,
                    Destination = waypoint,
                };
                if (mode == 2)
                {
                    initial = initial.WithCraft("SHIP", craft => craft with
                    {
                        Logistics = craft.Logistics! with
                        {
                            Status = "STR_OUT",
                            Fuel = 1000,
                            Damage = 0,
                            Speed = 6000,
                            Takeoff = 0,
                            Longitude = 1,
                            Latitude = 0.6,
                            Destination = WorldTargetReference.ForWaypoint(1, new(2, 0.7)),
                        },
                    });
                    var craft = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                    source = source with
                    {
                        Hunting = true,
                        OriginalWaypoint = waypoint.Position,
                        Destination = new(WorldTargetKind.Craft, "SHIP", craft.Id, 1, 0.6),
                    };
                }
                initial = initial with
                {
                    World = initial.World with
                    {
                        Missions = [initial.World.Missions[0] with { LiveUfos = 1 }],
                        Ufos = [source],
                        Waypoints = mode == 2 ? [new(1, 2, 0.7)] : initial.World.Waypoints,
                    },
                };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                var mission = Assert.Single(after.World.Missions);
                Assert.False(mission.Interrupted);
                Assert.Equal(deleted, mission.AlienBase is null);
                Assert.Equal(kind == 2 ? deleted ? 2 : 3 : deleted ? 1 : 2, after.World.Ufos.Count);
                var hunter = after.World.Ufos[0];
                Assert.True(hunter.HunterKiller);
                Assert.False(hunter.Escort);
                Assert.False(hunter.Escorting);
                Assert.Equal(mode == 2, hunter.Hunting);
                Assert.Equal(mode == 1 ? 1 : 0, hunter.TrajectoryPoint);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent,
                    new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "optional-space-supply-hunter.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }
}
