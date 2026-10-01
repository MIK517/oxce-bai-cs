using System.Text;
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

    private static RuntimeContent Load(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-strategy");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-strategy.rul"], cache);
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
