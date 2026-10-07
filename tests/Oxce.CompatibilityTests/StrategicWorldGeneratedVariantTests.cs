using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedFutureRaceTimelineUsesNativeEmptyRaceAndReloadRepair(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var race = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "futureRace")[3][2].GetString();
        Assert.Equal(string.Empty, race);
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedVariantContent(cache, "{2: {RACE_B: 1}}", hunting: hunting);
            var initial = VariantSnapshot(content, hunting);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(19));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(race, mission.Race);
            Assert.False(mission.Interrupted);
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(2200, ufo.Speed);
            var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(after), content, 19,
                "generated-empty-race.sav").Campaign;
            var repaired = Assert.Single(loaded.Capture().World.Missions);
            Assert.Equal("RACE_A", repaired.Race);
            Assert.True(repaired.Interrupted);
            Assert.Equivalent(after with { World = after.World with { Missions = [mission with { Race = "RACE_A", Interrupted = true }] } },
                loaded.Capture(), strict: true);
            AdvanceUnblocked(loaded, 1);
            Assert.Single(loaded.Capture().World.Ufos);
            Reload(content, loaded, 19, "generated-repaired-race.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateTargetBaseRegionsReserveOneBirthWithoutChangingNativeChoice(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = GeneratedVariantContent(cache, "{0: {RACE_A: 1}}", targetOdds: 100);
        var initial = VariantSnapshot(content, hunting: false);
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [.. Enumerable.Range(0, 64).Select(id => owner with
            { Id = id, Longitude = 0.2, Latitude = 0.1, Crafts = [], Soldiers = [], Transfers = [] })],
        };
        initial = initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue - 1);
        var random = new VariantChoices(19);
        var campaign = CampaignState.Restore(initial, content, random);
        AdvanceUnblocked(campaign, 1);
        Assert.Single(random.Calls, call => call == (0, 63));
        Assert.Equal(int.MaxValue - 1, Assert.Single(campaign.Capture().World.Ufos).UniqueId);
        Assert.Equal("REGION", Assert.Single(campaign.Capture().World.Missions).RegionId);
        Reload(content, campaign, 19, "duplicate-region-birth.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternativeRulesAndRacesReserveTheirLargestSingleBirth(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = GeneratedVariantContent(cache, "{0: {RACE_A: 1, RACE_B: 1}}", alternatives: true);
        var counts = new HashSet<int>();
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = (VariantSnapshot(content, hunting: false) with { RandomState = seed })
                .WithNextId("STR_UFO_UNIQUE", int.MaxValue - 2);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            var expected = mission.RuleId == "VARIANT_OTHER" ? 2 : 1;
            Assert.Equal(expected, after.World.Ufos.Count);
            Assert.Equal(expected, mission.LiveUfos);
            Assert.All(after.World.Ufos, ufo => Assert.Equal(mission.Id, ufo.MissionId));
            Assert.Equal(int.MaxValue - 2 + expected, after.NextIds["STR_UFO_UNIQUE"]);
            counts.Add(expected);
            Reload(content, campaign, seed, "alternative-wave-birth.sav");
        }
        Assert.True(counts.SetEquals([1, 2]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedMissionsReserveFollowingSavedHunterMissionLookupsBeforeMutation(bool cache, bool born)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = GeneratedVariantContent(cache, "{0: {RACE_A: 1}}");
        var initial = VariantSnapshot(content, hunting: false);
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with { Crafts = [], Soldiers = [] }],
            World = new()
            {
                AlienBases = [.. Enumerable.Range(2, 750).Select(id => new AlienBaseSnapshot(id, "VARIANT_BASE", "RACE_A", 0.2, 0.1))],
                Missions = [new(7, born ? "VARIANT_HUNTER_BIRTH" : "VARIANT_MISSION", "REGION", "RACE_A", born ? 0 : 1, 0, 0, born ? 0 : 1400, -1)],
                Ufos = born ? [] : [.. Enumerable.Range(1, 1400).Select(id => new UfoSnapshot(id, "UFO_SCOUT", 7, "TRAJ_SPAWN_FIXED",
                    0, 0, 0, UfoStatus.Flying, WorldAltitudes.High)
                {
                    HunterKiller = true,
                    Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
                })],
            },
        };
        initial = initial.WithNextId("STR_UFO_UNIQUE", born ? 1 : 1401).WithNextId("ALIEN_MISSIONS", 8);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
            "UFO retargeting with generated missions exceeds the supported work limit.");
    }

    private static RuntimeContent GeneratedVariantContent(bool cache, string races, int targetOdds = 0, bool hunting = false, bool alternatives = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            $$$"""
            alienDeployments:
              - type: VARIANT_BASE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {{{(alternatives ? "{VARIANT_MISSION: 1, VARIANT_OTHER: 1}" : "{VARIANT_MISSION: 1}")}}}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                genMissionRaceFromAlienBase: false
                baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionRaceFromAlienBase: false
                huntMissionWeights: {0: {VARIANT_MISSION: 1}}
            alienMissions:
              - type: VARIANT_MISSION
                objective: 0
                targetBaseOdds: {{{targetOdds}}}
                raceWeights: {{{races}}}
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]
              - type: VARIANT_OTHER
                objective: 0
                waves: [{ufo: UFO_SCOUT, count: 2, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]
              - type: VARIANT_HUNTER_BIRTH
                objective: 0
                waves: [{ufo: UFO_SCOUT, count: 1500, trajectory: TRAJ_SPAWN_FIXED, timer: 0, hunterKillerPercentage: 100}]
            """);

    private static CampaignSnapshot VariantSnapshot(RuntimeContent content, bool hunting) =>
        (hunting ? HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 29, 55)) : GeneratedWaveSnapshot(content, 19)) with
        {
            World = new()
            {
                AlienBases = [new(2, "VARIANT_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 50 }]
            },
        };

    private sealed class VariantChoices(ulong seed) : IStatefulRandomSource
    {
        private readonly SplitMix64RandomSource _inner = new(seed);
        public List<(int Minimum, int Maximum)> Calls { get; } = [];
        public ulong State => _inner.State;
        public void Restore(ulong state) => _inner.Restore(state);
        public int NextInclusive(int minimum, int maximum)
        {
            Calls.Add((minimum, maximum));
            return _inner.NextInclusive(minimum, maximum);
        }
        public int NextExclusive(int maximum)
        {
            Calls.Add((0, maximum - 1));
            return _inner.NextExclusive(maximum);
        }
        public double NextUnit() => _inner.NextUnit();
    }
}
