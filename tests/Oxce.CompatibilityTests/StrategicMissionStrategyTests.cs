using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionStrategyTests
{
    [Fact]
    public void StrategyMissionSelectionMatchesReferenceAndReload()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-strategy");
        var content = Load();
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows").Where(row => row[0].GetString() != "refresh"))
        {
            var name = row[0].GetString()!;
            var options = name switch
            {
                "empty" => Array.Empty<KeyValuePair<string, ulong>>(),
                "removed" => [new("MISSION_B", 1UL)],
                _ => new KeyValuePair<string, ulong>[] { new("MISSION_A", 3), new("MISSION_B", 1) },
            };
            var snapshot = Snapshot(content, 30, options);
            var random = new Choices(name == "last");
            var campaign = CampaignState.Restore(snapshot, content, random);
            Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
            var after = campaign.Capture();
            var mission = after.World.Missions.SingleOrDefault();
            rows.Add([name, mission?.RuleId ?? "", random.Calls.Skip(3).Take(mission is null ? 0 : 1).ToArray()]);
            if (mission is null)
            {
                Assert.Empty(after.World.Strategy.MissionRuns);
                Assert.Equal(snapshot.NextIds, after.NextIds);
            }
            else
            {
                Assert.Equal(("REGION", "RACE_A", 60), (mission.RegionId, mission.Race, mission.SpawnCountdown));
                Assert.DoesNotContain(after.World.Strategy.RegionMissions.SelectMany(p => p.Value), p => p.Key == mission.RuleId);
            }
            Reload(content, campaign, 17, "strategy-mission.sav");
        }
        var expected = TestFixtures.Rows(oracle.RootElement, "rows").Where(row => row[0].GetString() != "refresh").ToArray();
        TestFixtures.AssertJsonEqual(expected, JsonSerializer.SerializeToUtf8Bytes(rows));
    }

    [Fact]
    public void BatchSelectionsUseLiveTableAndUseTableFalseRetainsHistory()
    {
        var content = Load();
        var snapshot = Snapshot(content, 33, [new("MISSION_A", 3), new("MISSION_B", 1)]);
        var campaign = CampaignState.Restore(snapshot, content, new Choices());
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(["MISSION_A", "MISSION_B"], campaign.Capture().World.Missions.Select(m => m.RuleId));
        Assert.Empty(campaign.Capture().World.Strategy.RegionMissions);
        Reload(content, campaign, 17, "strategy-batch.sav");
        snapshot = Snapshot(content, 31, [new("MISSION_A", 3)]);
        campaign = CampaignState.Restore(snapshot, content, new Choices());
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(snapshot.World.Strategy.RegionMissions, campaign.Capture().World.Strategy.RegionMissions);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("depletion")]
    [InlineData("emptyDepletion")]
    public void InvalidAndPotentiallyDeletedTablesBlockWholeBatch(string kind)
    {
        var content = Load();
        var snapshot = Snapshot(content, kind is "depletion" or "emptyDepletion" ? 32 : 30,
            [new(kind == "invalid" ? "MISSING_MISSION" : "MISSION_A", 1)]);
        if (kind == "emptyDepletion") snapshot = snapshot with
        {
            World = snapshot.World with
            { Strategy = snapshot.World.Strategy with { RegionMissions = [new("REGION", [])] } }
        };
        if (kind == "missing") snapshot = snapshot with
        {
            World = snapshot.World with
            { Strategy = snapshot.World.Strategy with { RegionMissions = [] } }
        };
        var campaign = CampaignState.Restore(snapshot, content, new Choices());
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ExhaustedRegionsRefreshWithoutClearingRunHistoryAndComposeWithinBatch()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-strategy");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "refresh");
        var content = Load();
        Assert.Equal(["MISSION_A", "MISSION_B"], content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value.MissionWeights.Keys);
        var snapshot = Snapshot(content, 34, []) with
        {
            World = new()
            {
                Strategy = new() { MissionRuns = [new("OLDER_RUN", 4)] },
            }
        };
        var random = new Choices();
        var campaign = CampaignState.Restore(snapshot, content, random);
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(row[1].GetString(), Assert.Single(campaign.Capture().World.Missions).RuleId);
        TestFixtures.AssertJsonEqual(row[2], JsonSerializer.SerializeToUtf8Bytes(random.Calls.Skip(2).Take(2).ToArray()));
        Assert.Equal(4, campaign.Capture().World.Strategy.MissionRuns.Single(p => p.Key == "OLDER_RUN").Value);
        campaign = Reload(content, campaign, 17, "strategy-refresh.sav");
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(2, campaign.Capture().World.Strategy.MissionRuns.Single(p => p.Key == "REFRESH_RUN").Value);

        campaign = CampaignState.Restore(Snapshot(content, 35, [new("MISSION_A", 3), new("MISSION_B", 1)]), content, new Choices());
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(["MISSION_A", "MISSION_B", "MISSION_A"], campaign.Capture().World.Missions.Select(m => m.RuleId));
        Assert.Equal(3, campaign.Capture().World.Strategy.MissionRuns.Single(p => p.Key == "REFRESH_RUN").Value);
        Reload(content, campaign, 17, "strategy-refresh-batch.sav");
    }

    [Fact]
    public void ImplicitBaseMissionSelectionPrunesMissingTablesAndMatchesReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-base-target");
        var content = Load();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows").Where(row => row[0].GetString()!.StartsWith("implicit", StringComparison.Ordinal)))
        {
            var name = row[0].GetString()!;
            var snapshot = Snapshot(content, 36, [new("MISSION_A", 3), new("MISSION_B", 1)]);
            var owner = snapshot.Bases[0];
            IReadOnlyList<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, ulong>>>> tables =
                name == "implicitEmpty" ? [] : name == "implicitPruned"
                ? [new("REGION_SECOND", [new("MISSION_A", 3), new("MISSION_B", 1)])]
                : [new("REGION", [new("MISSION_A", 3), new("MISSION_B", 1)]),
                    new("REGION_SECOND", [new("MISSION_A", 3), new("MISSION_B", 1)])];
            snapshot = snapshot with
            {
                Bases = [owner, owner with { Id = 2, Crafts = [], Soldiers = [] }, owner with
                    { Id = 3, Longitude = WorldGeometry.Nautical(45 * 60), Crafts = [], Soldiers = [] }],
                World = snapshot.World with { Strategy = snapshot.World.Strategy with { RegionMissions = tables } },
            };
            var random = new Choices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
            var mission = campaign.Capture().World.Missions.SingleOrDefault();
            Assert.Equal(row[2].GetString(), mission?.RegionId ?? "");
            var count = row[3].GetArrayLength();
            TestFixtures.AssertJsonEqual(row[3], JsonSerializer.SerializeToUtf8Bytes(random.Calls.Skip(1).Take(count).ToArray()));
            if (mission is not null) Assert.Equal("MISSION_A", mission.RuleId);
            else Assert.Empty(campaign.Capture().World.Strategy.MissionRuns);
            Reload(content, campaign, 17, "strategy-implicit-base.sav");
        }
    }

    [Fact]
    public void RemovingFromEmptyTableDeletesRegionLikeReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-strategy");
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "deletionRows"))
        {
            var name = row[0].GetString()!;
            var missions = new List<KeyValuePair<string, ulong>>();
            if (name != "empty") missions.Add(new("MISSION_A", 1));
            if (name == "remaining") missions.Add(new("MISSION_B", 1));
            var strategy = new AlienStrategyState();
            strategy.Initialize([("REGION", 1UL, (IEnumerable<KeyValuePair<string, ulong>>)missions)]);
            strategy.RemoveMission("REGION", "MISSION_A");
            rows.Add([name, strategy.RegionMissions.Count, strategy.RegionChances.TotalWeight]);
        }
        TestFixtures.AssertJsonEqual(oracle.RootElement.GetProperty("deletionRows"), JsonSerializer.SerializeToUtf8Bytes(rows));
    }

    private static RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-strategy");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-strategy.rul", "strategic-a-strategy-reset.rul"]);
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, int month, IReadOnlyList<KeyValuePair<string, ulong>> missions)
    {
        var snapshot = CreateSnapshot(content, "Strategy missions") with { MonthsPassed = month, RandomState = 0 };
        return snapshot with
        {
            World = snapshot.World with
            {
                Strategy = new()
                {
                    RegionChances = [new("REGION", 1)],
                    RegionMissions = [new("REGION", missions)],
                }
            }
        };
    }

    private sealed class Choices(bool last = false) : IStatefulRandomSource
    {
        public List<int[]> Calls { get; } = [];
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int maximum)
        {
            Calls.Add([minimum, maximum]); State++;
            return last && (minimum, maximum) == (1, 4) ? maximum : minimum;
        }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() => throw new InvalidOperationException();
    }
}
