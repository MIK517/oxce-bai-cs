using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void DailyGeneratedAirborneHuntersWithNoPossibleCraftKeepTheirNativeWaypoint(int percentage)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var constructor = TestFixtures.Rows(oracle.RootElement, "constructorRows").Where(row => row[0].GetInt32() == percentage).ToArray();
        Assert.NotEmpty(constructor);
        var content = GeneratedWaveContent(0, hunterPercentage: percentage);
        HashSet<bool> flags = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = GeneratedWaveSnapshot(content, seed);
            var owner = initial.Bases[0];
            initial = initial with { Bases = [owner with { Crafts = [], Soldiers = [] }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "idle-generated-hunter-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos);
            flags.Add(born.HunterKiller);
            Assert.Contains(constructor, row => row[4].GetBoolean() == born.HunterKiller);
            Assert.Equal(UfoStatus.Flying, born.Status);
            Assert.False(born.Hunting);
            Assert.False(born.Escorting);
            Assert.Null(born.OriginalWaypoint);
            Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
            Assert.Equal(2200, born.Speed);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 3, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
            campaign = Reload(content, campaign, seed, "idle-generated-hunter-after.sav");
            AdvanceUnblocked(campaign, 12);
            Reload(content, campaign, seed, "idle-generated-hunter-following.sav");
        }
        Assert.True(flags.SetEquals(percentage == 100 ? [true] : [false, true]));
        var exhausted = GeneratedWaveSnapshot(content, 19);
        exhausted = exhausted with { Bases = [exhausted.Bases[0] with { Crafts = [], Soldiers = [] }] };
        AssertTimeBlocked(CampaignState.Restore(exhausted.WithNextId("STR_UFO_UNIQUE", int.MaxValue), content,
            new SplitMix64RandomSource(19)), "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DailyGeneratedHuntersSelectNativeCraftAndPersistTheirPursuit(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = GeneratedHunterContent(mode);
        var initial = GeneratedHunterSnapshot(content);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            81, "generated-pursuit-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var born = Assert.Single(after.World.Ufos);
        var selection = TestFixtures.Rows(oracle.RootElement, "selectionRows").Single(row =>
            row[0].GetInt32() == born.HuntMode && row[1].GetInt32() == 0);
        var transition = TestFixtures.Rows(oracle.RootElement, "retargetRows").Single(row => row[0].GetInt32() == 150);
        Assert.True(born.HunterKiller);
        Assert.True(born.Hunting);
        Assert.True(born.Detected);
        Assert.Equal(selection[2].GetInt32(), born.Destination!.Id);
        Assert.Equal(transition[1].GetInt32(), born.Speed);
        Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
        Assert.Equal(born.UniqueId, Assert.Single(result.Events.OfType<UfoStartedHunting>()).UniqueId);
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 3, after.Regions[0].ActivityAlien[^1]);
        Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
        campaign = Reload(content, campaign, 81, "generated-pursuit-after.sav");
        AdvanceUnblocked(campaign, 12);
        Reload(content, campaign, 81, "generated-pursuit-following.sav");
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue), content,
            new SplitMix64RandomSource(81)), "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with { Crafts = [owner.Crafts[0] with
        { Logistics = owner.Crafts[0].Logistics! with { Longitude = Degrees(10), Latitude = Degrees(8) } }] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Fact]
    public void GeneratedHunterProofsCannotLeakBetweenSourcesWithTheSameCandidateId()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = GeneratedHunterContent(0);
        var initial = GeneratedHunterSnapshot(content);
        var target = initial.Bases[0].Crafts[0].Logistics!;
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases =
            [new(2, "GENERATED_HUNTER_BASE", "RACE_A", 0.45, 0),
             new(3, "GENERATED_HUNTER_BASE", "RACE_A", target.Longitude, target.Latitude)]
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HuntingGeneratedHuntersJoinLaterRetargetWithoutEarlierHalfHourActivity(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var minute in new[] { 9, 29 })
        {
            var content = GeneratedHunterContent(mode, hunting: true);
            var initial = GeneratedHunterSnapshot(content, hunting: true) with { Time = new(1, 1, 1, 1999, 0, minute, 55) };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "hunting-generated-target-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos);
            var selection = TestFixtures.Rows(oracle.RootElement, "selectionRows").Single(row =>
                row[0].GetInt32() == born.HuntMode && row[1].GetInt32() == 0);
            Assert.True(born.Hunting);
            Assert.True(born.Detected);
            Assert.True(born.Id > 0);
            Assert.Equal(selection[2].GetInt32(), born.Destination!.Id);
            Assert.Single(result.Events.OfType<UfoStartedHunting>());
            Assert.Equal(initial.Regions[0].ActivityAlien, after.Regions[0].ActivityAlien);
            Assert.Equal(0, after.World.AlienBases[0].MinutesSinceLastHuntMissionGeneration);
            Assert.Equal(0, after.World.AlienBases[0].GenMissionCount);
            campaign = Reload(content, campaign, 81, "hunting-generated-target-after.sav");
            AdvanceUnblocked(campaign, 12);
            Reload(content, campaign, 81, "hunting-generated-target-following.sav");
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue), content,
                new SplitMix64RandomSource(81)), "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        }
    }

    [Fact]
    public void DailyAndHuntingHunterBirthsReserveBothHiddenContactMarkers()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = GeneratedHunterContent(2, hunting: true, combined: true);
        var initial = GeneratedHunterSnapshot(content, hunting: true) with { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue - 1), content,
            new SplitMix64RandomSource(81)), "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        var campaign = CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue - 2), content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(2, after.World.Ufos.Count);
        Assert.Equal(2, result.Events.OfType<UfoStartedHunting>().Count());
        Assert.Equal(new[] { int.MaxValue - 2, int.MaxValue - 1 }, after.World.Ufos.Select(ufo => ufo.Id));
        Assert.Equal(2, after.World.Missions.Count);
        Assert.All(after.World.Ufos, ufo => Assert.True(ufo.Hunting));
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 3, after.Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 81, "combined-hunter-markers.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedHunterRaceStatsOverflowRefusesBeforeEitherCreationCaller(bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = GeneratedHunterContent(0, hunting, radarBonus: int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(GeneratedHunterSnapshot(content, hunting), content, new SplitMix64RandomSource(81)),
            hunting ? "Alien base hunting mission fields exceed the supported range." :
                "Immediate generated mission fields exceed the supported range.");
    }

    [Fact]
    public void GeneratedIdleHunterValidatesConsumedMaximumSpeedBeforeBirth()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = GeneratedHunterContent(0, speedMaximum: -1, spawnPercentage: 0);
        var initial = GeneratedHunterSnapshot(content);
        initial = initial with { Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting stats exceed the supported range.");
    }

    private static RuntimeContent GeneratedHunterContent(int mode, bool hunting = false, bool combined = false,
        int radarBonus = 0, int speedMaximum = 2200, int spawnPercentage = 100) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            crafts:
              - {type: GENERATED_SLOW, speedMax: 2000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
              - {type: GENERATED_FAST, speedMax: 6000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
            ufos:
              - type: GENERATED_HUNTER
                speedMax: {{{speedMaximum}}}
                damageMax: 50
                radarRange: 300
                huntSpeed: 150
                hunterKillerPercentage: 100
                huntMode: {{{mode}}}
                noAlert: true
                missionScore: 3
                raceBonus:
                  RACE_A: {radarRange: {{{radarBonus}}}}
            ufoTrajectories:
              - id: GENERATED_HUNTER_PATH
                waypoints: [[4, 3, {{{spawnPercentage}}}], [3, 2, 80], [0, 3, 50]]
            alienDeployments:
              - type: GENERATED_HUNTER_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_HUNTER_MISSION: 1}
                genMissionFreq: {{{(hunting && !combined ? 0 : 100)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(hunting ? 500 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionWeights: {0: {GENERATED_HUNTER_MISSION: 1}}
            alienMissions:
              - type: GENERATED_HUNTER_MISSION
                objective: 0
                operationType: 1
                waves: [{ufo: GENERATED_HUNTER, count: 1, trajectory: GENERATED_HUNTER_PATH, timer: 0}]
            """);

    private static CampaignSnapshot GeneratedHunterSnapshot(RuntimeContent content, bool hunting = false)
    {
        var initial = CreateSnapshot(content, "Generated hunting") with
        { Time = hunting ? new(1, 1, 1, 1999, 0, 9, 55) : new(1, 1, 1, 1999, 23, 59, 55), RandomState = 81 };
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        CraftSnapshot Craft(int id, string rule) => template with
        {
            Id = id,
            RuleId = rule,
            Logistics = template.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 1000,
                Damage = 0,
                Weapons = [],
                Speed = 0,
                SpeedRadian = 0,
                Longitude = Degrees(10) + WorldGeometry.Nautical(100),
                Latitude = Degrees(8),
                Destination = null,
                LowFuel = false,
                MissionComplete = false,
                IsAutoPatrolling = false,
            },
        };
        return initial with
        {
            Bases = [owner with { Crafts = [Craft(1, "GENERATED_SLOW"), Craft(2, "GENERATED_FAST")], Soldiers = [] }],
            World = new()
            {
                AlienBases = [new(2, "GENERATED_HUNTER_BASE", "RACE_A", Degrees(10), Degrees(8))
                { MinutesSinceLastHuntMissionGeneration = hunting ? 50 : 0 }]
            },
        };
    }
}
