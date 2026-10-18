using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSupplySourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SpaceSupplySourceLossSuppressesOnlyObjectiveUfosAndPersistsCounterCleanup(int kind)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-origin");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "supplyRows"), row =>
            row[0].GetInt32() == 1 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0 && row[4].GetInt32() == 0);
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = Content(kind);
        var deletedContent = Content(kind, "CORE");
        var survivedContent = Content(kind, "");
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, kind);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            var births = kind is 3 or 4 ? 0 : kind == 1 ? 1 : kind == 2 ? (deleted ? 1 : 2) : deleted ? 0 : 1;
            Assert.Equal(births, after.World.Ufos.Count);
            Assert.Equal(kind == 3 ? 1 : 0, after.World.MissionSites.Count);
            Assert.Equal(kind == 4 || births != 0 ? 1 : 0, after.World.Missions.Count);
            Assert.Equal(!deleted, after.World.AlienBases.Any());
            Assert.Equal(initial.NextIds.GetValueOrDefault("STR_UFO_UNIQUE", 1) + births,
                after.NextIds.GetValueOrDefault("STR_UFO_UNIQUE", 1));
            Assert.All(after.World.Missions, mission =>
            {
                Assert.Equal(deleted, mission.AlienBase is null);
                Assert.False(mission.Interrupted);
                Assert.Equal(births, mission.LiveUfos);
            });
            Assert.All(after.World.Ufos, ufo => { Assert.False(ufo.HunterKiller); Assert.False(ufo.Escort); });
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            campaign = ReloadRepeatedly(content, campaign, seed, "optional-space-supply-source.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    public void SourceSuppressedSupplyCannotSupplySignedActivityHeadroom(int score)
    {
        var content = Content(0, points: score < 0 ? 7 : -7, followingScore: score);
        var initial = ScoreSnapshot(content, score < 0 ? int.MinValue + 3 : int.MaxValue - 3);
        for (ulong seed = 1; seed <= 32; seed++)
            AssertTimeBlocked(CampaignState.Restore(initial with { RandomState = seed }, content, new SplitMix64RandomSource(seed)),
                "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    public void SafeSupplyActivityLimitsMatchSelectedSourceControlsAndReloads(int score)
    {
        var points = score < 0 ? 7 : -7;
        var content = Content(0, points: points, followingScore: score);
        var deletedContent = Content(0, "CORE", points, score);
        var survivedContent = Content(0, "", points, score);
        var initial = ScoreSnapshot(content, score < 0 ? int.MinValue + 5 : int.MaxValue - 5);
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var selected = initial with { RandomState = seed };
            var campaign = CampaignState.Restore(selected, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + score + (deleted ? 0 : points), after.Regions[0].ActivityAlien[^1]);
            var control = CampaignState.Restore(selected, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            campaign = ReloadRepeatedly(content, campaign, seed, "optional-supply-source-score.sav", 3, after);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    [Fact]
    public void PossibleSupplyBirthStillReservesIdentityAndEarlyCleanup()
    {
        var content = Content(0);
        var initial = Snapshot(content, 17).WithNextId("STR_UFO_UNIQUE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO unique IDs are exhausted or collide with a saved UFO.");
        initial = Snapshot(content, 17);
        initial = initial with { Bases = [initial.Bases[0] with { RetaliationMissionId = 4 }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "A non-retaliation mission cannot expire while a base links it as retaliation.");
    }

    [Fact]
    public void ConditionalSupplyTerminalLeadersRetainTheirArrivalBoundary()
    {
        var content = Content(0);
        var initial = Snapshot(content, 17);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 1 }],
                Ufos = [new(9, "OPTIONAL_SUPPLY_UFO", 4, "OPTIONAL_SUPPLY_AIR", 2, 0.4, 0.2, UfoStatus.Flying, "STR_HIGH_UC")
            { Speed = 2200, HunterKiller = true, Escort = true, MissionWaveNumber = 0, Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)) }],
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Loaded idle escorts require same-tick newborn waypoint forecasts.");
    }

    [Fact]
    public void DelayedSpaceSupplyUnlinkingDoesNotInventConditionalLeaderWork()
    {
        var content = Content(4);
        var deletedContent = Content(4, "CORE");
        var survivedContent = Content(4, "");
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, 4);
            initial = initial with
            {
                World = initial.World with
                {
                    Missions = [initial.World.Missions[0] with { LiveUfos = 1 }],
                    Ufos = [new(9, "OPTIONAL_SUPPLY_UFO", 4, "OPTIONAL_SUPPLY_AIR", 0, 0.4, 0.2, UfoStatus.Flying, "STR_HIGH_UC")
                { Speed = 2200, HunterKiller = true, MissionWaveNumber = 0, Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)) }],
                }
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.False(Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(deleted, after.World.Missions[0].AlienBase is null);
            Assert.Equal(8970, after.World.Missions[0].SpawnCountdown);
            Assert.True(Assert.Single(after.World.Ufos).HunterKiller);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            campaign = ReloadRepeatedly(content, campaign, seed, "delayed-space-supply-source.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    internal static RuntimeContent Content(int kind, string code = "BONUS", int points = 3, int? followingScore = null, int radarRange = 0, int nonObjectiveHunterPercentage = 100, bool nonObjectiveEscort = false, bool nonObjectiveRegional = false, int craftRadarRange = 0)
    {
        var wave = kind == 3 ? "{ufo: OPTIONAL_SUPPLY_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0, objective: true}" :
            "{ufo: OPTIONAL_SUPPLY_UFO, count: 1, trajectory: OPTIONAL_SUPPLY_AIR, timer: " + (kind == 4 ? 9000 : 0) +
            ", objective: " + (kind is 1 or 7 ? "false" : "true") + (kind == 5 ? ", hunterKillerPercentage: 100, escort: true" : kind == 7 ? ", hunterKillerPercentage: " + nonObjectiveHunterPercentage + ", escort: true" : "") + "}";
        return StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            {{{(craftRadarRange > 0 ? "crafts: [{type: SHIP, radarRange: " + craftRadarRange + ", radarChance: 100}]" : "")}}}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienDeployments:
              - {type: OPTIONAL_OPERATION_BASE, markerName: OPTIONAL_SOURCE, baseSelfDestructCode: {{{code}}}}
              - {type: OPTIONAL_SUPPLY_SITE, markerName: OPTIONAL_SUPPLY_SITE_MARKER, duration: [1, 1]}
            {{{(followingScore is { } score ? "  - {type: OPTIONAL_SUPPLY_ACTIVITY_SITE, markerName: OPTIONAL_SUPPLY_ACTIVITY, duration: [1, 1], points: " + score + "}" : "")}}}
            ufos:
              - {type: OPTIONAL_SUPPLY_UFO, speedMax: 2200, damageMax: 50, missionScore: {{{points}}}, noAlert: true, radarRange: {{{radarRange}}}}
            ufoTrajectories:
              - {id: OPTIONAL_SUPPLY_AIR, waypoints: [[4, 3, 100], [5, 2, 80], [4, 3, 100]]}
              - {id: OPTIONAL_SUPPLY_REGIONAL, waypoints: [[0, 3, 100], [5, 2, 80], [4, 3, 100]]}
            alienMissions:
              - type: OPTIONAL_SPACE_SUPPLY
                objective: 5
                operationType: 0
                spawnZone: 5
                waves:
                  - {{{wave}}}
            {{{(kind is 2 or 6 ? "      - {ufo: OPTIONAL_SUPPLY_UFO, count: 1, trajectory: " + (nonObjectiveRegional ? "OPTIONAL_SUPPLY_REGIONAL" : "OPTIONAL_SUPPLY_AIR") + ", timer: 0, hunterKillerPercentage: " + (kind == 6 ? nonObjectiveHunterPercentage : 0) + (nonObjectiveEscort ? ", escort: true" : "") + "}" : "")}}}
            """, finalRules: "crafts: [{type: SHIP, fuelMax: 1000}]\n");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int kind = 0)
    {
        var initial = CreateSnapshot(content, "Optional Space supply") with { RandomState = seed, Time = new(2, 2, 1, 1999, 23, 59, 55) };
        return StrategicWorldOptionalBaseMissionTests.Source(initial with
        {
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)], Soldiers = [] }],
            World = initial.World with { Missions = [new(4, "OPTIONAL_SPACE_SUPPLY", "REGION", "RACE_A", 0, 0, kind == 4 ? 9000 : 0, 0, -1)] },
        });
    }

    private static CampaignSnapshot ScoreSnapshot(RuntimeContent content, int score)
    {
        var initial = Snapshot(content, 17);
        return initial with
        {
            Regions = [.. initial.Regions.Select(region => region with { ActivityAlien = [score] })],
            World = initial.World with { MissionSites = [new(30, "MISSION_SCOUT", "OPTIONAL_SUPPLY_ACTIVITY_SITE", "RACE_A", 0.2, 0.1, 3600) { Detected = true }] },
        };
    }
}
