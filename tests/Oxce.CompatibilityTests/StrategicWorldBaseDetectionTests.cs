using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>GeoscapeState::DetectXCOMBase/time10Minutes and Base::getDetectionChance/load/save.</summary>
public sealed class StrategicWorldBaseDetectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LaterFlightAndGroundTimersCrossTheBoundaryAndPersistDiscovery(bool fromCache, bool landed)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var snapshot = CreateSnapshot(content, landed);
        var campaign = Restore(content, snapshot);
        Assert.True(campaign.Options.AggressiveRetaliation); // reference default
        AdvanceOne(campaign);

        var after = campaign.Capture();
        Assert.True(Assert.Single(after.Bases).RetaliationTarget);
        Assert.Equal(StateAfterRolls(2, 1), after.RandomState); // 10 < 15
        var ufo = Assert.Single(after.World.Ufos);
        if (landed) Assert.Equal(1795, ufo.SecondsRemaining);
        else Assert.NotEqual(snapshot.World.Ufos[0].Position, ufo.Position);
        var yaml = OxceSaveAdapter.EmitNewCampaign(after);
        Assert.Contains("retaliationTarget: true", yaml, StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 40, name: "base-detected.sav");
        Assert.Equivalent(after, loaded.Campaign.Capture(), strict: true);

        // Promoted ownership must replace an old true key, not preserve it opaquely.
        var cleared = after with { Bases = [after.Bases[0] with { RetaliationTarget = false }] };
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(cleared, loaded.Source);
        Assert.DoesNotContain("retaliationTarget:", rewritten, StringComparison.Ordinal);
        Assert.False(TestFixtures.LoadLogisticsSave(rewritten, content, seed: 41,
            name: "base-cleared.sav").Campaign.Capture().Bases[0].RetaliationTarget);
    }

    [Theory]
    [InlineData("early")]
    [InlineData("zone-five")]
    [InlineData("no-sight")]
    [InlineData("distant")]
    [InlineData("non-aggressive")]
    public void ExcludedScannersDoNotConsumeRandomness(string condition)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var ufo = snapshot.World.Ufos[0];
        ufo = condition switch
        {
            "early" => ufo with { TrajectoryPoint = 1 },
            "zone-five" => ufo with { TrajectoryId = "TRAJ_BASE_SCAN_EXCLUDED" },
            "no-sight" => ufo with { RuleId = "UFO_SCAN_TEST" },
            "distant" => ufo with { Longitude = 0.8 },
            _ => ufo,
        };
        var campaign = Restore(content, snapshot with
        {
            Options = snapshot.Options with { AggressiveRetaliation = condition != "non-aggressive" },
            World = snapshot.World with { Ufos = [ufo] },
        });
        AdvanceOne(campaign);
        Assert.False(campaign.Capture().Bases[0].RetaliationTarget);
        Assert.Equal(snapshot.RandomState, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(0, 0, false, true)]
    [InlineData(26, 0, false, false)]
    [InlineData(26, 1, false, true)]
    [InlineData(26, 0, true, true)]
    [InlineData(50, 1, true, false)]
    public void DestroyedDepartureScansBeforeCleanupButCrashDamageDoesNot(
        int damage, int huntBehavior, bool unmanned, bool scans)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var campaign = Restore(content, snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with
            {
                Status = UfoStatus.Destroyed,
                Damage = damage,
                HuntBehavior = huntBehavior,
                RuleId = unmanned ? "UFO_SCAN_UNMANNED" : "UFO_SCOUT",
            }]
            },
        });
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(scans, after.Bases[0].RetaliationTarget);
        Assert.Equal(scans ? StateAfterRolls(2, 1) : snapshot.RandomState, after.RandomState);
        Assert.Empty(after.World.Ufos);
        Assert.Equal(0, Assert.Single(after.World.Missions).LiveUfos);
    }

    [Fact]
    public void RaceBonusEnablesSightAndFirstSuccessStopsTheUfoScan()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with { RuleId = "UFO_SCAN_TEST" };
        var campaign = Restore(content, snapshot with
        {
            Bases = [snapshot.Bases[0] with { RetaliationTarget = true }],
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { Race = "RACE_B", LiveUfos = 2 }],
                Ufos = [ufo, ufo with { UniqueId = 10, Id = 4 }],
            },
        });
        AdvanceOne(campaign);
        Assert.True(campaign.Capture().Bases[0].RetaliationTarget);
        // An already-marked base still rolls; success stops before the second UFO.
        Assert.Equal(StateAfterRolls(2, 1), campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(-0.000001, true)]
    [InlineData(0, false)]
    [InlineData(0.000001, false)]
    public void SightRangeBoundaryIsExclusive(double offset, bool scans)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with
        {
            RuleId = "UFO_SCAN_BOUNDARY",
            Longitude = Math.PI / 2 + offset,
            Latitude = 0,
            Destination = snapshot.World.Ufos[0].Destination! with { Longitude = 2, Latitude = 0 },
        };
        if (offset == 0)
            Assert.Equal(WorldGeometry.Nautical(5400), WorldGeometry.Distance(WorldPosition.Origin, ufo.Position));
        var campaign = Restore(content, snapshot with
        {
            Bases = [snapshot.Bases[0] with { Longitude = 0, Latitude = 0 }],
            World = snapshot.World with { Ufos = [ufo] },
        });
        AdvanceOne(campaign);
        Assert.Equal(scans, campaign.Capture().Bases[0].RetaliationTarget);
        Assert.Equal(scans ? StateAfterRolls(2, 1) : snapshot.RandomState, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void AggressiveModeMarksAllBasesOtherwiseOnlyTheLastDiscoveredBasePerRegion(bool aggressive, bool outsideRegion)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var first = snapshot.Bases[0] with { Longitude = outsideRegion ? 1 : 0.2 };
        var second = first with { Id = first.Id + 1, Name = "Beta", Crafts = [], Soldiers = [] };
        var campaign = Restore(content, snapshot with
        {
            RandomState = 30, // successive rolls 10 and 8 both detect a 15%-chance base
            Options = snapshot.Options with { AggressiveRetaliation = aggressive },
            Bases = [first, second],
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { RuleId = "MISSION_RETALIATION" }],
                Ufos = [snapshot.World.Ufos[0] with { Longitude = first.Longitude + 0.01 }],
            },
        });
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(aggressive, after.Bases[0].RetaliationTarget);
        Assert.True(after.Bases[1].RetaliationTarget);
        Assert.Equal(StateAfterRolls(30, 2), after.RandomState);
        var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(after), content,
            seed: 42, name: "base-scan-option.sav").Campaign;
        Assert.Equal(aggressive, loaded.Options.AggressiveRetaliation);
        Assert.Equivalent(after.Bases, loaded.Capture().Bases, strict: true);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    public void OnlyCompletedEnabledMindShieldsReduceDetection(int buildTime, bool disabled, bool scans)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = WithShield(CreateSnapshot(content), "MIND_SCREEN_TEST", buildTime, disabled);
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.Equal(scans, campaign.Capture().Bases[0].RetaliationTarget);
        Assert.Equal(scans ? StateAfterRolls(2, 1) : snapshot.RandomState, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonAggressiveSelectionSeparatesRegionsAndKeepsEarlierSuccessWhenTheLastBaseFails(bool differentRegions)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var first = snapshot.Bases[0] with { Longitude = 0.51 };
        var second = first with
        {
            Id = first.Id + 1,
            Name = "Beta",
            Longitude = differentRegions ? 0.53 : 0.51,
            Crafts = [],
            Soldiers = [],
        };
        // With seed 2 the first roll succeeds and the second fails; with 30 both succeed.
        var seed = differentRegions ? 30UL : 2UL;
        var choices = new SplitMix64RandomSource(seed);
        Assert.True(choices.NextInclusive(0, 99) < 15);
        Assert.Equal(differentRegions, choices.NextInclusive(0, 99) < 15);
        var campaign = Restore(content, snapshot with
        {
            RandomState = seed,
            Options = snapshot.Options with { AggressiveRetaliation = false },
            Bases = [first, second],
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { RuleId = "MISSION_RETALIATION" }],
                Ufos = [snapshot.World.Ufos[0] with { Longitude = 0.52 }],
            },
        });
        AdvanceOne(campaign);
        Assert.True(campaign.Capture().Bases[0].RetaliationTarget);
        Assert.Equal(differentRegions, campaign.Capture().Bases[1].RetaliationTarget);
        Assert.Equal(choices.State, campaign.Capture().RandomState);
    }

    [Fact]
    public void LegacySaveWithoutThePortOptionKeepsTheReferenceDefault()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var yaml = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content));
        Assert.Contains("aggressiveRetaliation: true", yaml, StringComparison.Ordinal);
        yaml = string.Join('\n', yaml.Split('\n').Where(line =>
            !line.TrimStart().StartsWith("aggressiveRetaliation:", StringComparison.Ordinal)));
        var campaign = TestFixtures.LoadLogisticsSave(yaml, content, seed: 43, name: "legacy-scan.sav").Campaign;
        Assert.True(campaign.Options.AggressiveRetaliation);
        AdvanceOne(campaign);
        Assert.True(campaign.Capture().Bases[0].RetaliationTarget);
    }

    [Fact]
    public void MidnightConstructionCompletionAppliesBeforeTheScan()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = WithShield(CreateSnapshot(content), "MIND_SCREEN_TEST", 1, false) with
        { Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55) };
        var campaign = Restore(content, snapshot);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var after = campaign.Capture();
        Assert.Equal(0, after.Bases[0].Facilities[1].BuildTime);
        Assert.False(after.Bases[0].RetaliationTarget);
        Assert.Equal(snapshot.RandomState, after.RandomState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidMindShieldPowerStopsBeforeAnyTimeOrRandomChanges(bool midnight)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = WithShield(CreateSnapshot(content), "MIND_SCREEN_INVALID", midnight ? 1 : 0, false);
        if (midnight) snapshot = snapshot with { Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55) };
        AssertBlocked(Restore(content, snapshot), "Base detection inputs are outside the supported range.");
    }

    [Fact]
    public void InvalidBasePositionIsRejectedAtRestore()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var error = Assert.Throws<InvalidDataException>(() => Restore(content, snapshot with
        { Bases = [snapshot.Bases[0] with { Longitude = 7 }] }));
        Assert.Equal("Base longitude must be in [0, 2π).", error.Message);
    }

    [Fact]
    public void InconsistentLiveCountCannotDeleteAMissionBeforeItsUfoHandlersRun()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content);
        var campaign = Restore(content, snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { LiveUfos = 0 }] },
        });
        AssertBlocked(campaign, "Alien mission cannot expire while UFOs still reference it.");
    }

    private static CampaignSnapshot WithShield(CampaignSnapshot snapshot, string rule, int buildTime, bool disabled) =>
        snapshot with
        {
            Bases = [snapshot.Bases[0] with { Facilities = [.. snapshot.Bases[0].Facilities,
            new FacilitySnapshot(rule, 1, 0, buildTime, 0, false, disabled, false)] }]
        };

    private static CampaignSnapshot CreateSnapshot(RuntimeContent content, bool landed = false)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "UFO base detection", CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        return snapshot with
        {
            MonthsPassed = 0,
            RandomState = 2,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 9, 55),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_LANDING", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_LONG_LANDING", 2,
                    0.21, 0.1, landed ? UfoStatus.Landed : UfoStatus.Flying,
                    landed ? WorldAltitudes.Ground : WorldAltitudes.VeryLow)
                {
                    Id = 3, MissionWaveNumber = 0, Speed = landed ? 0 : 1100,
                    SecondsRemaining = landed ? 1800 : 0, Shield = 0,
                    Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                        WorldTargetReference.WaypointType, 0, 1.2, 0.3),
                }],
            },
        };
    }

    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(2));

    private static ulong StateAfterRolls(ulong seed, int count)
    {
        var random = new SplitMix64RandomSource(seed);
        for (var index = 0; index < count; index++) random.NextInclusive(0, 99);
        return random.State;
    }

    private static void AdvanceOne(CampaignState campaign) => Assert.Equal(1,
        Assert.IsType<CampaignTimeAdvanced>(Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events)).Summary.TickCount);

    private static void AssertBlocked(CampaignState campaign, string reason)
    {
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(reason, Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
