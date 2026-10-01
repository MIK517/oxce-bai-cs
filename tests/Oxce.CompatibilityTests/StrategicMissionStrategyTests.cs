using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionStrategyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrategyMissionSelectionMatchesReferenceAndReload(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-strategy");
        var content = Load(cache);
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
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchSelectionsUseLiveTableAndUseTableFalseRetainsHistory(bool cache)
    {
        var content = Load(cache);
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
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("invalid", false)]
    [InlineData("invalid", true)]
    [InlineData("depletion", false)]
    [InlineData("depletion", true)]
    public void InvalidAndPotentiallyDeletedTablesBlockWholeBatch(string kind, bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, kind == "depletion" ? 32 : 30,
            [new(kind == "invalid" ? "MISSING_MISSION" : "MISSION_A", 1)]);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExhaustedRegionsRefreshWithoutClearingRunHistoryAndComposeWithinBatch(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-strategy");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "refresh");
        var content = Load(cache);
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
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row[2])),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(random.Calls.Skip(2).Take(2).ToArray())));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitBaseMissionSelectionPrunesMissingTablesAndMatchesReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-base-target");
        var content = Load(cache);
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
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row[3])),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(random.Calls.Skip(1).Take(count).ToArray())));
            if (mission is not null) Assert.Equal("MISSION_A", mission.RuleId);
            else Assert.Empty(campaign.Capture().World.Strategy.MissionRuns);
            Reload(content, campaign, 17, "strategy-implicit-base.sav");
        }
    }

    private static RuntimeContent Load(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-strategy");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-strategy.rul", "strategic-a-strategy-reset.rul"], cache);
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
