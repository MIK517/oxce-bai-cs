using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using System.Text;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using System.Text.Json;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicOperationBaseSpawnTests
{
    [Fact]
    public void OperationBaseSearchMatchesPinnedBoundedFallbackAndGeometryRows()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-operation-base-spawn");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var successAt = row[0].GetInt32();
            var geometry = row[1].GetInt32();
            var fakeChance = row[2].GetInt32();
            var fake = row[3].GetInt32() != 0;
            var allow = row[4].GetInt32() != 0;
            var x = geometry == 0 ? 0.2 : 2.0;
            var region = new RuntimeRegionRule(0, geometry == 2 ? [] : [new(0, 1, -0.5, 0.5)],
                [new([new(x, x, 0.1, 0.1, 0, "")])], new Dictionary<string, ulong>(), 0, "", null, [], []);
            var random = new SplitMix64RandomSource(17);
            var expected = new SplitMix64RandomSource(17);
            var timer = WorldTrajectory.SpawnCountdown(9000, random);
            WorldTrajectory.SpawnCountdown(9000, expected);
            var landCalls = 0;
            var selected = WorldOperationBaseSpawn.Select(region, 0, fakeChance, allow, random,
                _ => ++landCalls >= successAt && successAt != 0, _ => fake);
            var attempts = row[8].GetInt32() / 2;
            expected.NextInclusive(0, 99);
            for (var index = 0; index < attempts; index++)
            {
                expected.NextInclusive(0, 0);
                expected.NextUnit();
                expected.NextUnit();
            }
            Assert.Equal(expected.State, random.State);
            Assert.Equal(4500, row[10].GetInt32()); // minimum captured start timer
            Assert.InRange(timer, 4500, 13500);
            if (selected is { } position) Assert.Equal(new WorldPosition(x, 0.1), position);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                successAt, geometry, fakeChance, fake ? 1 : 0, allow ? 1 : 0,
                selected is null ? 0 : 1, selected is null ? 1 : 0,
                2 + attempts, 2 * attempts, landCalls, row[10].GetInt32(),
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Fact]
    public void CreationModesReuseOrCreateBasesInReferenceOrderAndPersistLinks()
    {
        foreach (var operation in new[] { 2, 3, 5, 6 })
            foreach (var existing in new[] { false, true })
            {
                var content = LoadCreation(operation);
                var initial = CreateSnapshot(content, "Mission operation base creation").WithNextId("STR_ALIEN_BASE", 11) with
                {
                    RandomState = 17,
                    Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
                };
                if (existing) initial = initial with
                {
                    World = initial.World with { AlienBases = [new(10, "OPERATION_BASE", "RACE_A", Degrees(8), Degrees(2))] },
                };
                var expected = new SplitMix64RandomSource(17);
                expected.NextInclusive(0, 99);
                expected.NextInclusive(0, 99);
                expected.NextInclusive(1, 1);
                expected.NextInclusive(1, 1);
                expected.NextInclusive(1, 1);
                var creates = !existing || operation == 2;
                var expectedPosition = new WorldPosition(Degrees(8), Degrees(2));
                if (creates)
                {
                    expected.NextInclusive(0, 99);
                    var area = expected.NextInclusive(0, 1);
                    expected.NextUnit();
                    expected.NextUnit();
                    var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("OPERATION_REGION")].Value;
                    var point = region.MissionZones[1].Areas[area];
                    expectedPosition = new(point.LongitudeMinimum, point.LatitudeMinimum);
                }
                else expected.NextInclusive(0, 0);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
                Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
                var after = campaign.Capture();
                var mission = Assert.Single(after.World.Missions);
                Assert.False(mission.Interrupted);
                var id = creates ? 11 : 10;
                var alienBase = after.World.AlienBases.Single(candidate => candidate.Id == id);
                Assert.Equal(expectedPosition, alienBase.Position);
                Assert.Equal((WorldTargetKind.AlienBase, "STR_ALIEN_BASE", id, expectedPosition),
                    (mission.AlienBase!.Kind, mission.AlienBase.TypeId, mission.AlienBase.Id, mission.AlienBase.Position));
                Assert.Equal((0, 0, creates ? 90 : 0),
                    (alienBase.StartMonth, alienBase.GenMissionCount, alienBase.MinutesSinceLastHuntMissionGeneration));
                Assert.False(alienBase.Discovered);
                Assert.False(alienBase.InBattlescape);
                Assert.Empty(alienBase.PactCountryId);
                Assert.Equal("RACE_A", alienBase.Race);
                Assert.Equal(expected.State, after.RandomState);
                Assert.Equal(creates ? 13 : 0, after.Regions.Sum(state => (long)state.ActivityAlien[^1]) -
                    initial.Regions.Sum(state => (long)state.ActivityAlien[^1]));
                campaign = Reload(content, campaign, 17, "operation-base-created.sav");
                AdvanceUnblocked(campaign, 1);
                Assert.Empty(campaign.Capture().World.Missions);
                Assert.Equal(after.World.AlienBases.Count, campaign.Capture().World.AlienBases.Count);
                Reload(content, campaign, 17, "operation-base-mission-cleaned.sav");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidTextureSearchSuppressesOrForcesCreationAfterOneHundredAttempts(bool allow)
    {
        var content = LoadCreation(2, invalidTexture: true, allow: allow);
        var initial = CreateSnapshot(content, "Operation base texture fallback") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(!allow, mission.Interrupted);
        Assert.Equal(allow ? 1 : 0, after.World.AlienBases.Count);
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99);
        expected.NextInclusive(0, 99);
        expected.NextInclusive(1, 1);
        expected.NextInclusive(1, 1);
        expected.NextInclusive(1, 1);
        expected.NextInclusive(0, 99);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            expected.NextInclusive(0, 0);
            expected.NextUnit();
            expected.NextUnit();
        }
        Assert.Equal(expected.State, after.RandomState);
        if (allow) Assert.Equal(new WorldPosition(Degrees(45), Degrees(2)), Assert.Single(after.World.AlienBases).Position);
        else Assert.Null(mission.AlienBase);
        Reload(content, campaign, 17, "operation-base-fallback.sav");
    }

    [Fact]
    public void ComposedCreationReservesSharedMarkerIdsAndCombinedScoreBeforeMutation()
    {
        var content = LoadCreation(2, additional: true);
        var initial = CreateSnapshot(content, "Composed operation base creation") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        Assert.Equal([1, 2], after.World.AlienBases.Select(candidate => candidate.Id));
        Assert.Equal([1, 2], after.World.Missions.Select(mission => mission.AlienBase!.Id));
        Assert.Equal(26, after.Regions.Sum(state => (long)state.ActivityAlien[^1]) -
            initial.Regions.Sum(state => (long)state.ActivityAlien[^1]));
        Reload(content, campaign, 17, "composed-operation-bases.sav");
        initial = initial with
        {
            Regions = initial.Regions.Select(region =>
                region with { ActivityAlien = [int.MaxValue - 13] }).ToArray()
        };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base activity exceeds the supported range.");
    }

    [Fact]
    public void CreationIdsAndMarkerCollisionsAreCheckedBeforeRng()
    {
        var content = LoadCreation(2);
        var initial = CreateSnapshot(content, "Operation base identity limits") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", int.MaxValue),
            content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base IDs are exhausted or collide.");
        initial = initial with { World = initial.World with { AlienBases = [new(1, "OPERATION_BASE", "RACE_A", 0.2, 0.1)] } };
        campaign = CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", 1), content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base IDs are exhausted or collide.");
    }

    [Fact]
    public void InfiltrationOperationCreatesLinkedBaseWithoutActivityAward()
    {
        var content = LoadCreation(2, objective: 1);
        var initial = CreateSnapshot(content, "Infiltration operation base") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Single(campaign.Capture().World.AlienBases);
        Assert.Equivalent(initial.Regions, campaign.Capture().Regions, strict: true);
        Assert.Equivalent(initial.Countries, campaign.Capture().Countries, strict: true);
        Reload(content, campaign, 17, "infiltration-operation-base.sav");
    }

    [Fact]
    public void EmptyAndMissingSpawnZonesFailBeforeMutationWhileExistingBaseReuseSkipsThem()
    {
        foreach (var zone in new[] { 0, 99 })
        {
            var content = LoadCreation(2, zone: zone);
            var initial = CreateSnapshot(content, "Invalid operation base zone") with { RandomState = 17 };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            var before = campaign.Capture();
            Assert.Throws<InvalidDataException>(() => campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
        var reusable = LoadCreation(3, zone: 99);
        var saved = CreateSnapshot(reusable, "Unused invalid operation base zone") with { RandomState = 17 };
        saved = saved with { World = saved.World with { AlienBases = [new(10, "OPERATION_BASE", "RACE_A", 0.2, 0.1)] } };
        var reused = CampaignState.Restore(saved, reusable, new SplitMix64RandomSource(17));
        Assert.Empty(reused.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(10, Assert.Single(reused.Capture().World.Missions).AlienBase!.Id);
    }

    [Fact]
    public void CombinedLocationSearchWorkStopsTheWholeBatchBeforeRng()
    {
        var content = LoadCreation(2, extraScripts: 450);
        var initial = CreateSnapshot(content, "Operation base work bound") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base creation exceeds the supported work limit.");
    }

    [Fact]
    public void TaggedAdhocCreationUsesTheSameOperationAndLeavesDueEventPayloadGuarded()
    {
        var content = LoadCreation(2, adhoc: true);
        var initial = CreateSnapshot(content, "Tagged operation base creation") with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("OPERATION_EVENT", 0, 0)).Events);
        var after = campaign.Capture();
        Assert.Equal(Assert.Single(after.World.AlienBases).Id, Assert.Single(after.World.Missions).AlienBase!.Id);
        Assert.Empty(after.World.Events);
        campaign = Reload(content, campaign, 17, "adhoc-operation-base.sav");
        var scheduled = campaign.Capture();
        scheduled = scheduled with { World = scheduled.World with { Events = [new("OPERATION_EVENT", 0)] } };
        campaign = CampaignState.Restore(scheduled, content, new SplitMix64RandomSource(scheduled.RandomState));
        AssertTimeBlocked(campaign, "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
    }

    [Fact]
    public void DeletedBaseSidecarCannotAttachToACreatedBaseReusingItsMarkerId()
    {
        var content = LoadCreation(2);
        var initial = CreateSnapshot(content, "Deleted operation base sidecar") with { RandomState = 17 };
        initial = initial with { World = initial.World with { AlienBases = [new(1, "OPERATION_BASE", "RACE_A", 0.2, 0.1)] } };
        var yaml = Oxce.Savegames.Oxce.OxceSaveAdapter.EmitNewCampaign(initial);
        var start = yaml.IndexOf("alienBases:\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var line = yaml.IndexOf('\n', start + "alienBases:\n".Length);
        yaml = yaml.Insert(line + 1, "    futureBase: obsolete\n");
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 17, "obsolete-operation-base.sav");
        var deleted = loaded.Campaign.Capture().WithNextId("STR_ALIEN_BASE", 1);
        deleted = deleted with { World = deleted.World with { AlienBases = [] } };
        var campaign = CampaignState.Restore(deleted, content, new SplitMix64RandomSource(deleted.RandomState));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var created = campaign.Capture();
        Assert.Equal(1, Assert.Single(created.World.AlienBases).Id);
        var written = Oxce.Savegames.Oxce.OxceSaveAdapter.EmitLoadedCampaign(created, loaded.Source);
        Assert.DoesNotContain("futureBase", written, StringComparison.Ordinal);
        var again = TestFixtures.LoadLogisticsSave(written, content, 17, "replacement-operation-base.sav");
        Assert.Equivalent(created, again.Campaign.Capture(), strict: true);
        Assert.Equal(1UL, created.World.AlienBaseBirthSequence);
        var firstKey = Assert.Single(created.World.AlienBases).PreservationKey;
        var empty = again.Campaign.Capture();
        empty = empty with { World = empty.World with { AlienBases = [], Missions = [] } };
        var emptyYaml = Oxce.Savegames.Oxce.OxceSaveAdapter.EmitLoadedCampaign(empty, again.Source);
        var emptied = TestFixtures.LoadLogisticsSave(emptyYaml, content, 17, "operation-base-sequence-without-actors.sav");
        Assert.Equal(1UL, emptied.Campaign.Capture().World.AlienBaseBirthSequence);
        var replacement = emptied.Campaign.Capture().WithNextId("STR_ALIEN_BASE", 1);
        var next = CampaignState.Restore(replacement, content, new SplitMix64RandomSource(replacement.RandomState));
        Assert.Empty(next.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal(2UL, next.Capture().World.AlienBaseBirthSequence);
        Assert.NotEqual(firstKey, Assert.Single(next.Capture().World.AlienBases).PreservationKey);
        Reload(content, next, 17, "second-replacement-operation-base.sav");
    }

    [Fact]
    public void ExhaustedBirthSequenceBlocksBeforeRngAndInconsistentBirthIdentityIsRejected()
    {
        var content = LoadCreation(2);
        var initial = CreateSnapshot(content, "Operation base birth capacity") with { RandomState = 17 };
        initial = initial with { World = initial.World with { AlienBaseBirthSequence = ulong.MaxValue } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base birth sequence is exhausted.");
        initial = initial with
        {
            World = initial.World with
            {
                AlienBaseBirthSequence = 0,
                AlienBases = [new(1, "OPERATION_BASE", "RACE_A", 0.2, 0.1) { PreservationKey = "alien-base:generated:1" }],
            },
        };
        Assert.Throws<InvalidDataException>(() =>
            CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)));
    }

    [Theory]
    [InlineData("SHIP", true)]
    [InlineData("STR_ALIEN_TERROR", false)]
    public void CreationCannotReuseACraftMarkerIdentityOrTheLegacyTerrorNamespace(string marker, bool collision)
    {
        var content = LoadCreation(2, marker: marker);
        var initial = CreateSnapshot(content, "Operation base target identity") with { RandomState = 17 };
        if (collision)
        {
            var craft = initial.Bases.SelectMany(owner => owner.Crafts).First(candidate => candidate.RuleId == marker);
            initial = initial.WithNextId(marker, craft.Id);
        }
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, collision ? "Mission operation base IDs are exhausted or collide." :
            "Mission operation base uses an unsupported marker namespace.");
    }

    [Fact]
    public void IncomingCraftMarkerIdentityAlsoPreventsAmbiguousBaseCreation()
    {
        var content = LoadCreation(2, marker: "SHIP");
        var initial = CreateSnapshot(content, "Incoming craft operation base identity") with { RandomState = 17 };
        var owner = initial.Bases[0];
        var craft = owner.Crafts.First(candidate => candidate.RuleId == "SHIP") with { Id = 11, PreservationKey = "" };
        initial = initial.WithNextId("SHIP", 11) with
        {
            Bases = [owner with
            {
                Transfers = [new TransferSnapshot(77, 1, CampaignTransferKind.Craft, "SHIP", 1, Craft: craft)],
            }, .. initial.Bases.Skip(1)],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base IDs are exhausted or collide.");
    }

    [Fact]
    public void RegionalBaseCandidateGeometryIsIncludedInTheWholeBatchWorkBound()
    {
        var content = LoadCreation(3, regionAreaCopies: 1000);
        var initial = CreateSnapshot(content, "Regional operation candidate work").WithNextId("STR_ALIEN_BASE", 501) with
        {
            RandomState = 17,
        };
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases = Enumerable.Range(1, 500).Select(id =>
                    new AlienBaseSnapshot(id, "OPERATION_BASE", "RACE_A", 2, 0.1)).ToArray(),
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission script base targeting exceeds the supported work limit.");
    }

    [Fact]
    public void NegativeOperationBaseTimerIsRejectedBeforeCreatingUnreloadableState()
    {
        var content = LoadCreation(2, huntTimer: -1);
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Invalid operation birth timer"),
            content, new SplitMix64RandomSource(17));
        AssertCommandBlocked(campaign, "Mission operation base hunting timer cannot be negative.");
    }

    private static Oxce.Mods.Rulesets.Content.RuntimeContent LoadCreation(
        int operation, int zone = 1, int objective = 0, bool additional = false, bool invalidTexture = false, bool? allow = null, int extraScripts = 0, bool adhoc = false, string marker = "STR_ALIEN_BASE", int regionAreaCopies = 1, int huntTimer = 90)
    {
        TestFixtures.LoadVerifiedManifest("strategic-operation-base-spawn");
        var regionAreas = string.Join(", ", Enumerable.Repeat("[0, 90, -20, 20]", regionAreaCopies));
        var points = invalidTexture ? "[[45, 45, 2, 2]]" : "[[8, 8, 2, 2], [12, 12, -2, -2]]";
        var option = allow is { } configured ? "allowAlienBasesOnWrongTextures: " + (configured ? "true" : "false") : "";
        var second = string.Join("\n", Enumerable.Range(0, additional ? 1 : extraScripts).Select(index => $$$"""
              - type: CREATE_SECOND_OPERATION_{{{index}}}
                useTable: false
                startDelay: 30
                missionWeights: {0: {CREATE_OPERATION: 1}}
                regionWeights: {0: {OPERATION_REGION: 1}}
                raceWeights: {0: {RACE_A: 1}}
            """));
        var tagged = adhoc ? """
            events:
              - name: OPERATION_EVENT
                adhocMissionScriptTags: [operation]
            adhocScripts:
              - type: ADHOC_OPERATION
                adhocMissionScriptTags: [operation]
                useTable: false
                startDelay: 30
                missionWeights: {0: {CREATE_OPERATION: 1}}
                regionWeights: {0: {OPERATION_REGION: 1}}
                raceWeights: {0: {RACE_A: 1}}
            """ : "";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            {{{option}}}
            regions:
              - type: OPERATION_REGION
                areas: [{{{regionAreas}}}]
                missionZones:
                  - []
                  - {{{points}}}
            alienDeployments:
              - type: OPERATION_BASE
                alienBase: true
                markerName: {{{marker}}}
                huntMissionMaxFrequency: {{{huntTimer}}}
            alienMissions:
              - type: CREATE_OPERATION
                objective: {{{objective}}}
                points: 13
                operationType: {{{operation}}}
                operationSpawnZone: {{{zone}}}
                operationBaseType: OPERATION_BASE
                raceWeights: {0: {RACE_A: 1}}
                waves:
                  - {ufo: no_object, count: 1, trajectory: TRAJ_PATROL, timer: 3000}
            missionScripts:
              - type: CREATE_OPERATION
                useTable: false
                startDelay: 30
                missionWeights: {0: {CREATE_OPERATION: 1}}
                regionWeights: {0: {OPERATION_REGION: 1}}
                raceWeights: {0: {RACE_A: 1}}
            {{{second}}}
            {{{tagged}}}
            """);
        Assert.Equal(1 + (additional ? 1 : extraScripts), content.RuntimeRules.MissionScripts.Count);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("OPERATION_REGION")].Value;
        Assert.Equal(invalidTexture ? 1 : 2, region.MissionZones[1].Areas.Count);
        if (allow is { } value) Assert.Equal(value, content.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
        return content;
    }

    private static void AssertCommandBlocked(CampaignState campaign, string reason)
    {
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(
            Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equal(reason, blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void WrongTextureFallbackDefaultsTrueAndExplicitFalseSurvivesCache()
    {
        TestFixtures.LoadVerifiedManifest("strategic-operation-base-spawn");
        var defaults = StrategicReadinessTestContent.Load("strategic-world.rul");
        Assert.True(defaults.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
        var configured = StrategicReadinessTestContent.Load(["strategic-world.rul"], "allowAlienBasesOnWrongTextures: false\n");
        Assert.False(configured.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
    }
}
