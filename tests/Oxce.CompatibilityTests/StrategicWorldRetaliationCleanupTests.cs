using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRetaliationCleanupTests
{
    [Fact]
    public void RetaliationCleanupMatchesReferenceAndRetainsUnrelatedLinks()
    {
        var content = Load();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-cleanup");
        var rows = new List<int[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var objective = row[0].GetInt32(); var over = row[1].GetInt32(); var linked = row[2].GetInt32();
            var snapshot = Snapshot(content, objective, over == 1);
            snapshot = snapshot with { Bases = [snapshot.Bases[0] with { RetaliationMissionId = linked == 1 ? 4 : 5 }] };
            var campaign = Restore(content, snapshot);
            Reload(content, campaign, 40, "before-retaliation-cleanup.sav");
            if (objective == 6 && over == 1 && linked == 1)
            {
                // The reference excludes this inconsistent link from repair. Guard its dangling pointer before deletion.
                AssertTimeBlocked(campaign, "A non-retaliation mission cannot expire while a base links it as retaliation.");
                rows.Add([objective, over, linked, campaign.Capture().Bases[0].RetaliationMissionId ?? 0]);
                continue;
            }
            AdvanceOne(campaign);
            var after = campaign.Capture();
            rows.Add([objective, over, linked, after.Bases[0].RetaliationMissionId ?? 0]);
            Assert.Equal(snapshot.RandomState, after.RandomState);
            Assert.Equal(over == 1 ? 1 : 2, after.World.Missions.Count);
            Assert.True(after.Bases[0].RetaliationTarget);
            Reload(content, campaign, 40, "after-retaliation-cleanup.sav");
        }
        Equal(oracle.RootElement.GetProperty("rows"), rows);
    }

    [Fact]
    public void SavedLinksResolveOnlyToExistingMissionsAsInBaseLoad()
    {
        var content = Load();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-cleanup");
        var rows = new List<int[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "loadRows"))
        {
            var id = row[0].GetInt32();
            var snapshot = Snapshot(content);
            snapshot = snapshot with { Bases = [snapshot.Bases[0] with { RetaliationMissionId = id == 0 ? null : id }] };
            var after = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot), content, 40).Campaign.Capture();
            rows.Add([id, after.Bases[0].RetaliationMissionId ?? 0]);
            Reload(content, Restore(content, after), 40, "loaded-retaliation-link.sav");
        }
        Equal(oracle.RootElement.GetProperty("loadRows"), rows);
    }

    [Fact]
    public void DailyResearchInterruptionCleansLinkedRetaliationAndRepeatedSaveOverlays()
    {
        var content = Load();
        var snapshot = Snapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = ["UNLOCK"] };
        snapshot = snapshot with
        {
            Bases = [snapshot.Bases[0] with { RetaliationMissionId = 4 }],
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { RuleId = "MISSION_RETALIATION" }, snapshot.World.Missions[1]] }
        };
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot).Replace("retaliationMissionUniqueId: 4\n",
            "retaliationMissionUniqueId: 4\n    futureBase: preserved\n", StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40);
        AdvanceOne(loaded.Campaign);
        Assert.Null(loaded.Campaign.Capture().Bases[0].RetaliationMissionId);
        Assert.DoesNotContain(loaded.Campaign.Capture().World.Missions, mission => mission.Id == 4);
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.DoesNotContain("retaliationMissionUniqueId", rewritten, StringComparison.Ordinal);
        Assert.Contains("futureBase: preserved", rewritten, StringComparison.Ordinal);
        var again = TestFixtures.LoadLogisticsSave(rewritten, content, 40);
        rewritten = OxceSaveAdapter.EmitLoadedCampaign(again.Campaign.Capture(), again.Source);
        Assert.DoesNotContain("retaliationMissionUniqueId", rewritten, StringComparison.Ordinal);
        Assert.Contains("futureBase: preserved", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedFinishedMissionCannotOrphanAReferencedUfo()
    {
        var content = Load();
        var snapshot = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 0, 29, 55), "UFO_SCOUT", 2200,
            "RETALIATION_CLEANUP", missionWaveNumber: 0).Capture();
        snapshot = snapshot with
        {
            Bases = [snapshot.Bases[0] with { RetaliationMissionId = 4 }],
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { LiveUfos = 0 }] }
        };
        AssertTimeBlocked(Restore(content, snapshot), "Alien mission cannot expire while UFOs still reference it.");
    }

    private static RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-retaliation-cleanup");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-retaliation-cleanup.rul"]);
    }
    private static CampaignSnapshot Snapshot(RuntimeContent content, int objective = 4, bool over = false)
    {
        var snapshot = CreateSnapshot(content, "Retaliation cleanup");
        return snapshot with
        {
            Time = new(1, 1, 1, 1999, 0, 29, 55),
            RandomState = 0,
            Bases = [snapshot.Bases[0] with { RetaliationTarget = true }],
            World = snapshot.World with
            {
                Missions = [
                new(4, objective == 4 ? "RETALIATION_CLEANUP" : "RETALIATION_INSTANT_CLEANUP", "REGION", "RACE_A", over ? 1 : 0, 0, 9000, 0, -1),
                new(5, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1)]
            }
        };
    }
    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
    private static void Equal(JsonElement expected, List<int[]> rows) =>
        TestFixtures.AssertJsonEqual(expected, JsonSerializer.SerializeToUtf8Bytes(rows));
}
