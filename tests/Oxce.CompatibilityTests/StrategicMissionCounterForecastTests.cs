using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionCounterForecastTests
{
    [Fact]
    public void LegacyWaveFallbackMatchesPinnedArrivalCaller()
    {
        using var expected = TestFixtures.ReadVerifiedExpected("strategic-mission-counter");
        foreach (var row in TestFixtures.Rows(expected.RootElement, "rows"))
        {
            var count = row[0].GetInt32();
            var next = row[1].GetInt32();
            var saved = row[2].GetInt32();
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { count, next, saved, WorldMissionWave.Infer(saved, next, count) });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Fact]
    public void LegacyArrivalUsesCountersAfterTheHalfHourWaveRatherThanTheSavedCounter()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            ufoTrajectories:
              - id: LEGACY_ARRIVAL
                groundTimer: 60
                waypoints: [[0, 3, 0], [0, 0, 0], [0, 3, 0]]
            alienMissions:
              - type: LEGACY_COUNTER
                objective: 0
                spawnZone: 0
                siteType: SITE_DEPLOY
                waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 60}, {count: 1, trajectory: TRAJ_PATROL, timer: 60, objective: true}]
            """);
        var initial = CreateSnapshot(content, "Legacy counter forecast").WithNextId("STR_TERROR_SITE", int.MaxValue);
        initial = initial with
        {
            RandomState = 17,
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            World = initial.World with
            {
                Missions = [new(4, "LEGACY_COUNTER", "REGION", "RACE_A", 0, 0, 0, 1, 0)],
                Ufos = [new(9, "UFO_SCOUT", 4, "LEGACY_ARRIVAL", 0, Degrees(8), Degrees(2), UfoStatus.Flying, WorldAltitudes.High)
                { Shield = 0, MissionWaveNumber = -1, Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))) }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(1, Assert.Single(after.World.Missions).NextWave);
        Assert.Equal(-1, Assert.Single(after.World.Ufos).MissionWaveNumber);
        Assert.Equal(UfoStatus.Landed, after.World.Ufos[0].Status);
        Assert.Empty(after.World.MissionSites);
        Reload(content, campaign, 17, "legacy-counter-arrival.sav");
        // The half-hour pass can reveal a hidden contact before its ordinary landing.
        // The saved counter alone would classify this as a site and miss the reservation.
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_LANDING_SITE", int.MaxValue),
            content, new SplitMix64RandomSource(17)), "UFO landing marker IDs are exhausted or collide with a saved UFO.");
    }
}
