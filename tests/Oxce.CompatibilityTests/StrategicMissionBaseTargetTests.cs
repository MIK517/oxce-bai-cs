using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionBaseTargetTests
{
    [Fact]
    public void BaseTargetsMatchExtractedReferenceAndReload()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-base-target");
        var content = Load();
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows").Where(row => !row[0].GetString()!.StartsWith("implicit", StringComparison.Ordinal)))
        {
            var name = row[0].GetString()!;
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(Snapshot(content, name), content, random);
            Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
            var after = campaign.Capture();
            var created = after.World.Missions.SingleOrDefault(m => m.Id == 20);
            var callCount = created is null ? random.Calls.Count - 1 : random.Calls.Count - 2;
            // The oracle starts after execution odds; creation adds one mission-rule race draw.
            rows.Add([name, created?.RuleId ?? "", created?.RegionId ?? "", random.Calls.Skip(1).Take(callCount).ToArray()]);
            if (created is not null)
            {
                Assert.Equal(("RACE_A", 60), (created.Race, created.SpawnCountdown));
                Assert.Equal(1, after.World.Strategy.MissionRuns.Single(p => p.Key == "BASE_RUN").Value);
            }
            else Assert.DoesNotContain(after.World.Strategy.MissionRuns, p => p.Key == "BASE_RUN");
            Reload(content, campaign, 17, "mission-base-target.sav");
        }
        TestFixtures.AssertJsonEqual(TestFixtures.Rows(oracle.RootElement, "rows")
                .Where(row => !row[0].GetString()!.StartsWith("implicit", StringComparison.Ordinal)).ToArray(), JsonSerializer.SerializeToUtf8Bytes(rows));
    }

    [Fact]
    public void ExhaustedBaseTargetsSetFalseLabelAndAllowLaterNegativeConditional()
    {
        var content = Load();
        var initial = Snapshot(content, "busy") with { MonthsPassed = 19 };
        var campaign = CampaignState.Restore(initial, content, new MinimumChoices());
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var created = campaign.Capture().World.Missions.Single(m => m.Id == 20);
        Assert.Equal("MISSION_SCOUT", created.RuleId);
        Assert.Equal("AFTER_BUSY_RUN", Assert.Single(campaign.Capture().World.Strategy.MissionRuns).Key);
        Reload(content, campaign, 17, "mission-base-target-failure.sav");
    }

    [Fact]
    public void BaseTargetPreflightBoundsCombinedCandidateChecks()
    {
        var content = Load();
        var initial = Snapshot(content, "duplicates");
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = Enumerable.Range(1, 100).Select(id => owner with { Id = id, Crafts = [], Soldiers = [] }).ToArray(),
            World = initial.World with
            {
                Missions = Enumerable.Range(100, 6000)
                .Select(id => Mission(id, "MISSION_A", "REGION")).ToArray()
            },
        };
        var campaign = CampaignState.Restore(initial, content, new MinimumChoices());
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Contains("work limit", blocked.Reason, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void MixedBaseOddsSupportBothOrdinaryAndTargetedSelection()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-scripts.rul"]);
        var initial = CreateSnapshot(content, "Mixed targeting") with { MonthsPassed = 3, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal("MISSION_SCOUT", Assert.Single(campaign.Capture().World.Missions).RuleId);
        Reload(content, campaign, 17, "mission-mixed-base-target.sav");
    }

    private static AlienMissionSnapshot Mission(int id, string type, string region) => new(id, type, region, "RACE_A", 0, 0, 9000, 0, -1);

    private static CampaignSnapshot Snapshot(Oxce.Mods.Rulesets.Content.RuntimeContent content, string name)
    {
        var initial = CreateSnapshot(content, "Base targets") with { MonthsPassed = name == "single" ? 20 : 18, RandomState = 0 };
        var owner = initial.Bases[0];
        AlienMissionSnapshot[] active = name switch
        {
            "pruned" => [Mission(10, "MISSION_A", "REGION")],
            "wrap" => [Mission(10, "MISSION_A", "REGION"), Mission(11, "MISSION_A", "REGION_SECOND")],
            "busy" => [Mission(10, "MISSION_A", "REGION"), Mission(11, "MISSION_A", "REGION_SECOND"),
                Mission(12, "MISSION_B", "REGION"), Mission(13, "MISSION_B", "REGION_SECOND")],
            _ => [],
        };
        return (initial with
        {
            Bases = [owner, owner with { Id = 2, Crafts = [], Soldiers = [] }, owner with
                { Id = 3, Longitude = WorldGeometry.Nautical(45 * 60), Crafts = [], Soldiers = [] }],
            World = initial.World with { Missions = active },
        }).WithNextId("ALIEN_MISSIONS", 20);
    }

    private static Oxce.Mods.Rulesets.Content.RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-base-target");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-base-target.rul"]);
    }

    private sealed class MinimumChoices : IStatefulRandomSource
    {
        public List<int[]> Calls { get; } = [];
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.True(minimum <= maximum);
            Calls.Add([minimum, maximum]); State++;
            return minimum;
        }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() => throw new InvalidOperationException();
    }
}
