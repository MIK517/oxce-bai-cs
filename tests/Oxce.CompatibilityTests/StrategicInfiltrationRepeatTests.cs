using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicInfiltrationRepeatTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartAndCountdownMatchPinnedReference(bool cache)
    {
        var content = Load(cache);
        var template = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("INFILTRATE")].Value;
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-infiltration-repeat");
        foreach (var example in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var timer = example.GetProperty("name").GetString() switch { "one" => 1UL, "rounded" => 29UL, "short" => 60UL, _ => 210UL };
            var rule = template with { EndlessInfiltration = true, Waves = [template.Waves[0] with { SpawnTimer = timer }] };
            var random = new TestFixtures.CountingMinimumRandomSource();
            var mission = new AlienMissionSnapshot(4, "INFILTRATE", "INF_REGION", "RACE_B", 0, 0, 0, 0, -1);
            var spawns = 0;
            var actual = new List<int[]>();
            foreach (var row in TestFixtures.Rows(example, "rows"))
            {
                if (mission.SpawnCountdown > 30) mission = mission with { SpawnCountdown = mission.SpawnCountdown - 30 };
                else
                {
                    spawns++;
                    mission = WorldInfiltration.Restart(mission, rule, random);
                }
                actual.Add([mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown, spawns, random.IntegerCalls]);
            }
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(example.GetProperty("rows"))),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndlessCompletionSignsAtMostOnePendingPactPerPassAndRestartsAfterSearch(bool cache)
    {
        var content = Load(cache, """
            alienMissions:
              - type: INF_REPEAT
                objective: 1
                endlessInfiltration: true
                spawnZone: 1
                siteType: INF_BASE
                waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 60}]
            """);
        var initial = Initial(content, "INF_REPEAT");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var expected = new SplitMix64RandomSource(17);
        var rules = content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired("INF_REPEAT")].Value;
        for (var pass = 0; pass < 3; pass++)
        {
            if (pass < 2)
                _ = WorldInfiltration.SelectRegion(rules, rule, rules.Regions[rules.Regions.GetRequired("INF_REGION")].Value, expected);
            _ = WorldTrajectory.SpawnCountdown(60, expected);
            var snapshot = campaign.Capture();
            campaign = CampaignState.Restore(snapshot with
            {
                Time = initial.Time,
                World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { SpawnCountdown = 0 }] },
            }, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(Math.Min(pass + 1, 2), after.World.AlienBases.Count);
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(0, mission.NextWave);
            Assert.InRange(mission.SpawnCountdown, 30, 90);
            Assert.Equal(expected.State, after.RandomState);
            Assert.Equal(Math.Min(pass + 1, 2), after.Countries.Count(country => country.NewPact));
            campaign = Reload(content, campaign, 17, "endless-infiltration.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndlessFiniteZeroTimerFollowupRestartsInReferenceOrderAndUnsafeCyclesStopAtomically(bool cache)
    {
        var content = Load(cache, """
            alienMissions:
              - type: INF_REPEAT
                objective: 1
                endlessInfiltration: true
                waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 0}, {count: 1, trajectory: TRAJ_PATROL, timer: 60}]
            """);
        var initial = Initial(content, "INF_REPEAT") with { Countries = [] };
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { NextWave = 1 }] } };
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 0); // restart of wave 0, followed immediately
        var countdown = WorldTrajectory.SpawnCountdown(60, expected);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var mission = Assert.Single(campaign.Capture().World.Missions);
        Assert.Equal((1, countdown), (mission.NextWave, mission.SpawnCountdown));
        Assert.Equal(expected.State, campaign.Capture().RandomState);
        Reload(content, campaign, 17, "endless-zero-followup.sav");
        content = Load(cache, """
            alienMissions:
              - type: INF_REPEAT
                objective: 1
                endlessInfiltration: true
                waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 0}]
            """);
        AssertTimeBlocked(CampaignState.Restore(Initial(content, "INF_REPEAT") with { Countries = [] }, content, new SplitMix64RandomSource(17)),
            "Immediate mission waves exceed the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndlessLegacyUfoWaveInferenceStopsBeforeUnreservedSiteConversion(bool cache)
    {
        var content = Load(cache, """
            ufoTrajectories:
              - id: INF_ARRIVAL
                groundTimer: 60
                waypoints: [[0, 3, 0], [0, 0, 0], [0, 3, 0]]
            alienMissions:
              - type: INF_REPEAT
                objective: 1
                endlessInfiltration: true
                spawnZone: 0
                siteType: INF_BASE
                waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 60}, {count: 1, trajectory: TRAJ_PATROL, timer: 60, objective: true}]
            """);
        var initial = Initial(content, "INF_REPEAT").WithNextId("INF_BASE", int.MaxValue) with { Countries = [] };
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { NextWave = 1, LiveUfos = 1, MissionSiteZoneArea = 0 }],
                Ufos = [new(9, "UFO_SCOUT", 4, "INF_ARRIVAL", 0, Degrees(8), Degrees(2), UfoStatus.Flying, WorldAltitudes.High)
                {
                    Shield = 0,
                    MissionWaveNumber = -1,
                    Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))),
                }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Endless infiltration completion requires legacy UFO wave forecasts.");
    }

    private static CampaignSnapshot Initial(RuntimeContent content, string rule)
    {
        var initial = CreateSnapshot(content, "Repeating infiltration");
        return initial with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Countries = initial.Countries.Where(country => country.RuleId.StartsWith("INF_", StringComparison.Ordinal)).ToArray(),
            World = initial.World with { Missions = [new(4, rule, "INF_REGION", "RACE_B", 0, 0, 0, 0, -1)] },
        };
    }

    private static RuntimeContent Load(bool cache, string extra = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-infiltration.rul"], cache, extra);
}
