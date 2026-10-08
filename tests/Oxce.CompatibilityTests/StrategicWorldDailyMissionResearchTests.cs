using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyMissionResearchTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    public void DailyRewardSelectsMissionInterruptionBeforeAllOrdinaryFlightPaths(bool cache, int path)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        var content = Content(cache, path);
        var interruptedContent = Content(cache, path, "CORE");
        var continuedContent = Content(cache, path, "");
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, path);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(interrupted, Assert.Single(after.World.Missions).Interrupted);
            var terminal = path == 6 || interrupted && path != 0 || path == 3 && !interrupted;
            Assert.Equal(terminal, after.World.Ufos[0].Status == UfoStatus.Destroyed);
            Assert.Equal(terminal, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
            Assert.Equal(path is 3 or 4 && !interrupted ? 1 : 0, after.World.MissionSites.Count);
            Assert.Equal(path == 5 && !interrupted ? 1 : 0, after.World.AlienBases.Count);
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
                campaign = Reload(content, campaign, seed, "daily-mission-research-arrival.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(terminal ? 1 : 2, campaign.Capture().World.Ufos.Count);
            Assert.Equal(terminal ? 1 : 2, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
            Reload(content, campaign, seed, "daily-mission-research-followup.sav");
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleDailyContinuationRetainsMarkerCapacityAndCombatBoundaries(bool cache)
    {
        var content = Content(cache, 5);
        var initial = Snapshot(content, 17, 5).WithNextId("ARRIVAL_BASE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
        content = Content(cache, 0);
        initial = Snapshot(content, 17, 0).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Longitude = Degrees(8), Latitude = Degrees(2), Speed = 6000, Takeoff = 0 },
        });
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
            {
                TrajectoryId = "TRAJ_AIRBORNE", TrajectoryPoint = 0, Longitude = Degrees(8), Latitude = Degrees(2),
                Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))),
            }, initial.World.Ufos[1]]
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    internal static RuntimeContent Content(bool cache, int path, string interrupt = "BONUS") =>
        StrategicWorldConversionResearchTests.Content(cache, respawn: path == 4, interrupt: interrupt, baseLanding: path == 5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleDailyCleanupCannotOrphanUfosOrInvalidRetaliationLinks(bool cache)
    {
        var content = Content(cache, 0);
        var initial = Snapshot(content, 17, 0);
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 0, NextWave = 0 }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission cannot expire while UFOs still reference it.");
        initial = initial with
        {
            Bases = [initial.Bases[0] with { RetaliationMissionId = initial.World.Missions[0].Id }],
            World = initial.World with { Ufos = [] },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "A non-retaliation mission cannot expire while a base links it as retaliation.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int path)
    {
        var initial = StrategicWorldConversionResearchTests.Snapshot(content, seed, baseLanding: path == 5);
        var first = initial.World.Ufos[0];
        if (path == 0) first = first with { Longitude = 0, Latitude = 0, Destination = WorldTargetReference.ForWaypoint(0, new(0.6, 0.2)) };
        if (path == 1) first = first with { TrajectoryId = "TRAJ_AIRBORNE", TrajectoryPoint = 0 };
        if (path == 6) first = first with
        {
            TrajectoryPoint = content.RuntimeRules.UfoTrajectories[content.RuntimeRules.UfoTrajectories.GetRequired(first.TrajectoryId)].Value.Waypoints.Count - 1,
        };
        return initial with
        {
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)] }],
            World = initial.World with
            {
                Ufos = [first, initial.World.Ufos[1]],
                MissionSites = [],
                Missions = [initial.World.Missions[0] with { MissionSiteZoneArea = path is 3 or 4 ? 0 : -1 }],
            },
        };
    }
}
