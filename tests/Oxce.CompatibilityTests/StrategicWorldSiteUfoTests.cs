using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteUfoTests
{
    [Fact]
    public void DormantSiteLiftMatchesExtractedReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "siteLiftRows"))
        {
            var ufo = new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_LANDING", 2,
                0, 0, UfoStatus.IgnoreMe, WorldAltitudes.Ground)
            {
                SecondsRemaining = row[2].GetInt32(),
                Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)),
            };
            var (after, score) = row[3].GetInt32() == 0 ? (ufo, 0) :
                WorldUfoLift.Advance(ufo, 1100, row[0].GetInt32(),
                    (RuntimeMissionObjective)row[1].GetInt32(), fromSite: true);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                { (int)after.Status, after.Altitude, after.Speed, after.SecondsRemaining, score });
            var expected = JsonSerializer.SerializeToUtf8Bytes(new object[]
                { row[4].GetInt32(), row[5].GetString()!, row[6].GetInt32(), row[7].GetInt32(), row[8].GetInt32() });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
        }
    }

    [Fact]
    public void SiteUfoWaitsReactivatesFliesAndDepartsAcrossReload()
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 1800);
        var original = Assert.Single(snapshot.World.Ufos);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var waiting = campaign.Capture();
        Assert.Equivalent(original, Assert.Single(waiting.World.Ufos), strict: true);
        Assert.Equal(0, Assert.Single(waiting.World.MissionSites).SecondsRemaining);
        Assert.Equal(original.UniqueId, waiting.World.MissionSites[0].UfoUniqueId);
        Assert.Equal(4, waiting.Regions[0].ActivityAlien[^1] - snapshot.Regions[0].ActivityAlien[^1]);
        Assert.DoesNotContain(campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets,
            target => target.Reference.Kind == WorldTargetKind.Ufo);

        campaign = Reload(content, campaign, 17, "site-ufo-waiting.sav");
        AdvanceUnblocked(campaign, 359);
        Assert.Equivalent(original, Assert.Single(campaign.Capture().World.Ufos), strict: true);
        AdvanceUnblocked(campaign, 1);
        var activated = campaign.Capture();
        Assert.Empty(activated.World.MissionSites);
        var ufo = Assert.Single(activated.World.Ufos);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        Assert.Equal(WorldAltitudes.VeryLow, ufo.Altitude);
        Assert.Equal(1100, ufo.Speed);
        Assert.Equal(77, ufo.SecondsRemaining);
        Assert.Equal(40, ufo.LandId);
        Assert.Equal(15, ufo.Shield); // Flying initialization occurs after site reactivation.
        Assert.True(ufo.Detected); // The earlier half-hour radar pass skipped IGNORE_ME.
        Assert.Equal(WorldGeometry.Move(original.Position, original.Destination!.Position,
            WorldGeometry.RadianSpeed(1100)), ufo.Position);
        Assert.Equal(17, activated.Regions[0].ActivityAlien[^1] - snapshot.Regions[0].ActivityAlien[^1]);
        Assert.Equal(2, activated.NextIds["shared"]);
        campaign = Reload(content, campaign, 17, "site-ufo-activated.sav");
        AdvanceUnblocked(campaign, 10);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
        campaign = Reload(content, campaign, 17, "site-ufo-deleted.sav");
        AdvanceUnblocked(campaign, 350);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Empty(Reload(content, campaign, 17, "site-ufo-finished.sav").Capture().World.MissionSites);
    }

    [Fact]
    public void CombinedSitePenaltyAndReactivationScoreStopBeforeMutation()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        snapshot = snapshot with
        {
            Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 10] },
                .. snapshot.Regions.Skip(1)],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.Contains(campaign.Execute(new AdvanceCampaignTime(1)).Events,
            item => item is CampaignActionBlocked action && action.Reason.Contains("activity", StringComparison.Ordinal));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ReactivatedUfoInterceptionStopsBeforeSiteExpiry()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = WithFollower(LinkedSnapshot(content, 0));
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Fact]
    public void HiddenReactivatedContactPatrolsWithoutAnInterceptionGuard()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = WithFollower(LinkedSnapshot(content, 0));
        snapshot = snapshot with
        {
            World = snapshot.World with
            { Ufos = [snapshot.World.Ufos[0] with { Detected = false }] }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.False(Assert.Single(result.Events.OfType<CraftTargetLost>()).ReturningToBase);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.False(Assert.Single(after.World.Ufos).Detected);
        Assert.Null(after.Bases[0].Crafts.Single(c => c.RuleId == "SHIP").Logistics!.Destination);
        Reload(content, campaign, 17, "site-hidden-ufo.sav");
    }

    [Fact]
    public void AFollowedSiteKeepsItsAssociatedUfoDormant()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        var site = snapshot.World.MissionSites[0];
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Longitude = 0.5,
                Latitude = 0.3,
                Destination = new(WorldTargetKind.MissionSite, "STR_TERROR_SITE", site.Id,
                    site.Longitude, site.Latitude),
            },
        });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equivalent(snapshot.World.Ufos[0], Assert.Single(campaign.Capture().World.Ufos), strict: true);
        Assert.Equivalent(site, Assert.Single(campaign.Capture().World.MissionSites), strict: true);
        Assert.False(campaign.Capture().NextIds.ContainsKey("shared"));
        Reload(content, campaign, 17, "site-held-ufo.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseScanningStartsAfterReactivationInTheSameTick(bool expires)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, expires ? 0 : 1800);
        var ufo = snapshot.World.Ufos[0] with
        {
            RuleId = "UFO_SCAN_TEST",
            Shield = 0,
            Longitude = 0.2,
            Latitude = 0.1,
            Destination = WorldTargetReference.ForWaypoint(0, new(1.2, 0.3)),
        };
        snapshot = snapshot with
        {
            RandomState = 2,
            World = snapshot.World with
            {
                Ufos = [ufo],
                MissionSites = [snapshot.World.MissionSites[0] with
                { Longitude = ufo.Longitude, Latitude = ufo.Latitude }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(2));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(expires, campaign.Capture().Bases[0].RetaliationTarget);
        var random = new SplitMix64RandomSource(2);
        if (expires) Assert.True(random.NextInclusive(0, 99) < 15);
        Assert.Equal(random.State, campaign.Capture().RandomState);
        Reload(content, campaign, 2, "site-ufo-base-scan.sav");
    }

    [Fact]
    public void InvalidShieldsStopBeforeReactivationMutatesSiteCounters()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 1800);
        snapshot = snapshot with
        {
            World = snapshot.World with
            { Ufos = [snapshot.World.Ufos[0] with { Shield = -2 }] }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 360);
        AssertTimeBlocked(campaign, "UFO shield arithmetic exceeds the supported range.");
    }

    [Fact]
    public void ALinkedActiveUfoStopsBeforeSiteExpiry()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        snapshot = snapshot with
        {
            World = snapshot.World with
            { Ufos = [snapshot.World.Ufos[0] with { Status = UfoStatus.Flying, Altitude = WorldAltitudes.VeryLow }] }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Mission site UFO reactivation requires a dormant UFO.");
    }

    [Theory]
    [InlineData("TRAJ_SITE_NEGATIVE_LIFT")]
    [InlineData("TRAJ_SITE_OVERFLOW_LIFT")]
    public void UnsafeLiftSpeedStopsOnlyWhenTheSiteWouldExpire(string trajectory)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 1800);
        snapshot = snapshot with
        {
            World = snapshot.World with
            { Ufos = [snapshot.World.Ufos[0] with { TrajectoryId = trajectory, TrajectoryPoint = 0 }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 360);
        AssertTimeBlocked(campaign, "Mission site UFO reactivation speed exceeds the supported range.");
    }

    [Theory]
    [InlineData("UFO_NEGATIVE_LIFT_MAXIMUM", "TRAJ_NORMAL_NEGATIVE_LIFT", "RACE_A")]
    [InlineData("UFO_OVERFLOW_LIFT_MAXIMUM", "TRAJ_NORMAL_NEGATIVE_LIFT", "RACE_B")]
    [InlineData("UFO_SCOUT", "TRAJ_SITE_OVERFLOW_LIFT", "RACE_A")]
    public void UnsafeOrdinaryLiftStopsBeforeSiteExpiry(string rule, string trajectory, string race)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with
        { RuleId = rule, TrajectoryId = trajectory, TrajectoryPoint = 0, SecondsRemaining = 5 };
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Ufos = [ufo],
                Missions = [snapshot.World.Missions[0] with { Race = race }],
                MissionSites = [new MissionSiteSnapshot(1, "STR_ALIEN_TERROR", "SITE_COUNTERS", "RACE_A",
                    ufo.Longitude, ufo.Latitude, 0)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        // Site counters/activity precede the UFO takeoff handler. The whole tick,
        // including clock, RNG and those site changes, must remain untouched.
        AssertTimeBlocked(campaign, "UFO speed is invalid.");
    }

    [Fact]
    public void LoadedRewritesRemoveSiteSidecarsAndPreserveTheReactivatedUfo()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        Assert.Contains("ufoUniqueId: 9\n", yaml, StringComparison.Ordinal);
        yaml = yaml.Replace("ufoUniqueId: 9\n", "ufoUniqueId: 9\n    futureSite: expired\n", StringComparison.Ordinal)
            .Replace("uniqueId: 9\n", "uniqueId: 9\n    futureUfo: surviving\n", StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 17, name: "site-ufo-sidecars.sav");
        AdvanceUnblocked(loaded.Campaign, 1);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var after = loaded.Campaign.Capture();
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(after, loaded.Source);
            Assert.DoesNotContain("futureSite", rewritten, StringComparison.Ordinal);
            Assert.Contains("futureUfo: surviving", rewritten, StringComparison.Ordinal);
            Assert.DoesNotContain("ufoUniqueId:", rewritten, StringComparison.Ordinal);
            loaded = TestFixtures.LoadLogisticsSave(rewritten, content, seed: 17, name: "site-ufo-sidecars.sav");
            Assert.Equivalent(after, loaded.Campaign.Capture(), strict: true);
        }
        AdvanceUnblocked(loaded.Campaign, 10);
        Assert.DoesNotContain("futureUfo", OxceSaveAdapter.EmitLoadedCampaign(
            loaded.Campaign.Capture(), loaded.Source), StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalReactivationSkipsCraftMovementAndRepairsFollowerOnNextTick()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        var ufo = Assert.Single(snapshot.World.Ufos);
        snapshot = WithFollower(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo with
            {
                TrajectoryPoint = 3, Destination = WorldTargetReference.ForWaypoint(0, ufo.Position)
            }]
            },
        });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var ended = campaign.Capture();
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(ended.World.Ufos).Status);
        Assert.Empty(ended.World.MissionSites);
        Assert.Equal(WorldTargetKind.Ufo, ended.Bases[0].Crafts.Single(c => c.RuleId == "SHIP").Logistics!.Destination!.Kind);
        AdvanceUnblocked(campaign, 1);
        var repaired = campaign.Capture();
        Assert.Empty(repaired.World.Ufos);
        Assert.Equal(WorldTargetKind.Base, repaired.Bases[0].Crafts.Single(c => c.RuleId == "SHIP").Logistics!.Destination!.Kind);
        Reload(content, campaign, 17, "site-terminal-ufo.sav");
    }

    [Fact]
    public void GuardedDespawnConsequencesLeaveTheAssociatedUfoDormant()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                MissionSites =
                [snapshot.World.MissionSites[0] with { DeploymentId = "SITE_DEPLOY" }]
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Mission site despawn requires research or event payloads.");
    }

    [Fact]
    public void DuplicateSiteUfoLinksFailIntentionally()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        var site = snapshot.World.MissionSites[0];
        snapshot = snapshot with { World = snapshot.World with { MissionSites = [site, site with { Id = 2 }] } };
        var error = Assert.Throws<InvalidDataException>(() =>
            CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)));
        Assert.Contains("same dormant UFO", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HourlyDiscoveryKeepsAssociatedUfoDormantUntilTheLaterExpiry()
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul"]);
        var snapshot = LinkedSnapshot(content, 0);
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 59, 55),
            World = snapshot.World with
            {
                MissionSites = [snapshot.World.MissionSites[0] with { Detected = false }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Single(result.Events.OfType<MissionSiteDetected>());
        Assert.Equal(1800, Assert.Single(campaign.Capture().World.MissionSites).SecondsRemaining);
        Assert.Equivalent(snapshot.World.Ufos[0], Assert.Single(campaign.Capture().World.Ufos), strict: true);
        Assert.Equal(snapshot.NextIds, campaign.Capture().NextIds);
        Reload(content, campaign, 17, "site-discovery-dormant-ufo.sav");
    }

    private static CampaignSnapshot LinkedSnapshot(RuntimeContent content, int seconds)
    {
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with
        {
            RuleId = "UFO_SHIELD_TEST",
            Status = UfoStatus.IgnoreMe,
            Shield = -1,
            SecondsRemaining = 77,
            LandId = 40,
            Detected = true,
            Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8.1), Degrees(2))),
        };
        return snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Ufos = [ufo],
                Missions = [snapshot.World.Missions[0] with { RuleId = "STR_ALIEN_TERROR", Race = "RACE_B" }],
                MissionSites = [new MissionSiteSnapshot(1, "STR_ALIEN_TERROR", "SITE_COUNTERS", "RACE_B",
                    ufo.Longitude, ufo.Latitude, seconds) { UfoUniqueId = ufo.UniqueId, Detected = true }],
            },
        };
    }

    private static CampaignSnapshot WithFollower(CampaignSnapshot snapshot)
    {
        var ufo = snapshot.World.Ufos[0];
        return snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Speed = 6000,
                Fuel = 100,
                Longitude = ufo.Longitude,
                Latitude = ufo.Latitude,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                    ufo.Id, ufo.Longitude, ufo.Latitude)
                { UniqueId = ufo.UniqueId },
            },
        });
    }
}
