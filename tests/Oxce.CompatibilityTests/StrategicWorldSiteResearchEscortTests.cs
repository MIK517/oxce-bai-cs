using System.Globalization;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchHuntingTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchEscortTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedMissionStillSelectsAnEscortLeaderWithStableIdentity(bool stale)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content();
        var initial = Snapshot(content);
        var leader = initial.World.Ufos[0] with { HunterKiller = false };
        var escort = leader with
        {
            UniqueId = 10,
            Id = 4,
            RuleId = "ESCORT_HUNTER",
            Longitude = 0.6,
            Latitude = 0.3,
            HunterKiller = true,
            Escort = true,
            Escorting = stale,
            OriginalWaypoint = stale ? new(1, 0.6) : null,
            Destination = stale ? new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, leader.Id, leader.Longitude, leader.Latitude) { UniqueId = 999 } : leader.Destination,
        };
        initial = initial with
        {
            World = initial.World with
            { Ufos = [leader, escort], Missions = [initial.World.Missions[0] with { LiveUfos = 2 }] }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        var follower = after.World.Ufos.Single(ufo => ufo.UniqueId == 10);
        Assert.True(follower.Escorting);
        Assert.False(follower.Hunting);
        Assert.Equal(leader.UniqueId, follower.Destination!.UniqueId);
        Assert.Equal(leader.Id, follower.Destination.Id);
        Assert.Equal(2200, follower.Speed);
        campaign = ReloadRepeatedly(content, campaign, 17, "site-research-escort-identity.sav", 3, after);
    }

    [Fact]
    public void SiteResearchPreservesNativeEscortOrderAndTerminalFallthrough()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content();
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "escortMovement").GroupBy(row => (Shape: row[0].GetInt32(), Order: row[1].GetInt32())))
        {
            var initial = Snapshot(content);
            var actors = Enumerable.Range(0, 3).Select(index => initial.World.Ufos[0] with
            {
                UniqueId = 9 + index,
                Id = 3 + index,
                HunterKiller = false,
                Escort = false,
                Longitude = 0.4 + index * 0.1,
                Latitude = 0.2 + index * 0.05,
                Escorting = group.Key.Shape is not (0 or 3) || index != 0,
                OriginalWaypoint = group.Key.Shape is not (0 or 3) || index != 0 ? new(1, 0.6) : null,
                TrajectoryPoint = group.Key.Shape == 3 && index == 0 ? content.RuntimeRules.UfoTrajectories[
                    content.RuntimeRules.UfoTrajectories.GetRequired(initial.World.Ufos[0].TrajectoryId)].Value.Waypoints.Count - 1 : initial.World.Ufos[0].TrajectoryPoint,
                Destination = group.Key.Shape is 0 or 3 && index == 0
                    ? new(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, group.Key.Shape == 3 ? 0.400001 : 1, group.Key.Shape == 3 ? 0.2 : 0.6)
                    : new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3 + (group.Key.Shape == 2 ? index : index == 0 ? 2 : index - 1), 0, 0)
                    { UniqueId = 9 + (group.Key.Shape == 2 ? index : index == 0 ? 2 : index - 1) },
            }).ToArray();
            initial = initial with
            {
                World = initial.World with
                { Ufos = orders[group.Key.Order].Select(index => actors[index]).ToArray(), Missions = [initial.World.Missions[0] with { LiveUfos = 3 }] }
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            foreach (var row in group)
            {
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.Empty(after.World.MissionSites);
                Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
                Assert.True(Assert.Single(after.World.Missions).Interrupted);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { group.Key.Shape, group.Key.Order, row[2].GetInt32() }
                    .Concat(after.World.Ufos.OrderBy(ufo => ufo.UniqueId).SelectMany(ufo => new object[]
                    {
                        decimal.Parse(ufo.Longitude.ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                        decimal.Parse(ufo.Latitude.ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                    })).ToArray());
                TestFixtures.AssertJsonEqual(row, actual);
                campaign = Reload(content, campaign, 81, "site-research-escort.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            if (group.Key.Shape == 3)
            {
                var cleanup = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(cleanup.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.Equal(2, after.World.Ufos.Count);
                Assert.DoesNotContain(after.World.Ufos, ufo => ufo.UniqueId == 9 || ufo.Destination is { Kind: WorldTargetKind.Ufo, UniqueId: 9 });
                campaign = Reload(content, campaign, 81, "site-research-escort-cleanup.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }
}
