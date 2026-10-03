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
    }

    private static CampaignSnapshot Initial(RuntimeContent content)
    {
        var snapshot = CreateSnapshot(content, "Base objective") with
        {
            RandomState = 17, Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        return snapshot with
        {
            World = snapshot.World with { Missions = [new(4, "FINAL_BASE", "OBJECTIVE_REGION", "RACE_B", 0, 0, 0, 0, -1)] },
        };
    }

    private static RuntimeContent LoadWorld(bool cache, string kind = "EXPLICIT", int chance = 0,
        bool allow = true, bool ufo = false, bool chain = false, string marker = "OBJECTIVE_BASE", int zone = 1)
    {
        var table = kind switch
        {
            "single" => "{TEXTURE: 0}",
            "weighted" => "{TEXTURE: 2, Z_UNUSED: 3}",
            "zero" => "{TEXTURE: 0, Z_UNUSED: 0}",
            _ => "{}",
        };
        var explicitType = marker == "OBJECTIVE_BASE" ? "EXPLICIT" : "COLLISION_BASE";
        var site = kind == "EXPLICIT" ? explicitType : "\"\"";
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
              - {type: {{{explicitType}}}, markerName: {{{marker}}}, fakeUnderwaterSpawnChance: {{{chance}}}}
              - {type: TEXTURE, fakeUnderwaterSpawnChance: {{{chance}}}}
              - {type: Z_UNUSED, fakeUnderwaterSpawnChance: {{{chance}}}}
            ufoTrajectories:
              - id: OBJECTIVE_AIR
                waypoints: [[0, 3, 100], [1, 2, 80], [0, 3, 50]]
            alienMissions:
              - type: FINAL_BASE
                objective: 2
                siteType: {{{site}}}
                spawnZone: {{{zone}}}
                points: 13
                waves:
                  {{{first}}}
                  - {ufo: {{{(ufo ? "UFO_SPAWN" : "UNDEFINED")}}}, count: 1, trajectory: OBJECTIVE_AIR, timer: 0}
            """;
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-objective-base.rul"], cache, rules);
    }

}
