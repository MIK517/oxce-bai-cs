using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicObjectiveBaseSpawnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectiveBaseSearchAndDeploymentSelectionMatchPinnedReference(bool cache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-objective-base.rul"], cache);
        var rules = content.RuntimeRules;
        var template = rules.AlienMissions[rules.AlienMissions.GetRequired("BASE_OBJECTIVE")].Value;
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-objective-base-spawn");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var success = row[0].GetInt32();
            var geometry = row[1].GetInt32();
            var kind = row[2].GetInt32();
            var chance = row[3].GetInt32();
            var fake = row[4].GetInt32() != 0;
            var allow = row[5].GetInt32() != 0;
            var x = geometry == 0 ? 0.2 : 2.0;
            var region = new RuntimeRegionRule(0, geometry == 2 ? [] : [new(0, 1, -0.5, 0.5)],
                [new([new(x, x, 0.1, 0.1, 100 + kind, "")])], new Dictionary<string, ulong>(), 0, "", null, [], []);
            var mission = template with { SiteTypeId = kind == 0 ? "EXPLICIT" : "" };
            var random = new TestFixtures.CountingMinimumRandomSource();
            var landCalls = 0;
            var error = false;
            WorldObjectiveBaseSpawn.Selection? selected = null;
            try
            {
                selected = WorldObjectiveBaseSpawn.Select(region, 0, kind != 0, allow, random,
                    area => WorldObjectiveBaseSpawn.Deployment(rules, mission, area, random),
                    _ => chance, _ => ++landCalls >= success && success != 0, _ => fake);
            }
            catch (KeyNotFoundException) { error = true; }
            var id = selected is { } value ? rules.AlienDeployments.GetExternalId(value.Deployment) : "";
            if (selected is { } result) Assert.Equal(new WorldPosition(x, 0.1), result.Position);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                success, geometry, kind, chance, fake ? 1 : 0, allow ? 1 : 0,
                selected is null ? 0 : 1, error ? 1 : 0, random.IntegerCalls, random.UnitCalls, landCalls, id,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalWavesCreateFixedDynamicAndFallbackBasesAndReloadWithoutDuplicateEffects(bool cache)
    {
        foreach (var kind in new[] { "EXPLICIT", "single", "weighted", "fallback" })
            foreach (var ufo in new[] { false, true })
            {
                var content = LoadWorld(cache, kind, ufo: ufo);
                var initial = Initial(content);
                var expected = new SplitMix64RandomSource(17);
                var rules = content.RuntimeRules;
                var region = rules.Regions[rules.Regions.GetRequired("OBJECTIVE_REGION")].Value;
                var rule = rules.AlienMissions[rules.AlienMissions.GetRequired("FINAL_BASE")].Value;
                if (ufo)
                {
                    WorldGeometry.RandomPoint(region, 0, -1, expected);
                    WorldGeometry.RandomPoint(region, 1, -1, expected);
                }
                var selected = WorldObjectiveBaseSpawn.Select(rules, rule, region, expected)!.Value;
                if (ufo) expected.NextInclusive(0, 99); // base detection follows creation
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
                campaign = Reload(content, campaign, 17, "before-objective-base.sav");
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var alienBase = Assert.Single(after.World.AlienBases);
                Assert.Equal(rules.AlienDeployments.GetExternalId(selected.Deployment), alienBase.DeploymentId);
                Assert.Equal(selected.Position, alienBase.Position);
                Assert.Equal(expected.State, after.RandomState);
                Assert.Equal("RACE_B", alienBase.Race);
                Assert.Equal(0, alienBase.StartMonth);
                Assert.False(alienBase.Discovered);
                Assert.Equal(1UL, after.World.AlienBaseBirthSequence);
                Assert.Equal("alien-base:generated:1", alienBase.PreservationKey);
                Assert.Equal(1, alienBase.Id);
                Assert.Contains(alienBase.Position, new[] { new WorldPosition(Degrees(8), Degrees(2)), new(Degrees(12), Degrees(-2)) });
                Assert.Equal(ufo ? 1 : 0, after.World.Ufos.Count);
                Assert.Equal(ufo ? 1 : 0, after.World.Missions.Count);
                if (ufo)
                {
                    var mission = Assert.Single(after.World.Missions);
                    Assert.Equal((1, 0, 1), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos));
                    Assert.Null(mission.AlienBase); // objective birth does not become the operation source
                }
                Assert.Equal(13, after.Regions.Sum(state => (long)state.ActivityAlien[^1]) -
                    initial.Regions.Sum(state => (long)state.ActivityAlien[^1]));
                campaign = Reload(content, campaign, 17, "objective-base-created.sav");
                AdvanceUnblocked(campaign, 1);
                Assert.Single(campaign.Capture().World.AlienBases);
                Assert.Equal(1UL, campaign.Capture().World.AlienBaseBirthSequence);
                Reload(content, campaign, 17, "objective-base-after.sav");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedFallbackFinishesMissionWithoutInterruptingOrAllocatingBase(bool cache)
    {
        foreach (var allow in new[] { false, true })
        {
            var content = LoadWorld(cache, "weighted", chance: 100, allow: allow);
            var initial = Initial(content);
            var random = new SplitMix64RandomSource(17);
            var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("OBJECTIVE_REGION")].Value;
            var rule = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("FINAL_BASE")].Value;
            var selected = WorldObjectiveBaseSpawn.Select(content.RuntimeRules, rule, region, random);
            Assert.Equal(allow, selected.HasValue);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.Missions);
            Assert.Equal(allow ? 1 : 0, after.World.AlienBases.Count);
            Assert.Equal(allow ? 1UL : 0UL, after.World.AlienBaseBirthSequence);
            Assert.Equal(random.State, after.RandomState);
            Assert.Equal(allow ? 13 : 0, after.Regions.Sum(state => (long)state.ActivityAlien[^1]) -
                initial.Regions.Sum(state => (long)state.ActivityAlien[^1]));
            Reload(content, campaign, 17, "objective-base-suppressed.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateChainsCompleteOnceAndComposedMissionsReserveIdsAndScores(bool cache)
    {
        var content = LoadWorld(cache, chain: true);
        var initial = Initial(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.AlienBases);
        Assert.Empty(campaign.Capture().World.Missions);
        Reload(content, campaign, 17, "objective-base-chain.sav");
        initial = initial with
        {
            World = initial.World with { Missions = [initial.World.Missions[0], initial.World.Missions[0] with { Id = 5 }] },
        };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal([1, 2], campaign.Capture().World.AlienBases.Select(value => value.Id));
        Assert.Equal(26, campaign.Capture().Regions.Sum(value => (long)value.ActivityAlien[^1]) -
            initial.Regions.Sum(value => (long)value.ActivityAlien[^1]));
        var overflow = initial with { Regions = initial.Regions.Select(value => value with { ActivityAlien = [int.MaxValue - 13] }).ToArray() };
        AssertTimeBlocked(CampaignState.Restore(overflow, content, new SplitMix64RandomSource(17)), "UFO alien activity exceeds the supported range.");
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("OBJECTIVE_BASE", int.MaxValue - 1), content,
            new SplitMix64RandomSource(17)), "Base objective IDs are exhausted or collide.");
    }

    [Fact]
    public void ObjectivePreflightRejectsMarkerCollisionBirthExhaustionAndInvalidInputsAtomically()
    {
        var content = LoadWorld(false);
        var initial = Initial(content);
        var collision = initial with { World = initial.World with { AlienBases = [new(1, "EXPLICIT", "RACE_A", 0.2, 0.1)] } };
        AssertTimeBlocked(CampaignState.Restore(collision, content, new SplitMix64RandomSource(17)), "Base objective IDs are exhausted or collide.");
        var exhausted = initial with { World = initial.World with { AlienBaseBirthSequence = ulong.MaxValue } };
        AssertTimeBlocked(CampaignState.Restore(exhausted, content, new SplitMix64RandomSource(17)), "Base objective birth sequence is exhausted.");
        content = LoadWorld(false, marker: "SHIP");
        var craftInitial = Initial(content);
        var craftId = craftInitial.Bases.SelectMany(owner => owner.Crafts).First(craft => craft.RuleId == "SHIP").Id;
        Assert.Equal("SHIP", content.RuntimeRules.AlienDeployments[content.RuntimeRules.AlienDeployments.GetRequired("COLLISION_BASE")].Value.MarkerName);
        AssertTimeBlocked(CampaignState.Restore(craftInitial.WithNextId("SHIP", craftId), content,
            new SplitMix64RandomSource(17)), "Base objective IDs are exhausted or collide.");
        content = LoadWorld(false, zone: 99);
        AssertTimeBlocked(CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17)),
            "A mission tried to use zone 99, but the region only defines 2 zones.");
        content = LoadWorld(false, "zero");
        AssertTimeBlocked(CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17)),
            "Base objective texture weights select an unavailable deployment.");
        content = LoadWorld(false, marker: "\"\"");
        AssertTimeBlocked(CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17)),
            "Base objective uses an unsupported marker namespace.");
        // SavedGame::getId keys are external names: a whitespace marker is its own namespace.
        content = LoadWorld(false, marker: "\" \"");
        var whitespace = CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(whitespace, 1);
        Assert.Contains(whitespace.Capture().World.AlienBases, value => value.DeploymentId == "COLLISION_BASE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingObjectiveConditionAndDefaultTextureMatchPinnedReference(bool cache)
    {
        var contents = Enumerable.Range(0, 3).Select(kind => StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-objective-base.rul"], cache,
            kind == 1 ? "globe:\n  textures:\n    - {id: 0, deployments: {TEXTURE: 2, Z_UNUSED: 3}}" : null)).ToArray();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-objective-base-spawn");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "landingRows"))
        {
            var kind = row[7].GetInt32();
            var rules = contents[kind].RuntimeRules;
            var template = rules.AlienMissions[rules.AlienMissions.GetRequired("BASE_OBJECTIVE")].Value;
            var rule = template with
            {
                Objective = (RuntimeMissionObjective)row[0].GetInt32(),
                SpawnZone = row[4].GetInt32(),
                SiteTypeId = kind == 0 ? "EXPLICIT" : "",
            };
            var wave = rule.Waves[0] with
            {
                ObjectiveOnTheLandingSite = row[1].GetInt32() != 0,
                Objective = row[2].GetInt32() != 0,
            };
            var random = new TestFixtures.CountingMinimumRandomSource();
            var creates = WorldObjectiveBaseSpawn.CreatesOnLanding(rule, wave, row[3].GetInt32(),
                row[5].GetInt32() != 0, row[6].GetInt32() != 0);
            var id = creates ? rules.AlienDeployments.GetExternalId(WorldObjectiveBaseSpawn.Deployment(rules, rule,
                new(0, 0, 0, 0, 0, ""), random)) : "";
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(), row[4].GetInt32(),
                row[5].GetInt32(), row[6].GetInt32(), kind, creates ? 1 : 0, random.IntegerCalls, random.UnitCalls, id,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingCreatesAtActualPositionRegardlessOfObjectiveFlagAndPersistsThroughTakeoff(bool cache)
    {
        foreach (var objective in new[] { false, true })
        {
            // A 100% fake-water base rule does not prevent a normal-land birth here:
            // this path makes no preference roll or search.
            var content = LoadWorld(cache, chance: 100, landing: true, zone: 0, objectiveFlag: objective);
            var initial = LandingInitial(content);
            var rules = content.RuntimeRules;
            var region = rules.Regions[rules.Regions.GetRequired("OBJECTIVE_REGION")].Value;
            var expected = new SplitMix64RandomSource(17);
            WorldGeometry.RandomPoint(region, 1, -1, expected);
            WorldGeometry.RandomPoint(region, 1, -1, expected);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            campaign = Reload(content, campaign, 17, "before-landing-base.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var alienBase = Assert.Single(after.World.AlienBases);
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), alienBase.Position);
            Assert.Equal(ufo.Position, alienBase.Position);
            Assert.Equal("EXPLICIT", alienBase.DeploymentId);
            Assert.Equal("RACE_B", alienBase.Race);
            Assert.Equal(90, alienBase.MinutesSinceLastHuntMissionGeneration);
            Assert.False(alienBase.Discovered);
            Assert.Equal(UfoStatus.Landed, ufo.Status);
            Assert.Equal(10, ufo.SecondsRemaining);
            Assert.Equal(expected.State, after.RandomState);
            Assert.Equal(13, after.Regions.Sum(value => (long)value.ActivityAlien[^1]) -
                initial.Regions.Sum(value => (long)value.ActivityAlien[^1]));
            campaign = Reload(content, campaign, 17, "landing-base-created.sav");
            AdvanceUnblocked(campaign, 2);
            Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
            Assert.Single(campaign.Capture().World.AlienBases);
            Assert.Equal(1UL, campaign.Capture().World.AlienBaseBirthSequence);
            Reload(content, campaign, 17, "landing-base-takeoff.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingBirthIsSkippedForInvalidTerrainWrongCurrentZoneAndAbsentFlag(bool cache)
    {
        foreach (var scenario in new[] { "water", "zone", "flag", "conversion" })
        {
            var content = LoadWorld(cache, landing: true, zone: scenario == "zone" ? 1 : 0,
                landingFlag: scenario != "flag", objectiveFlag: scenario == "conversion");
            var initial = LandingInitial(content);
            if (scenario == "water") initial = initial with
            {
                World = initial.World with
                {
                    Ufos = [initial.World.Ufos[0] with
                    {
                        Longitude = Degrees(45), Latitude = Degrees(2),
                        Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(45), Degrees(2))),
                    }],
                },
            };
            if (scenario == "conversion") initial = initial with
            {
                World = initial.World with { Missions = [initial.World.Missions[0] with { MissionSiteZoneArea = 0 }] },
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            Assert.Empty(campaign.Capture().World.AlienBases);
            Assert.Equal(0UL, campaign.Capture().World.AlienBaseBirthSequence);
            if (scenario == "conversion")
            {
                Assert.Single(campaign.Capture().World.MissionSites);
                Assert.Equal(UfoStatus.Destroyed, Assert.Single(campaign.Capture().World.Ufos).Status);
            }
            else Assert.Equal(UfoStatus.Landed, Assert.Single(campaign.Capture().World.Ufos).Status);
            Reload(content, campaign, 17, "landing-base-skipped.sav");
        }
    }

    [Fact]
    public void LandingAndFinalWaveBirthsShareScoreIdentityAndCounterReservations()
    {
        var content = LoadWorld(false, landing: true, zone: 0);
        var initial = LandingInitial(content) with { Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55) };
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0], initial.World.Ufos[0] with { UniqueId = 10 }],
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 },
                    new(5, "FINAL_BASE_NO_OBJECT", "OBJECTIVE_REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal([1, 2, 3], campaign.Capture().World.AlienBases.Select(value => value.Id));
        Assert.Equal(45, campaign.Capture().Regions.Sum(value => (long)value.ActivityAlien[^1]) -
            initial.Regions.Sum(value => (long)value.ActivityAlien[^1])); // 2 UFOs*3 and 3 births*13
        Reload(content, campaign, 17, "composed-landing-bases.sav");
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("OBJECTIVE_BASE", int.MaxValue - 2),
            content, new SplitMix64RandomSource(17)), "Base objective IDs are exhausted or collide.");
        var overflow = initial with { Regions = initial.Regions.Select(value => value with { ActivityAlien = [int.MaxValue - 39] }).ToArray() };
        AssertTimeBlocked(CampaignState.Restore(overflow, content, new SplitMix64RandomSource(17)), "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void LandingCounterForecastWorkIsBoundedAcrossExpiringSitesAndManyBirths()
    {
        var content = LoadWorld(false, landing: true, zone: 0);
        var initial = LandingInitial(content) with { Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55) };
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 100 }],
                Ufos = Enumerable.Range(1, 100).Select(id => initial.World.Ufos[0] with { UniqueId = id }).ToArray(),
                MissionSites = Enumerable.Range(1, 3000).Select(id =>
                    new MissionSiteSnapshot(id, "MISSION_SCOUT", "COUNTER_SITE", "RACE_A", 0.2, 0.1, 0)
                    { Detected = true }).ToArray(),
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective creation exceeds the supported work limit.");
    }

    [Fact]
    public void ObjectiveBirthsReserveCombinedExistingAndNewBasePatrolDiscoveryWork()
    {
        var content = LoadWorld(false);
        var initial = Initial(content);
        var owner = initial.Bases[0];
        var prototype = owner.Crafts.First(craft => craft.RuleId == "SHIP");
        Assert.True(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.SightRange > 0);
        initial = initial with
        {
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts, .. Enumerable.Range(1000, 4000).Select(id => prototype with
                {
                    Id = id, Logistics = prototype.Logistics! with
                    {
                        Status = "STR_OUT", Takeoff = 0, Destination = null, Speed = 0, Fuel = 100, Damage = 0,
                        Longitude = owner.Longitude, Latitude = owner.Latitude,
                    },
                })],
            }],
            World = initial.World with
            {
                Missions = Enumerable.Range(1, 80).Select(id => initial.World.Missions[0] with { Id = id }).ToArray(),
                AlienBases = Enumerable.Range(1000, 190).Select(id => new AlienBaseSnapshot(id, "EXPLICIT", "RACE_A", 0.2, 0.1)).ToArray(),
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective patrol discovery exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NewFinalWaveBaseReservesHuntingMissionsAndStartsDelayedWaves(bool cache, bool regional)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = LoadWorld(cache, hunting: true, regional: regional);
        var initial = Initial(content).WithNextId("ALIEN_MISSIONS", int.MaxValue);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Destination = null,
                Speed = 0,
                Fuel = 100,
                Damage = 0,
                Longitude = Degrees(8),
                Latitude = Degrees(2),
                LowFuel = false,
                MissionComplete = false,
            },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission IDs or population exceed the supported limit.");
        var hunting = CampaignState.Restore(initial.WithNextId("ALIEN_MISSIONS", 5), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(hunting, 1);
        var hunt = Assert.Single(hunting.Capture().World.Missions);
        Assert.Equal("MISSION_SCOUT", hunt.RuleId);
        Assert.Equal(5, hunt.Id);
        Assert.NotNull(hunt.AlienBase);
        Assert.InRange(hunt.SpawnCountdown, 4470, 13470);
        Assert.Equal(0, Assert.Single(hunting.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Reload(content, hunting, 17, "new-base-hunt-start.sav");
        // No available craft still permits birth and the reference's timer fallthrough.
        var peaceful = Initial(content);
        var campaign = CampaignState.Restore(peaceful, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(100, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Reload(content, campaign, 17, "new-base-hunting-timer.sav");
        content = LoadWorld(cache, hunting: true, huntTimer: int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17)),
            "New base objective hunting timer exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeLandingBaseTimerIsRejectedBeforeCreatingUnreloadableState(bool cache)
    {
        var content = LoadWorld(cache, landing: true, zone: 0, hunting: true, huntTimer: -1);
        AssertTimeBlocked(CampaignState.Restore(LandingInitial(content), content, new SplitMix64RandomSource(17)),
            "Base objective hunting timer cannot be negative.");
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    public void SharedCraftProductionMarkersReserveThePostProductionBaseId(bool cache, bool landing, bool fallback, bool arrivingEngineers)
    {
        var content = LoadWorld(cache, marker: "SHIP", landing: landing, zone: landing ? 0 : 1);
        var initial = landing ? LandingInitial(content) : Initial(content);
        var owner = initial.Bases[0];
        var nextCraft = owner.Crafts.Where(craft => craft.RuleId == "SHIP").Max(craft => craft.Id) + 1;
        initial = initial.WithNextId("SHIP", nextCraft) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities, new("BASE_MARKER_WORKSHOP", 5, 5, 0, 0, false, false, false)],
                Engineers = arrivingEngineers ? 0 : fallback ? 1 : owner.Engineers,
                Transfers = arrivingEngineers ? [new(700, 1, CampaignTransferKind.Engineer, "", 1)] : owner.Transfers,
                Productions = [new("BASE_MARKER_CRAFT", fallback ? 0 : 1, 0, 1, false, false, fallback, new Dictionary<string, int>())],
            }],
            World = initial.World with { AlienBases = [new(nextCraft + 1, "COLLISION_BASE", "RACE_A", 0.2, 0.1)] },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
        initial = initial with { World = initial.World with { AlienBases = [] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Contains(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP" && craft.Id == nextCraft);
        Assert.Equal(nextCraft + 1, Assert.Single(after.World.AlienBases).Id);
        Assert.Equal(nextCraft + 2, after.NextIds["SHIP"]);
        campaign = Reload(content, campaign, 17, "produced-craft-base.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.AlienBases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewbornHuntingExecutesFiniteNoObjectChainsButKeepsSupplySourceGuard(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = LoadWorld(cache, hunting: true, regional: true, huntingMission: "NEW_BASE_NO_OBJECT");
        var initial = Initial(content).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Destination = null,
                Speed = 0,
                Fuel = 100,
                Damage = 0,
                Longitude = Degrees(8),
                Latitude = Degrees(2),
                LowFuel = false,
                MissionComplete = false,
            },
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var hunt = Assert.Single(campaign.Capture().World.Missions);
        Assert.Equal("NEW_BASE_NO_OBJECT", hunt.RuleId);
        Assert.Equal((1, 0, 0), (hunt.NextWave, hunt.NextUfoCounter, hunt.LiveUfos));
        Assert.InRange(hunt.SpawnCountdown, 30, 90);
        Assert.Empty(campaign.Capture().World.Ufos);
        campaign = Reload(content, campaign, 17, "new-base-immediate-hunting.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(hunt, Assert.Single(campaign.Capture().World.Missions));
        content = LoadWorld(cache, hunting: true, regional: true, huntingMission: "NEW_BASE_SUPPLY");
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "New base hunting supply waves require a live source forecast.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewBaseHuntingUsesGroundedAndIncomingServicingForecasts(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = LoadWorld(cache, hunting: true, huntingMission: "NEW_BASE_SUPPLY",
            extra: "crafts: [{type: OBJECTIVE_REFUEL, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var incoming in new[] { false, true })
            foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => !row[0].GetBoolean() && !row[3].GetBoolean() && !row[4].GetBoolean()))
            {
                var initial = Initial(content) with { Time = new(1, 1, 1, 1999, 1, incoming ? 59 : 29, 55) };
                var owner = initial.Bases[0];
                var craft = owner.Crafts.Single(craft => craft.RuleId == "SHIP") with { RuleId = "OBJECTIVE_REFUEL" };
                craft = craft with
                {
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = row[1].GetInt32(),
                        Damage = 0,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                };
                initial = initial with
                {
                    Bases = [owner with { Crafts = incoming ? [] : [craft], Soldiers = [],
                    Transfers = incoming ? [new(1, 1, CampaignTransferKind.Craft, "OBJECTIVE_REFUEL", 1, Craft: craft)] : [] }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "objective-refuel-before.sav");
                var launches = row[6].GetString() == "STR_OUT" && (!incoming || row[1].GetInt32() < 100);
                if (launches)
                {
                    AssertTimeBlocked(campaign, "New base hunting supply waves require a live source forecast.");
                    continue;
                }
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                Assert.Empty(after.World.Missions);
                Assert.Equal(100, Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration);
                var state = Assert.Single(after.Bases[0].Crafts).Logistics!;
                Assert.Equal(row[5].GetInt32(), state.Fuel);
                Assert.Equal(incoming && row[1].GetInt32() == 100 ? "STR_READY" : row[6].GetString(), state.Status);
                Reload(content, campaign, 17, "objective-refuel-after.sav");
            }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void NewBaseHuntingUsesRelaunchedHiddenLeaderSpeedForItsEscort(bool cache, bool consumed, bool independent = false)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "escortFuelRows"), row => row[0].GetBoolean() && row[2].GetInt32() == 20 && !row[3].GetBoolean());
        var content = LoadWorld(cache, hunting: true, huntingMission: "NEW_BASE_SUPPLY", extra:
            "crafts: [{type: OBJECTIVE_ESCORT, speedMax: 6000, damageMax: 100, fuelMax: 100}, {type: OBJECTIVE_HIDDEN_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, undetectable: true}]\n",
            extraManufacture: consumed ? "- {name: CONSUME_OBJECTIVE_ESCORT, time: 1, requiredItems: {OBJECTIVE_ESCORT: 0}, producedItems: {SUPPLY: 0}}" : "");
        var initial = Initial(content) with { Time = new(1, 1, 1, 1999, 1, consumed ? 59 : 29, 55) };
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = template with
        {
            RuleId = "OBJECTIVE_HIDDEN_LEADER",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Damage = 0,
                Speed = 0,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        };
        var escort = template with
        {
            RuleId = "OBJECTIVE_ESCORT",
            Logistics = template.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 50,
                Damage = 0,
                Speed = 6000,
                Weapons = [],
                LowFuel = false,
                MissionComplete = false,
                Longitude = owner.Longitude,
                Latitude = owner.Latitude,
                Destination = new(WorldTargetKind.Craft, "OBJECTIVE_HIDDEN_LEADER", 2, owner.Longitude, owner.Latitude)
            }
        };
        initial = initial with { Bases = [owner with { Crafts = [leader, escort], Soldiers = [] }] };
        if (independent)
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Crafts = [leader, escort with { Logistics = escort.Logistics! with { Fuel = 100, Destination = null } }],
            }]
            };
        if (consumed) initial = initial with
        {
            Bases = [initial.Bases[0] with
            { Facilities = [.. owner.Facilities, new("BASE_MARKER_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("CONSUME_OBJECTIVE_ESCORT", 1, 0, 2, false, false, false, new Dictionary<string, int>())] }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "objective-escort-before.sav");
        if (!consumed) AssertTimeBlocked(campaign, "New base hunting supply waves require a live source forecast.");
        else
        {
            using var deleted = TestFixtures.ReadVerifiedExpected("strategic-hunting");
            Assert.NotEmpty(TestFixtures.Rows(deleted.RootElement, "deletedCraftFollowerRows"));
            AdvanceUnblocked(campaign, 1);
            Assert.DoesNotContain(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "OBJECTIVE_ESCORT");
            Assert.Empty(campaign.Capture().World.Missions);
            Assert.Equal(100, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
            Reload(content, campaign, 17, "objective-consumed-escort-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewZeroChanceBaseComposesWithStochasticProductionAndServicing(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-timer");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "rows"), row => row[0].GetString() == "zeroChance" && row[1].GetInt32() == 60);
        foreach (var chance in new[] { 0, 100 })
        {
            var content = LoadWorld(cache, hunting: true, huntingMission: "NEW_BASE_SUPPLY", huntingChance: chance, extra:
                "crafts: [{type: ZERO_OBJECTIVE_CRAFT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\n",
                extraManufacture: "- {name: ZERO_OBJECTIVE_PRODUCT, time: 1, producedItems: {SUPPLY: 1}, randomProducedItems: [[1, {SUPPLY: 1}], [1, {BULKY: 1}]]}");
            Assert.Equal(chance, content.RuntimeRules.AlienDeployments[content.RuntimeRules.AlienDeployments.GetRequired("HUNT_BASE")].Value.BaseDetectionChance);
            var initial = Initial(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                RuleId = "ZERO_OBJECTIVE_CRAFT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Damage = 0,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3
                }
            });
            var owner = initial.Bases[0];
            initial = initial with
            {
                Bases = [owner with
                { Facilities = [.. owner.Facilities, new("BASE_MARKER_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("ZERO_OBJECTIVE_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())] }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "zero-objective-before.sav");
            if (chance != 0)
            {
                AssertTimeBlocked(campaign, "UFO retargeting requires production fuel-stock forecasts.");
                continue;
            }
            var control = CampaignState.Restore(initial with { World = WorldSnapshot.Empty }, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(control, 1);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(100, Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration);
            Assert.Empty(after.World.Missions);
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(control.Capture().Bases)),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(after.Bases)));
            Reload(content, campaign, 17, "zero-objective-after.sav");
        }
    }

    private static CampaignSnapshot LandingInitial(RuntimeContent content)
    {
        var initial = Initial(content) with { Time = new CampaignTime(1, 1, 1, 1999, 1, 0, 0) };
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { NextWave = 1, LiveUfos = 1 }],
                Ufos = [new(9, "UFO_SCOUT", 4, "OBJECTIVE_LAND", 0, Degrees(8), Degrees(2),
                    UfoStatus.Flying, WorldAltitudes.High)
                {
                    MissionWaveNumber = 0, Shield = 0,
                    Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))),
                }],
            },
        };
    }

    private static CampaignSnapshot Initial(RuntimeContent content)
    {
        var snapshot = CreateSnapshot(content, "Base objective") with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        return snapshot with
        {
            World = snapshot.World with { Missions = [new(4, "FINAL_BASE", "OBJECTIVE_REGION", "RACE_B", 0, 0, 0, 0, -1)] },
        };
    }

    private static RuntimeContent LoadWorld(bool cache, string kind = "EXPLICIT", int chance = 0,
        bool allow = true, bool ufo = false, bool chain = false, string marker = "OBJECTIVE_BASE", int zone = 1, bool landing = false,
        bool objectiveFlag = false, bool landingFlag = true, bool hunting = false, int huntTimer = 90, bool regional = false, string huntingMission = "MISSION_SCOUT", string extra = "", int huntingChance = 100, string extraManufacture = "")
    {
        var table = kind switch
        {
            "single" => "{TEXTURE: 0}",
            "weighted" => "{TEXTURE: 2, Z_UNUSED: 3}",
            "zero" => "{TEXTURE: 0, Z_UNUSED: 0}",
            _ => "{}",
        };
        var explicitType = hunting ? "HUNT_BASE" : marker == "OBJECTIVE_BASE" ? "EXPLICIT" : "COLLISION_BASE";
        var site = kind == "EXPLICIT" ? explicitType : "\"\"";
        var waveType = landing ? "UFO_SCOUT" : ufo ? "UFO_SPAWN" : "UNDEFINED";
        var trajectory = landing ? "OBJECTIVE_LAND" : "OBJECTIVE_AIR";
        var first = chain ? "- {count: 2, trajectory: TRAJ_PATROL, timer: 0}" : "";
        var rules = $$$"""
            allowAlienBasesOnWrongTextures: {{{allow.ToString().ToLowerInvariant()}}}
            regions:
              - type: OBJECTIVE_REGION
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[10, 10, 8, 8]]
                  - {{{(regional ? "[[7, 9, 1, 3, 110]]" : "[[8, 8, 2, 2, 110], [12, 12, -2, -2, 110]]")}}}
            globe:
              textures:
                - {id: 110, deployments: {{{table}}}}
            alienDeployments:
              - type: {{{explicitType}}}
                baseDetectionRange: {{{(hunting ? 10000 : 0)}}}
                baseDetectionChance: {{{huntingChance}}}
                huntMissionMaxFrequency: {{{huntTimer}}}
                huntMissionWeights: {{{(hunting ? "{0: {" + huntingMission + ": 1}}" : "{}")}}}
              - type: COUNTER_SITE
                markerName: COUNTER_SITE
                counterDespawn: OTHER_COUNTER
                counterFailure: OTHER_COUNTER
                counterAll: OTHER_COUNTER
                decreaseCounterDespawn: OTHER_COUNTER
                decreaseCounterFailure: OTHER_COUNTER
                decreaseCounterAll: OTHER_COUNTER
              - {type: {{{explicitType}}}, markerName: {{{marker}}}, fakeUnderwaterSpawnChance: {{{chance}}}}
              - {type: TEXTURE, fakeUnderwaterSpawnChance: {{{chance}}}}
              - {type: Z_UNUSED, fakeUnderwaterSpawnChance: {{{chance}}}}
            ufoTrajectories:
              - id: OBJECTIVE_AIR
                waypoints: [[0, 3, 100], [1, 2, 80], [0, 3, 50]]
              - id: OBJECTIVE_LAND
                groundTimer: 2
                waypoints: [[0, 3, 100], [1, 0, 80], [0, 1, 50]]
            facilities:
              - type: BASE_MARKER_WORKSHOP
                mapName: LOGISTICS
                size: 1
                workshops: 2
            manufacture:
              - name: BASE_MARKER_CRAFT
                category: STR_CRAFT
                time: 1
                producedItems: {SHIP: 1}
              {{{extraManufacture}}}
            alienMissions:
              - type: NEW_BASE_NO_OBJECT
                objective: 0
                waves: [{count: 3, trajectory: TRAJ_PATROL, timer: 0}, {count: 1, trajectory: TRAJ_PATROL, timer: 60}]
              - type: NEW_BASE_SUPPLY
                objective: 5
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_PATROL, timer: 0, objective: true}]
              - type: FINAL_BASE
                objective: 2
                siteType: {{{site}}}
                spawnZone: {{{zone}}}
                points: 13
                waves:
                  {{{first}}}
                  - {ufo: {{{waveType}}}, count: 1, trajectory: {{{trajectory}}}, timer: 0, objective: {{{objectiveFlag.ToString().ToLowerInvariant()}}}, objectiveOnTheLandingSite: {{{(landing && landingFlag).ToString().ToLowerInvariant()}}}}
              - type: FINAL_BASE_NO_OBJECT
                objective: 2
                siteType: EXPLICIT
                spawnZone: 1
                points: 13
                waves: [{count: 1, trajectory: OBJECTIVE_AIR, timer: 0}]
            """;
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-objective-base.rul"], cache, rules + "\n" + extra);
    }

}
