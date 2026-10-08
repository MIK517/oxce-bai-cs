using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchSpawnTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AirborneBirthsPrecedeSiteResearchAndKeepCompletedMissionsAlive(bool cache, bool final)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Where(row => row[0].GetInt32() == 0 && row[1].GetInt32() == 0))
        {
            var second = row[2].GetInt32();
            var content = Content(cache, $"PATH_0_{second}", final);
            var initial = Snapshot(content);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            var mission = Assert.Single(after.World.Missions);
            Assert.True(mission.Interrupted);
            Assert.Equal((1, 0, 1), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos));
            Assert.Empty(after.World.MissionSites);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            Assert.Equal(UfoStatus.Flying, ufo.Status);
            var origin = Position(row, 6);
            var destination = Position(row, 8);
            var expected = WorldGeometry.Move(origin, destination, WorldGeometry.RadianSpeed(row[5].GetInt32()));
            Assert.InRange(WorldGeometry.Distance(expected, ufo.Position), 0, 1e-8);
            Assert.InRange(WorldGeometry.Distance(destination, ufo.Destination!.Position), 0, 1e-7);
            var random = new SplitMix64RandomSource(initial.RandomState);
            for (var draw = 0; draw < row[10].GetInt32(); draw++) random.NextInclusive(0, 0);
            if (!final) Assert.Equal(WorldTrajectory.SpawnCountdown(9000, random), mission.SpawnCountdown);
            random.NextInclusive(0, 99);
            Assert.Equal(random.State, after.RandomState);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "site-research-airborne-birth.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.Single(campaign.Capture().World.Missions);
            Assert.Single(campaign.Capture().World.Ufos);
            Assert.Single(campaign.Capture().ResearchDiary);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFinalBirthStillValidatesTheSurvivingMissionInterruptionLink(bool cache)
    {
        var content = Content(cache, "PATH_0_0", true, "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    internal static RuntimeContent Content(bool cache, string trajectory, bool final = false, string interrupt = "CORE", string extra = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-spawn-paths.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - type: SITE_RESEARCH_BIRTH
            interruptResearch: {{{interrupt}}}
            points: 7
            waves:
              - {ufo: UFO_SCOUT, count: 1, trajectory: {{{trajectory}}}, timer: 0}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + "\n" + extra);

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial with
        {
            RandomState = 17,
            Bases = [initial.Bases[0] with { Research = [] }],
            World = initial.World with { Missions = [new(4, "SITE_RESEARCH_BIRTH", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }

    internal static WorldPosition Position(System.Text.Json.JsonElement row, int index) =>
        new(double.Parse(row[index].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(row[index + 1].GetString()!, CultureInfo.InvariantCulture));
}
