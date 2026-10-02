using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldObjectiveWaveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoObjectWavesAdvanceUntilTheCapturedObjectiveCompletionBoundary(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "completionRows").Where(row => row[2].GetInt32() == 0))
        {
            var objective = row[0].GetInt32();
            var final = row[1].GetInt32() != 0;
            var mission = new AlienMissionSnapshot(4, $"EMPTY_OBJECTIVE_{objective}", "REGION", "RACE_A", final ? 1 : 0, 0, 0, 0, -1);
            var campaign = Restore(content, mission);
            if (row[3].GetInt32() != 0)
                AssertTimeBlocked(campaign, "Alien mission wave completion requires objective simulation.");
            else if (objective is 4 or 6)
                AssertTimeBlocked(campaign, "Alien mission wave spawning requires world simulation.");
            else
            {
                AdvanceOne(campaign);
                if (final) Assert.Empty(campaign.Capture().World.Missions);
                else
                {
                    var after = Assert.Single(campaign.Capture().World.Missions);
                    Assert.Equal((0, 1, 0), (after.NextWave, after.NextUfoCounter, after.LiveUfos));
                }
                Assert.Empty(campaign.Capture().World.Ufos);
                Assert.Empty(campaign.Capture().World.MissionSites);
                Assert.Empty(campaign.Capture().World.AlienBases);
                Reload(content, campaign, 17, "empty-objective-wave.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAirborneWavesShareSpawnAndDetectionBeforeObjectiveEffects(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        var content = Load(cache);
        var score = Restore(content, new AlienMissionSnapshot(4, "AIR_OBJECTIVE_0", "REGION", "RACE_A", 0, 0, 0, 0, -1));
        AdvanceOne(score);
        var baseline = score.Capture();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "completionRows").Where(row => row[2].GetInt32() == 0))
        {
            var objective = row[0].GetInt32();
            var final = row[1].GetInt32() != 0;
            var campaign = Restore(content, new AlienMissionSnapshot(4, $"AIR_OBJECTIVE_{objective}", "REGION", "RACE_A", final ? 1 : 0, 0, 0, 0, -1));
            if (row[3].GetInt32() != 0)
                AssertTimeBlocked(campaign, "Alien mission wave completion requires objective simulation.");
            else if (objective is 4 or 6)
                AssertTimeBlocked(campaign, "Alien mission wave spawning requires world simulation.");
            else
            {
                AdvanceOne(campaign);
                var after = campaign.Capture();
                Assert.Equivalent(baseline.World.Ufos.Select(ufo => ufo with { MissionWaveNumber = final ? 1 : 0 }), after.World.Ufos, strict: true);
                var mission = Assert.Single(after.World.Missions);
                Assert.Equal(1, mission.LiveUfos);
                Assert.Equal(final ? 2 : 0, mission.NextWave);
                Assert.Equal(final ? 0 : 1, mission.NextUfoCounter);
                Assert.Empty(after.World.AlienBases);
                Assert.Empty(after.World.MissionSites);
                campaign = Reload(content, campaign, 17, "air-objective-wave.sav");
                AdvanceOne(campaign);
                Assert.NotEqual(after.World.Ufos[0].Position, campaign.Capture().World.Ufos[0].Position);
            }
        }
    }

    private static RuntimeContent Load(bool cache) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-objective-waves.rul"], cache);

    private static CampaignState Restore(RuntimeContent content, AlienMissionSnapshot mission)
    {
        var snapshot = CreateSnapshot(content, "Objective waves") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            RandomState = 17,
        };
        return CampaignState.Restore(snapshot with { World = snapshot.World with { Missions = [mission] } },
            content, new SplitMix64RandomSource(17));
    }
}
