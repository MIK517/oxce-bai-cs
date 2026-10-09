using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornFirstArrivalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PointBirthsAdvanceOneAirborneWaypointOnTheirFirstTick(bool cache, bool separate)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var transition = Assert.Single(TestFixtures.Rows(oracle.RootElement, "transitionRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 0 && row[2].GetInt32() == 0 && row[3].GetInt32() == 0);
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = Content(cache, separate);
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var initial = Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(3, after.World.Ufos.Count);
            var expectedRandom = new SplitMix64RandomSource(seed);
            // Each birth chooses origin/destination; each arrival makes the two
            // native waypoint selections. Base detection draws even at zero chance;
            // two intermediate zero timers also draw.
            for (var draw = 0; draw < 3 * (6 + transition[10].GetInt32() + 1) + 2; draw++) expectedRandom.NextUnit();
            Assert.Equal(expectedRandom.State, after.RandomState);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 9, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(3, Assert.Single(after.World.Missions).LiveUfos);
            Assert.All(after.World.Ufos, ufo =>
            {
                Assert.Equal(1, ufo.TrajectoryPoint);
                Assert.Equal(UfoStatus.Flying, ufo.Status);
                Assert.Equal(transition[5].GetString(), ufo.Altitude);
                Assert.Equal(transition[8].GetInt32(), ufo.Speed);
                Assert.Equal(Degrees(separate ? 8.01 : 8), ufo.Longitude);
                Assert.Equal(Degrees(2), ufo.Latitude);
                Assert.Equal(ufo.Position, ufo.Destination!.Position);
                Assert.Equal(0, ufo.LandId);
                Assert.False(ufo.HunterKiller);
                Assert.False(ufo.Escort);
                Assert.Equal(10, ufo.Shield);
            });
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-first-airborne.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2, ufo.TrajectoryPoint));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseBirthsStillReserveIdsAndActivityBeforeMutation(bool cache)
    {
        var content = Content(cache, false);
        var initial = Snapshot(content, 17).WithNextId("STR_UFO_UNIQUE", int.MaxValue - 2);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO unique IDs are exhausted or collide with a saved UFO.");
        initial = Snapshot(content, 17);
        initial = initial with { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 8] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneBirthsKeepLaterGroundSelectionBoundaries(bool cache)
    {
        var content = Content(cache, false, nextAltitude: 0);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17), content, new SplitMix64RandomSource(17)),
            "Alien mission wave spawning requires world simulation.");
    }

    internal static RuntimeContent Content(bool cache, bool separate, string interrupt = "", int nextAltitude = 3,
        string extraRules = "", string generationRegion = "", int firstAltitude = 3, int groundTimer = 0,
        int points = 0, bool alerts = false, int nextSpeedPercentage = 80) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            {{{extraRules}}}
            ufos:
              - {type: FIRST_ARRIVAL_UFO, speedMax: 2200, damageMax: 50, missionScore: 3, noAlert: {{{(alerts ? "false" : "true")}}}, shieldCapacity: 10, shieldRechargeInGeoscape: 100}
            ufoTrajectories:
              - {id: FIRST_ARRIVAL_AIR, groundTimer: {{{groundTimer}}}, waypoints: [[5, {{{firstAltitude}}}, 100], [{{{(separate ? 6 : 5)}}}, 2, {{{nextSpeedPercentage}}}], [4, {{{nextAltitude}}}, 100]]}
            alienMissions:
              - type: FIRST_ARRIVAL_MISSION
                interruptResearch: {{{interrupt}}}
                objective: 0
                points: {{{points}}}
            {{{(generationRegion.Length == 0 ? "" : "    regionWeights: {0: {" + generationRegion + ": 1}}")}}}
                waves:
                  - {ufo: FIRST_ARRIVAL_UFO, count: 1, trajectory: FIRST_ARRIVAL_AIR, timer: 0}
                  - {ufo: FIRST_ARRIVAL_UFO, count: 1, trajectory: FIRST_ARRIVAL_AIR, timer: 0}
                  - {ufo: FIRST_ARRIVAL_UFO, count: 1, trajectory: FIRST_ARRIVAL_AIR, timer: 0}
            """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed)
    {
        var initial = CreateSnapshot(content, "Newborn first arrival");
        return initial with
        {
            RandomState = seed,
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            World = initial.World with { Missions = [new(4, "FIRST_ARRIVAL_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewbornReplacementSpeedOverflowRefusesTheTickAtomically(bool cache)
    {
        var content = Content(cache, false, nextSpeedPercentage: 100_000_000);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17), content, new SplitMix64RandomSource(17)),
            "Alien mission wave spawning requires world simulation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumSafeNewbornReplacementSpeedStillExecutes(bool cache)
    {
        var content = Content(cache, false, nextSpeedPercentage: 97_612_893);
        var campaign = CampaignState.Restore(Snapshot(content, 17), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2_147_483_646, ufo.Speed));
        campaign = Reload(content, campaign, 17, "newborn-replacement-speed-limit.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2, ufo.TrajectoryPoint));
    }
}
