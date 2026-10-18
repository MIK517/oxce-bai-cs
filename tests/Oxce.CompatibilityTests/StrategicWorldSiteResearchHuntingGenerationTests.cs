using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchHuntingGenerationTests
{
    [Theory]
    [InlineData("delayed")]
    [InlineData("short")]
    [InlineData("immediate")]
    [InlineData("immediateZero")]
    public void HuntingGenerationFollowsSiteGrantAndDoesNotReceiveEarlierExplicitInterruption(string name)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var row = TestFixtures.Rows(oracle.RootElement, name.StartsWith("immediate", StringComparison.Ordinal) ? "immediateRows" : "rows")
            .Single(row => row[0].GetString() == name);
        var content = Content();
        var initial = Snapshot(content, name);
        var expected = new SplitMix64RandomSource(initial.RandomState);
        var calls = row[2].EnumerateArray().ToArray();
        foreach (var call in calls.Take(calls.Length - 1)) expected.NextInclusive(call[0].GetInt32(), call[1].GetInt32());
        var timer = name == "delayed" ? 9000 : name == "short" ? 120 : name == "immediate" ? 90 : 0;
        var countdown = WorldTrajectory.SpawnCountdown(timer, expected);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Empty(after.World.Ufos);
        Assert.Contains("CORE", after.CompletedResearch);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        var mission = Assert.Single(after.World.Missions);
        Assert.False(mission.Interrupted);
        Assert.Equal((countdown <= 30 ? 1 : 0, countdown <= 30 ? countdown : countdown - 30),
            (mission.NextWave, mission.SpawnCountdown));
        Assert.Equal(0, mission.LiveUfos);
        Assert.Equal(2, mission.AlienBase!.Id);
        Assert.Equal(0, Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Assert.Equal(expected.State, after.RandomState);
        campaign = ReloadRepeatedly(content, campaign, 17, "site-research-hunting-generation.sav", 3, after);
        AdvanceUnblocked(campaign, 360);
        if (countdown <= 30) Assert.Empty(campaign.Capture().World.Missions);
        else Assert.False(Assert.Single(campaign.Capture().World.Missions).Interrupted);
    }

    [Fact]
    public void SameSourceDailyAndHuntingBirthsStraddleTheSiteGrant()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1}
            alienMissions:
              - type: BOTH_CALLERS
                interruptResearch: CORE
                waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 9000}]
            """ + "\n" + StrategicWorldSiteResearchTests.Deployment + """

              - type: BOTH_BASE
                markerName: BOTH
                genMission: {BOTH_CALLERS: 1}
                genMissionFreq: 100
                genMissionLimit: 1
                baseDetectionRange: 100
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionWeights: {0: {BOTH_CALLERS: 1}}
            """);
        var initial = Snapshot(content, "delayed");
        initial = initial with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            World = initial.World with { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "BOTH_BASE" }] },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Collection(after.World.Missions,
            mission => { Assert.Equal(1, mission.Id); Assert.True(mission.Interrupted); },
            mission => { Assert.Equal(2, mission.Id); Assert.False(mission.Interrupted); });
        Assert.All(after.World.Missions, mission => Assert.Equal("BOTH_CALLERS", mission.RuleId));
        Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
        Assert.Equal(0, after.World.AlienBases[0].MinutesSinceLastHuntMissionGeneration);
        campaign = ReloadRepeatedly(content, campaign, 17, "site-research-both-generation.sav", 3, after);
    }

    internal static RuntimeContent Content() => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-alien-base-hunt-generation.rul"], """
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - {type: HUNT_GEN, interruptResearch: CORE}
          - {type: HUNT_GEN_SHORT, interruptResearch: CORE}
          - {type: HUNT_NO_OBJECT_90, interruptResearch: CORE}
          - {type: HUNT_NO_OBJECT_0, interruptResearch: CORE}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, string name)
    {
        var initial = StrategicWorldSiteResearchGenerationTests.Snapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 300,
                Speed = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
            }
        });
        var deployment = name == "delayed" ? "HUNT_GENERATE" : name == "short" ? "HUNT_GENERATE_SHORT" :
            name == "immediate" ? "HUNT_NO_OBJECT_90" : "HUNT_NO_OBJECT_0";
        return initial with
        {
            Time = new(1, 1, 1, 1999, 0, 29, 55),
            World = initial.World with
            {
                AlienBases = [new(2, deployment, "RACE_A", 0.2, 0.1)
            { Discovered = true, MinutesSinceLastHuntMissionGeneration = 50 }]
            },
        };
    }
}
