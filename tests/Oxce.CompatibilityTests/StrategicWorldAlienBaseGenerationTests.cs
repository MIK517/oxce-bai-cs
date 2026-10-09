using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
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
    public void GenerationAndRandomRangesMatchExtractedReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var content = Load(cache);
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var name = row[0].GetString()!;
            var snapshot = Snapshot(content, name).WithNextId("ALIEN_MISSIONS", 1);
            if (name == "exhausted") snapshot = snapshot with
            {
                World = snapshot.World with { AlienBases = [snapshot.World.AlienBases[0] with { GenMissionCount = 3 }] },
            };
            if (name == "target")
            {
                var owner = snapshot.Bases[0];
                snapshot = snapshot with
                {
                    Bases = [owner, owner with { Id = 2, Crafts = [], Soldiers = [] },
                        owner with { Id = 3, Longitude = WorldGeometry.Nautical(45 * 60), Crafts = [], Soldiers = [] }],
                };
            }
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var source = Assert.Single(after.World.AlienBases);
            object? created = null;
            if (after.World.Missions.Count != 0)
            {
                var mission = Assert.Single(after.World.Missions);
                Assert.Equal("GEN_" + name, mission.RuleId);
                Assert.Equal((0, 0, 0), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos));
                Assert.False(mission.Interrupted);
                var link = Assert.IsType<WorldTargetReference>(mission.AlienBase);
                Assert.Equal((WorldTargetKind.AlienBase, "STR_ALIEN_BASE", source.Id, source.Position),
                    (link.Kind, link.TypeId, link.Id, link.Position));
                // Oracle captures creation; the midnight tick subsequently subtracts thirty minutes.
                created = new object[] { mission.Id, mission.RegionId, mission.Race, mission.MissionSiteZoneArea,
                    mission.SpawnCountdown + 30, 1 };
            }
            rows.Add([name, source.GenMissionCount, after.World.Missions.Count, created!, random.Calls]);
            Assert.Equal(snapshot.World.Strategy.MissionRuns, after.World.Strategy.MissionRuns);
            if (name != "futureRace") Reload(content, campaign, 40, "base-generated.sav");
            else
            {
                var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(after), content, 40,
                    "base-empty-race.sav").Campaign.Capture();
                Assert.True(Assert.Single(reloaded.World.Missions).Interrupted);
                Assert.Equal(1, reloaded.World.AlienBases[0].GenMissionCount);
            }
        }
        Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(oracle.RootElement.GetProperty("rows").GetRawText())),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissionLinksAndCountersSurviveRepeatedRewritesWithoutResurrectingDeletedState(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "local");
        var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var yaml = OxceSaveAdapter.EmitNewCampaign(after);
        var start = yaml.IndexOf("alienMissions:\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var line = yaml.IndexOf('\n', start + "alienMissions:\n".Length);
        yaml = yaml.Insert(line + 1, "    futureMission: preserved\n");
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40, "generated-sidecar.sav");
        for (var pass = 0; pass < 3; pass++)
        {
            yaml = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
            Assert.Contains("futureMission: preserved", yaml, StringComparison.Ordinal);
            loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40, "generated-sidecar.sav");
            Assert.Equivalent(after, loaded.Campaign.Capture(), strict: true);
        }
        var deleted = after with { World = after.World with { Missions = [] } };
        yaml = OxceSaveAdapter.EmitLoadedCampaign(deleted, loaded.Source);
        Assert.DoesNotContain("futureMission:", yaml, StringComparison.Ordinal);
        var reloaded = TestFixtures.LoadLogisticsSave(yaml, content, 40, "generated-deleted.sav").Campaign;
        Assert.Empty(reloaded.Capture().World.Missions);
        Assert.Equal(1, reloaded.Capture().World.AlienBases[0].GenMissionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeventyTwoHoursComposeGenerationCountdownCleanupAndPersistentLimits(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "short");
        var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
        AdvanceOne(campaign);
        Assert.Equal(30, Assert.Single(campaign.Capture().World.Missions).SpawnCountdown);
        campaign = Reload(content, campaign, 40, "base-short.sav");
        AdvanceUnblocked(campaign, 360); // Next half-hour consumes the no-object wave and removes the mission.
        Assert.Empty(campaign.Capture().World.Missions);
        campaign = Reload(content, campaign, 40, "base-short-cleanup.sav");
        AdvanceUnblocked(campaign, 51_840 - 360);
        var after = campaign.Capture();
        Assert.Equal(2, Assert.Single(after.World.AlienBases).GenMissionCount);
        Assert.Empty(after.World.Missions);
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 28, after.Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 40, "base-limit.sav");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void FrequencyFailureConsumesSelectionAndFrequencyOnly(ulong seed)
    {
        var content = Load();
        var snapshot = Snapshot(content, "rare") with { RandomState = seed };
        var expected = new SplitMix64RandomSource(seed);
        _ = expected.NextInclusive(1, 1);
        var generates = expected.NextInclusive(0, 99) < 50;
        if (generates)
        {
            _ = expected.NextInclusive(0, 99);
            _ = WorldTrajectory.SpawnCountdown(9000, expected);
        }
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
        AdvanceOne(campaign);
        Assert.Equal(expected.State, campaign.Capture().RandomState);
        Assert.Equal(generates ? 1 : 0, campaign.Capture().World.Missions.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedSupplyWavesCompleteWithoutObjectsOrSpawnTowardRegionalDestinations(bool cache)
    {
        var content = Load(cache);
        var campaign = CampaignState.Restore(Snapshot(content, "local"), content, new MinimumChoices());
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var due = after with
        {
            Time = new CampaignTime(1, 1, 2, 1999, 0, 29, 55),
            World = after.World with { Missions = [after.World.Missions[0] with { SpawnCountdown = 30 }] },
        };
        var completed = CampaignState.Restore(due, content, new MinimumChoices());
        AdvanceOne(completed);
        Assert.Empty(completed.Capture().World.Missions);
        Assert.Empty(completed.Capture().World.Ufos);
        Assert.Equal(after.World.AlienBases, completed.Capture().World.AlienBases);
        Reload(content, completed, 40, "completed-generated-supply.sav");
        var objective = due with
        {
            World = due.World with
            { Missions = [due.World.Missions[0] with { RuleId = "MISSION_SUPPLY" }] }
        };
        var spawned = CampaignState.Restore(objective with { RandomState = 40 }, content, new SplitMix64RandomSource(40));
        AdvanceOne(spawned);
        var ufo = Assert.Single(spawned.Capture().World.Ufos);
        var mission = Assert.Single(spawned.Capture().World.Missions);
        Assert.Equal(1, mission.LiveUfos);
        Assert.Equal(1, mission.NextWave);
        Assert.Equal(after.World.Missions[0].AlienBase, mission.AlienBase);
        Assert.Equal("UFO_SCOUT", ufo.RuleId);
        Assert.Equal(mission.Id, ufo.MissionId);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        Assert.Contains(ufo.Destination!.Position, new[]
        {
            new WorldPosition(Degrees(8), Degrees(2)),
            new WorldPosition(Degrees(12), Degrees(-2)),
        });
        Reload(content, spawned, 40, "spawned-generated-supply.sav");
    }

    [Theory]
    [InlineData("GEN_FUTURE_REGION", "Alien base mission region timeline has no entry for the current month.")]
    [InlineData("GEN_INVALID_ZONE", "Alien base site mission has an invalid spawn zone.")]
    [InlineData("GEN_WEIGHT_OVERFLOW", "Alien base mission weights exceed the supported range.")]
    [InlineData("GEN_INVALID_FALLBACK", "Alien base mission references an undefined fallback region.")]
    public void InvalidOrUnavailableCreationBlocksBeforeDailyScoringAndRandomDraws(string mission, string reason)
    {
        var content = Load();
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, mission), content, new MinimumChoices()), reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediatePatrolGenerationSupportsBothRoundedCountdownOutcomes(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = Load(cache);
        var bornSeen = false;
        var delayedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, "GEN_IMMEDIATE_UFO") with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            var born = after.World.Ufos.Count != 0;
            bornSeen |= born;
            delayedSeen |= !born;
            Assert.Equal(born ? 1 : 0, mission.NextWave);
            Assert.Equal(born ? 1 : 0, mission.LiveUfos);
            Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
            Assert.Equal(born ? 3 : 0, after.Regions[0].ActivityAlien[^1]);
            if (born)
            {
                var ufo = Assert.Single(after.World.Ufos);
                Assert.Equal("UFO_SCOUT", ufo.RuleId);
                Assert.Equal(mission.Id, ufo.MissionId);
                Assert.Equal(0, ufo.MissionWaveNumber);
                Assert.Equal(UfoStatus.Flying, ufo.Status);
            }
            campaign = Reload(content, campaign, seed, "generated-immediate-patrol.sav");
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 2);
            AdvanceUnblocked(campaign, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.True(bornSeen);
        Assert.True(delayedSeen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerationReservesAllPossibleIdsBeforeMutatingAnyBase(bool exhausted)
    {
        var content = Load();
        var snapshot = Snapshot(content, "local").WithNextId("ALIEN_MISSIONS", exhausted ? int.MaxValue - 1 : 1);
        var source = snapshot.World.AlienBases[0];
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                AlienBases = [source, new(3, source.DeploymentId, source.Race, source.Longitude, source.Latitude)],
                Missions = exhausted ? [] : [new(2, "GEN_local", "REGION", "RACE_A", 0, 0, 9000, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new MinimumChoices()),
            "Alien mission IDs or population exceed the supported limit.");
    }

    [Fact]
    public void MissingContainingRegionFailsOnlyWhenItsSelectionIsReachable()
    {
        var content = Load();
        var snapshot = Snapshot(content, "local");
        snapshot = snapshot with
        {
            World = snapshot.World with { AlienBases = [snapshot.World.AlienBases[0] with { Longitude = Math.PI }] },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new MinimumChoices()),
            "Alien base mission cannot locate its source base region.");
        snapshot = snapshot with
        {
            World = snapshot.World with { AlienBases = [snapshot.World.AlienBases[0] with { DeploymentId = "GEN_BASE_target" }] },
        };
        AdvanceOne(CampaignState.Restore(snapshot, content, new MinimumChoices()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateGeneratedNoObjectWavesCompleteInTheirCreationTick(bool cache)
    {
        var content = Load(cache);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "immediateRows"))
        {
            var name = row[0].GetString()!;
            if (name is not ("immediate" or "immediateZero")) continue;
            var snapshot = Snapshot(content, name);
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Equal(row[1].GetInt32(), Assert.Single(after.World.AlienBases).GenMissionCount);
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.Ufos);
            Assert.Equal(row[3][0].GetInt32() + 1, after.NextIds["ALIEN_MISSIONS"]);
            Assert.Equal((ulong)row[4].GetArrayLength(), after.RandomState);
            Reload(content, campaign, 40, "immediate-generated-no-object.sav");
        }
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, "immediateLimit"), content, new MinimumChoices()),
            "Immediate mission waves exceed the supported work limit.");
    }

    private static RuntimeContent Load(bool cache = false)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-generation.rul"], cache);
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, string name) =>
        CreateSnapshot(content, "Alien-base generation") with
        {
            RandomState = 0,
            Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
            World = new() { AlienBases = [new(2, "GEN_BASE_" + name, "RACE_A", 0.2, 0.1)] },
        };

    private sealed class MinimumChoices : IStatefulRandomSource
    {
        public List<int[]> Calls { get; } = [];
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.True(minimum <= maximum);
            Calls.Add([minimum, maximum]);
            State++;
            return minimum;
        }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() => throw new InvalidOperationException("No floating draw is expected during generation.");
    }
}
