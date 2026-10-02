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
    public void SupplySpawnSuppressionAndConstructorChoiceMatchReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-origin");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-supply-decisions.rul"], cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "supplyRows"))
            foreach (var operation in new[] { 0, 1 })
            {
                var defined = row[0].GetInt32();
                var objective = row[1].GetInt32();
                var linked = row[2].GetInt32() != 0;
                var percentage = row[3].GetInt32();
                var initial = CreateSnapshot(content, "Supply decision");
                var mission = new AlienMissionSnapshot(4, $"SUPPLY_{defined}_{objective}_{percentage}_{operation}",
                    "REGION", "RACE_A", 0, 0, 0, 0, -1)
                { AlienBase = linked ? new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 7, 0.2, 0.1) : null };
                var snapshot = initial with
                {
                    Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                    RandomState = 17,
                    World = initial.World with
                    {
                        Missions = [mission],
                        AlienBases = linked ? [new AlienBaseSnapshot(7, "ALIEN_BASE_DEPLOY", "RACE_A", 0.2, 0.1)] : [],
                    },
                };
                var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
                if (row[5].GetInt32() > 0)
                    AssertTimeBlocked(campaign, "Alien mission wave spawning requires world simulation.");
                else
                {
                    AdvanceOne(campaign);
                    var after = campaign.Capture();
                    Assert.Equal(row[4].GetInt32() * 2, after.World.Ufos.Count);
                    Assert.All(after.World.Ufos, ufo => Assert.False(ufo.HunterKiller));
                    if (after.World.Ufos.Count == 0) Assert.Empty(after.World.Missions);
                    else Assert.Equal(2, Assert.Single(after.World.Missions).LiveUfos);
                    Reload(content, campaign, 17, "supply-decision.sav");
                }
            }
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceBaseOriginsAndSupplyRandomOrderMatchReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-origin");
        var content = Load(cache);
        var regions = content.RuntimeRules.Regions;
        var region = regions[regions.GetRequired("REGION")].Value;
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var supply = row[0].GetInt32() != 0;
            var operation = row[1].GetInt32();
            var linked = row[2].GetInt32() != 0;
            var rule = $"ORIGIN_{(supply ? 5 : 0)}_{operation}";
            var mission = new AlienMissionSnapshot(4, rule, "REGION", "RACE_A", 0, 0, 0, 0, -1)
            { AlienBase = linked ? new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 7, 0.2, 0.1) : null };
            var initial = CreateSnapshot(content, "Source base wave") with
            { Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55), RandomState = 17 };
            var snapshot = initial with
            {
                World = initial.World with
                {
                    Missions = [mission],
                    AlienBases = linked ?
                [new AlienBaseSnapshot(7, "ALIEN_BASE_DEPLOY", "RACE_A", 0.2, 0.1)] : []
                }
            };
            var random = new SplitMix64RandomSource(17);
            WorldPosition position = default;
            if (row[5].GetInt32() != 0)
            {
                var raw = WorldGeometry.RandomPoint(region, 4, -1, random);
                position = WorldPosition.Create(raw.Longitude, raw.Latitude);
            }
            if (linked && operation != 0) position = new(row[3].GetDouble(), row[4].GetDouble());
            var target = WorldGeometry.RandomPoint(region, 3, -1, random);
            var destination = WorldPosition.Create(target.Longitude, target.Latitude);
            var countdown = WorldTrajectory.SpawnCountdown(9000, random);
            random.NextInclusive(0, 99); // base detection, even with no radar
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(WorldGeometry.Move(position, destination, WorldGeometry.RadianSpeed(2200)), ufo.Position);
            Assert.Equal(destination, ufo.Destination!.Position);
            Assert.Equal(countdown, Assert.Single(after.World.Missions).SpawnCountdown);
            Assert.Equal(random.State, after.RandomState);
            Reload(content, campaign, 17, "source-base-wave.sav");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FinalNoObjectWaveChecksCleanupReferencesBeforeMutatingTheTick(bool cache, bool ufoLink)
    {
        var content = Load(cache);
        foreach (var ruleId in new[] { "EMPTY_OBJECTIVE_5", "CHAIN_zero" })
        {
            var campaign = Restore(content, new AlienMissionSnapshot(4, ruleId, "REGION", "RACE_A", ruleId == "CHAIN_zero" ? 0 : 1, 0, 0, 0, -1));
            var before = campaign.Capture();
            var snapshot = before with
            {
                Bases = [before.Bases[0] with { RetaliationMissionId = ufoLink ? null : 4 }],
                World = before.World with
                {
                    Ufos = ufoLink ?
                    [new UfoSnapshot(9, "UFO_SPAWN", 4, "TRAJ_PATROL", 0, 0.4, 0.2, UfoStatus.Flying, WorldAltitudes.High)
                    { Speed = 2200, MissionWaveNumber = 1, Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)) }] : []
                }
            };
            var restored = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            AssertTimeBlocked(restored, ufoLink ? "Alien mission cannot expire while UFOs still reference it." :
                "A non-retaliation mission cannot expire while a base links it as retaliation.");
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
