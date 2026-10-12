using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldCrashedUfoTests
{
    [Fact]
    public void CrashTimerVisibilityAndExpiryMatchExtractedReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-crashed-ufo");
        var rows = new List<int[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var seconds = row[0].GetInt32(); var half = row[1].GetInt32(); var detected = row[2].GetInt32();
            var ufo = new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, 0.2, 0.1, UfoStatus.Crashed, WorldAltitudes.Ground)
            { SecondsRemaining = seconds, Detected = detected == 1 };
            if (half == 1) ufo = WorldCrashedUfo.HalfHour(ufo);
            ufo = WorldCrashedUfo.FiveSeconds(ufo);
            rows.Add([seconds, half, detected, (int)ufo.Status, ufo.SecondsRemaining, ufo.Detected ? 1 : 0]);
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(oracle.RootElement.GetProperty("rows"))),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Fact]
    public void GameplayCrashTransitionsAndMissionReleaseMatchOracleAcrossReload()
    {
        var content = Load();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-crashed-ufo");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var snapshot = Snapshot(content, row[0].GetInt32(), row[1].GetInt32() == 1, row[2].GetInt32() == 1);
            var campaign = Restore(content, snapshot);
            var after = Reload(content, campaign, 40, "before-crash-tick.sav").Capture();
            Assert.Equal(snapshot.World.Ufos[0].SecondsRemaining, after.World.Ufos[0].SecondsRemaining);
            AdvanceUnblocked(campaign, 1);
            after = campaign.Capture();
            var destroyed = row[3].GetInt32() == (int)UfoStatus.Destroyed;
            Assert.Equal(destroyed ? 0 : 1, after.World.Ufos.Count);
            Assert.Equal(destroyed ? 0 : 1, Assert.Single(after.World.Missions).LiveUfos);
            if (!destroyed)
            {
                var ufo = Assert.Single(after.World.Ufos);
                Assert.Equal(row[4].GetInt32(), ufo.SecondsRemaining);
                Assert.Equal(row[5].GetInt32() == 1, ufo.Detected);
                Assert.Equal(snapshot.World.Ufos[0].Position, ufo.Position);
                Assert.Equal(snapshot.World.Ufos[0].Shield, ufo.Shield);
                Assert.Equal(snapshot.World.Ufos[0].Speed, ufo.Speed);
                Assert.Equal(snapshot.World.Ufos[0].CrashId, ufo.CrashId);
            }
            Assert.Equal(snapshot.RandomState, after.RandomState);
            Assert.Equal(snapshot.Regions[0].ActivityAlien, after.Regions[0].ActivityAlien);
            Reload(content, campaign, 40, "after-crash-tick.sav");
        }
    }

    [Fact]
    public void ExpiredCrashReturnsCraftBeforeLandingAndSurvivesTransitionReload()
    {
        var content = Load();
        var snapshot = Snapshot(content, 1800, half: true);
        var ufo = snapshot.World.Ufos[0];
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Longitude = 0.2,
                Latitude = 0.1,
                Speed = 0,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, 0.2, 0.1) { UniqueId = ufo.UniqueId }
            }
        });
        var campaign = Restore(content, snapshot);
        var events = campaign.Execute(new AdvanceCampaignTime(2)).Events;
        Assert.Empty(events.OfType<CampaignActionBlocked>());
        // An expired crash is hidden before it is destroyed: GeoscapeState still offers its last position.
        var lost = Assert.Single(events.OfType<CraftTargetLost>());
        Assert.Equal((true, true), (lost.ReturningToBase, lost.OffersLastPosition));
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.NotEqual("STR_OUT", campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Status);
        Reload(content, campaign, 40, "expired-followed-crash.sav");
    }

    [Fact]
    public void LiveCrashLandingStillStopsBeforeTimerMutation()
    {
        var content = Load();
        var snapshot = Snapshot(content, 1801, half: true);
        var ufo = snapshot.World.Ufos[0];
        snapshot = snapshot.WithCraft("CARRIER", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Longitude = 0.2,
                Latitude = 0.1,
                Speed = 0,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, 0.2, 0.1) { UniqueId = ufo.UniqueId }
            }
        });
        AssertTimeBlocked(Restore(content, snapshot), "Craft landing requires deployment (Phase 6 branch 5).");
    }

    [Fact]
    public void CrashExpiryWithInsufficientLiveCountStopsBeforeItsTick()
    {
        var content = Load();
        var snapshot = Snapshot(content, 1, half: true);
        snapshot = snapshot with { World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { LiveUfos = 0 }] } };
        AssertTimeBlocked(Restore(content, snapshot), "Destroyed UFO count exceeds its mission's live count.");
    }

    [Fact]
    public void CrashCountdownPreservesUnknownDataUntilDeletionAcrossRepeatedRewrites()
    {
        var content = Load();
        var snapshot = Snapshot(content, 3600, half: true);
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        var start = yaml.IndexOf("ufos:\n", StringComparison.Ordinal);
        var end = yaml.IndexOf('\n', start + "ufos:\n".Length);
        yaml = yaml.Insert(end + 1, "    futureCrash: preserved\n");
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40);
        AdvanceOne(loaded.Campaign);
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.Contains("futureCrash: preserved", rewritten, StringComparison.Ordinal);
        loaded = TestFixtures.LoadLogisticsSave(rewritten, content, 40);
        AdvanceUnblocked(loaded.Campaign, 360);
        Assert.Empty(loaded.Campaign.Capture().World.Ufos);
        rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.DoesNotContain("futureCrash", rewritten, StringComparison.Ordinal);
        var again = TestFixtures.LoadLogisticsSave(rewritten, content, 40);
        Assert.DoesNotContain("futureCrash", OxceSaveAdapter.EmitLoadedCampaign(again.Campaign.Capture(), again.Source), StringComparison.Ordinal);
    }

    [Fact]
    public void CrashDeletionForecastWorkIsBoundedBeforeMutation()
    {
        var content = Load();
        var snapshot = Snapshot(content, 1, half: true);
        var source = snapshot.World.Ufos[0];
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [.. Enumerable.Range(1, 1001).Select(id =>
                new AlienMissionSnapshot(id, "MISSION_SCOUT", "REGION", "RACE_A", 1, 0, 9000, id == 4 ? 1000 : 0, -1))],
                Ufos = [.. Enumerable.Range(1, 1000).Select(id => source with { UniqueId = id, Id = id, CrashId = id })]
            }
        };
        AssertTimeBlocked(Restore(content, snapshot), "Destroyed UFO cleanup exceeds the supported work limit.");
    }

    [Fact]
    public void CrashedBattlescapeStateStillStopsBeforeTimeAdvancement()
    {
        var content = Load();
        var snapshot = Snapshot(content, 3600);
        snapshot = snapshot with { World = snapshot.World with { Ufos = [snapshot.World.Ufos[0] with { InBattlescape = true }] } };
        AssertTimeBlocked(Restore(content, snapshot), "Crashed UFO battlescape transitions require tactical simulation.");
    }

    [Fact]
    public void InvalidAssociatedSiteCannotRetainADeletedCrashReference()
    {
        var content = Load();
        var snapshot = Snapshot(content, 1, half: true);
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                MissionSites = [
            new(2, "MISSION_SITE", "SITE_DEPLOY", "RACE_A", 0.2, 0.1, 3600) { UfoUniqueId = 9 }]
            }
        };
        AssertTimeBlocked(Restore(content, snapshot), "Crashed UFO cannot expire while a mission site references it.");
    }

    [Fact]
    public void CrashExpiryComposesWithMissionCleanupForSeventyTwoHours()
    {
        var content = Load();
        var snapshot = Snapshot(content, 5400) with { Time = new(1, 1, 1, 1999, 0, 0, 0) };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with
            { RuleId = "MISSION_EMPTY_FINAL", NextWave = 1 }]
            }
        };
        var campaign = Restore(content, snapshot);
        AdvanceUnblocked(campaign, 720);
        Assert.Equal(1800, Assert.Single(campaign.Capture().World.Ufos).SecondsRemaining);
        campaign = Reload(content, campaign, 40, "before-composed-crash-expiry.sav");
        AdvanceUnblocked(campaign, 51_120);
        Assert.Equal(4, campaign.Capture().Time.Day);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Empty(campaign.Capture().World.Missions);
        Reload(content, campaign, 40, "after-composed-crash-cleanup.sav");
    }

    private static RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-crashed-ufo");
        return StrategicReadinessTestContent.Load("strategic-world.rul");
    }
    private static CampaignSnapshot Snapshot(RuntimeContent content, int seconds, bool half = false, bool detected = true)
    {
        var snapshot = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 0, half ? 29 : 0, 55), "UFO_SCOUT", 2200,
            missionWaveNumber: 0).Capture();
        return snapshot with
        {
            RandomState = 0,
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with
            { Status = UfoStatus.Crashed, Damage = 60, SecondsRemaining = seconds, Longitude = 0.2, Latitude = 0.1,
                CrashId = 7, Detected = detected, Altitude = WorldAltitudes.Ground, Shield = -1 }]
            }
        };
    }
    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
}
