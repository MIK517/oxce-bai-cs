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
        content = LoadWorld(false, marker: "\" \"");
        AssertTimeBlocked(CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17)),
            "Base objective uses an unsupported marker namespace.");
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
    [InlineData(false)]
    [InlineData(true)]
    public void NewFinalWaveBaseStopsBeforeUnreservedHuntingGeneration(bool cache)
    {
        var content = LoadWorld(cache, hunting: true);
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
            "New base objective hunting requires creation-tick mission forecasts.");
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BaseMarkerNamespaceWithHourlyCraftProductionStopsBeforeIdForecastChanges(bool cache, bool landing)
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
                Productions = [new("BASE_MARKER_CRAFT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
            World = initial.World with { AlienBases = [new(nextCraft + 1, "COLLISION_BASE", "RACE_A", 0.2, 0.1)] },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective markers shared with same-tick craft production require ID forecasts.");
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
        bool objectiveFlag = false, bool landingFlag = true, bool hunting = false, int huntTimer = 90)
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
                  - [[8, 8, 2, 2, 110], [12, 12, -2, -2, 110]]
            globe:
              textures:
                - {id: 110, deployments: {{{table}}}}
            alienDeployments:
              - type: {{{explicitType}}}
                baseDetectionRange: {{{(hunting ? 10000 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: {{{huntTimer}}}
                huntMissionWeights: {{{(hunting ? "{0: {MISSION_SCOUT: 1}}" : "{}")}}}
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
            alienMissions:
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
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-objective-base.rul"], cache, rules);
    }

}
