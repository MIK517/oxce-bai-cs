using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldAlienBaseActivityTests
{
    [Theory]
    [InlineData("BASE_MERGED_OVERRIDE")]
    [InlineData("BASE_MERGED_GENERATING")]
    public void InheritedGeneratingChoiceCreatesMissionWhenSelected(string deployment)
    {
        var content = Load();
        var rule = content.RuntimeRules.AlienDeployments[content.RuntimeRules.AlienDeployments.GetRequired(deployment)].Value;
        Assert.Equal(1UL, rule.GenMission["MISSION_SUPPLY"]);
        Assert.Equal(1UL, rule.GenMission[string.Empty]);
        var snapshot = Snapshot(content, deployment, daily: true);
        var random = new SplitMix64RandomSource(snapshot.RandomState);
        var selected = random.NextInclusive(1, 2) == 2;
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.Equal(selected ? 1 : 0, campaign.Capture().World.Missions.Count);
        Assert.Equal(selected ? 1 : 0, campaign.Capture().World.AlienBases[0].GenMissionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearCoincidentBaseSightCompletesTheDayAndPersists(bool sight)
    {
        var content = Load();
        var snapshot = Snapshot(content, daily: true);
        snapshot = snapshot with
        {
            Bases = [snapshot.Bases[0] with { Longitude = 0.2, Latitude = 0.0002 }],
            World = snapshot.World with
            {
                AlienBases = [snapshot.World.AlienBases[0] with
            {
                Longitude = 0.2 + 1e-12, Latitude = 0.0002,
            }]
            },
        };
        if (sight) snapshot = snapshot.WithFacility("SIGHT_ALWAYS");
        var campaign = Restore(content, snapshot);
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.Time.Day);
        Assert.Equal(snapshot.DaysPassed + 1, after.DaysPassed);
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 7, after.Regions[0].ActivityAlien[^1]);
        Assert.Equal(sight, Assert.Single(after.World.AlienBases).Discovered);
        Reload(content, campaign, 40, "near-base-discovery.sav");
    }

    [Fact]
    public void SightBranchesMatchExtractedReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-activity");
        var facilities = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "facilityRows"))
        {
            var known = row[5].GetInt32() != 0;
            var discovered = known;
            var alerts = 0;
            var rolls = new List<int>();
            if (!known)
                for (var index = 0; index < row[4].GetInt32(); index++)
                    if (WorldAlienBaseSight.FacilityChance(WorldGeometry.Nautical(row[0].GetInt32()),
                            row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32()) is { } chance)
                    {
                        rolls.Add(chance);
                        if (chance > 0) { discovered = true; alerts++; }
                    }
            facilities.Add([row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(),
                row[4].GetInt32(), known ? 1 : 0, discovered ? 1 : 0, alerts, rolls]);
        }
        EqualRows(oracle.RootElement.GetProperty("facilityRows"), facilities);
        var crafts = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "craftRows"))
        {
            var rolls = new List<int>();
            var known = row[2].GetInt32() != 0;
            var discovered = known;
            if (WorldAlienBaseSight.CraftChance(WorldGeometry.Nautical(row[0].GetInt32()),
                    row[1].GetInt32()) is { } chance)
            {
                rolls.Add(chance);
                discovered |= chance > 0;
            }
            crafts.Add([row[0].GetInt32(), row[1].GetInt32(), known ? 1 : 0, discovered ? 1 : 0, rolls]);
        }
        EqualRows(oracle.RootElement.GetProperty("craftRows"), crafts);
    }

    [Fact]
    public void DailyActivityMatchesFirstRegionAndCountryReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-activity");
        var content = Load();
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "activityRows"))
        {
            var points = row[0].GetInt32();
            var snapshot = Snapshot(content, points == 7 ? "BASE_PASSIVE" : "BASE_NEGATIVE", daily: true);
            snapshot = snapshot with
            {
                Regions = [snapshot.Regions.Single(region => region.RuleId == "REGION_OUTSIDE"),
                    snapshot.Regions.Single(region => region.RuleId == "REGION"),
                    snapshot.Regions.Single(region => region.RuleId == "REGION_OVERLAP")],
            };
            var campaign = Restore(content, snapshot);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            rows.Add([points, .. after.Regions.Select((region, index) => (object)(region.ActivityAlien[^1] -
                snapshot.Regions[index].ActivityAlien[^1])),
                after.Countries[0].ActivityAlien[^1] - snapshot.Countries[0].ActivityAlien[^1]]);
            Reload(content, campaign, 40, "base-activity.sav");
        }
        EqualRows(oracle.RootElement.GetProperty("activityRows"), rows);
    }

    [Fact]
    public void CompletingDisabledFacilitiesDiscoverAndContinueAllChecksAcrossReload()
    {
        var content = Load();
        var snapshot = Snapshot(content, daily: true).WithFacility("SIGHT_ALWAYS", buildTime: 1, disabled: true);
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with
        {
            Facilities = [.. owner.Facilities, new FacilitySnapshot("SIGHT_ALWAYS", 2, 0, 0, 0, false, false, false)],
        }]
        };
        var campaign = Restore(content, snapshot);
        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;
        Assert.Equal(2, events.OfType<AlienBaseDiscovered>().Count());
        Assert.Contains(events, notification => notification is CampaignConstructionCompleted);
        var after = campaign.Capture();
        Assert.True(Assert.Single(after.World.AlienBases).Discovered);
        Assert.Equal(StateAfterRolls(snapshot.RandomState, 2), after.RandomState);
        Assert.Contains(campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets,
            target => target.Kind == WorldTargetKind.AlienBase);
        campaign = Reload(content, campaign, 40, "base-discovery.sav");
        AdvanceUnblocked(campaign, 17_280);
        Assert.Equal(after.RandomState, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PatrolSightIncludesDisabledWeaponBonusAndRollsForKnownBases(bool known, bool bonus)
    {
        var content = Load();
        var snapshot = Patrol(Snapshot(content), bonus ? 150 : 0, fuel: 100, known);
        if (bonus) snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Weapons = [new("SIGHT_BOOST", 0, Disabled: true), null] },
        });
        var expectedRandom = new SplitMix64RandomSource(snapshot.RandomState);
        var chance = bonus ? 12 : 50;
        var success = expectedRandom.NextInclusive(0, 99) < chance;
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign); // craft discovery has no alert
        var after = campaign.Capture();
        Assert.Equal(known || success, Assert.Single(after.World.AlienBases).Discovered);
        Assert.Equal(StateAfterRolls(snapshot.RandomState, 1), after.RandomState);
        Reload(content, campaign, 40, "patrol-discovery.sav");
    }

    [Fact]
    public void FuelRecallPrecedesPatrolSight()
    {
        var content = Load();
        var snapshot = Patrol(Snapshot(content), 0, fuel: 0, known: false);
        var campaign = Restore(content, snapshot);
        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;
        Assert.Single(events.OfType<CraftLowFuel>());
        Assert.False(Assert.Single(campaign.Capture().World.AlienBases).Discovered);
        Assert.Equal(snapshot.RandomState, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData("BASE_ZERO_FREQUENCY", 2)]
    [InlineData("BASE_EXHAUSTED", 1)]
    public void NonGeneratingSupplyChecksStillChooseMissionBeforeFrequencyAndLimit(string deployment, int rolls)
    {
        var content = Load();
        var snapshot = Snapshot(content, deployment, daily: true);
        var random = new SplitMix64RandomSource(snapshot.RandomState);
        _ = random.NextInclusive(1, 5);
        if (rolls == 2) _ = random.NextInclusive(0, 99);
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(random.State, campaign.Capture().RandomState);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.AlienBases).GenMissionCount);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void EmptyGenerationChoiceUsesOrdinalWeightOrderAndSkipsFrequency(ulong seed)
    {
        var content = Load();
        var snapshot = Snapshot(content, "BASE_EMPTY_CHOICE", daily: true) with { RandomState = seed };
        var random = new SplitMix64RandomSource(seed);
        if (random.NextInclusive(1, 2) == 2) _ = random.NextInclusive(0, 99);
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.Equal(random.State, campaign.Capture().RandomState);
    }

    [Fact]
    public void FacilityNotificationPreservesCurrentTickPatrolFallthrough()
    {
        var content = Load();
        var snapshot = Snapshot(content, daily: true).WithFacility("SIGHT_ALWAYS")
            .WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Fuel = 100,
                    Longitude = 0.2,
                    Latitude = 0.1,
                    Destination = null,
                    Speed = 0
                },
            });
        var campaign = Restore(content, snapshot);
        var events = campaign.Execute(new AdvanceCampaignTime(2)).Events;
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(events[0]).Summary.TickCount);
        Assert.Single(events.OfType<AlienBaseDiscovered>());
        Assert.True(Assert.Single(campaign.Capture().World.AlienBases).Discovered);
        Assert.Equal(StateAfterRolls(snapshot.RandomState, 2), campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData("BASE_MISSING_MISSION", true, "Alien base generation references an undefined alien mission.")]
    [InlineData("BASE_EXCESSIVE_WEIGHTS", true, "Alien base mission weights exceed the supported range.")]
    public void UnownedTransitionsBlockBeforeTheirAffectedTick(string deployment, bool daily, string reason)
    {
        var content = Load();
        var snapshot = Snapshot(content, deployment, daily);
        // Mere presence of a base no longer blocks five-second movement.
        var earlier = snapshot with { Time = snapshot.Time with { Second = 50 } };
        AdvanceOne(Restore(content, earlier));
        AssertTimeBlocked(Restore(content, snapshot), reason);
    }

    [Fact]
    public void HuntingRangeWithoutActiveCraftAdvancesTimerAndReloads()
    {
        var content = Load();
        var campaign = Restore(content, Snapshot(content, "BASE_HUNTING"));
        AdvanceOne(campaign);
        Assert.Equal(10, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Reload(content, campaign, 40, "inactive-hunting-base.sav");
    }

    [Fact]
    public void SightOverflowStopsBeforeFuelConsumption()
    {
        var content = Load();
        var snapshot = Patrol(Snapshot(content), 0, 100, false).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Weapons = [new("SIGHT_EXCESS", 0), null] },
        });
        AssertTimeBlocked(Restore(content, snapshot), "Craft weapon sight bonuses exceed the supported range.");
    }

    [Fact]
    public void DailyBaseAndHalfHourUfoScoresShareAtomicOverflowPreflight()
    {
        var content = Load();
        var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
            "UFO_SCOUT", 2200, missionWaveNumber: 0).Capture();
        snapshot = snapshot with
        {
            Regions = [.. snapshot.Regions.Select(region => region with { ActivityAlien = [int.MaxValue - 8] })],
            World = snapshot.World with { AlienBases = [new(2, "BASE_PASSIVE", "RACE_A", 0.4, 0.2)] },
        };
        AssertTimeBlocked(Restore(content, snapshot), "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void PassiveBaseRunsSeventyTwoHoursAndRewritesDiscoveryWithoutResurrectingDeletedState()
    {
        var content = Load();
        var snapshot = Snapshot(content) with { Time = new CampaignTime(1, 1, 1, 1999, 0, 0, 0) };
        var campaign = Restore(content, snapshot);
        AdvanceUnblocked(campaign, 51_840);
        var after = campaign.Capture();
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 21, after.Regions[0].ActivityAlien[^1]);
        var yaml = OxceSaveAdapter.EmitNewCampaign(after);
        var section = yaml.IndexOf("alienBases:\n", StringComparison.Ordinal);
        Assert.True(section >= 0);
        var lineEnd = yaml.IndexOf('\n', section + "alienBases:\n".Length);
        yaml = yaml.Insert(lineEnd + 1, "    futureBase: preserved\n");
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40, "base-sidecar.sav");
        var discovered = loaded.Campaign.Capture() with
        {
            World = after.World with { AlienBases = [after.World.AlienBases[0] with { Discovered = true }] },
        };
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(discovered, loaded.Source);
        Assert.Contains("futureBase: preserved", rewritten, StringComparison.Ordinal);
        Assert.True(Assert.Single(TestFixtures.LoadLogisticsSave(rewritten, content, 40, "base-sidecar.sav")
            .Campaign.Capture().World.AlienBases).Discovered);
        var deleted = discovered with { World = discovered.World with { AlienBases = [] } };
        rewritten = OxceSaveAdapter.EmitLoadedCampaign(deleted, loaded.Source);
        Assert.DoesNotContain("futureBase:", rewritten, StringComparison.Ordinal);
        Assert.Empty(TestFixtures.LoadLogisticsSave(rewritten, content, 40, "base-deleted.sav").Campaign.Capture().World.AlienBases);
    }

    [Fact]
    public void ExcessiveSightCrossProductStopsBeforeAnyRollOrFuelTransition()
    {
        var content = Load();
        var snapshot = Patrol(Snapshot(content), 0, 100, false);
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts.Single(candidate => candidate.RuleId == "SHIP");
        snapshot = snapshot with
        {
            Bases = [owner with { Crafts = [.. owner.Crafts.Where(candidate => candidate.RuleId != "SHIP"),
                .. Enumerable.Range(1, 1000).Select(id => craft with { Id = id })] }],
            World = snapshot.World with
            {
                AlienBases = [.. Enumerable.Range(1, 1001)
                .Select(id => new AlienBaseSnapshot(id, snapshot.World.AlienBases[0].DeploymentId,
                    snapshot.World.AlienBases[0].Race, snapshot.World.AlienBases[0].Longitude,
                    snapshot.World.AlienBases[0].Latitude))]
            },
        };
        AssertTimeBlocked(Restore(content, snapshot),
            "Alien base activity and sight checks exceed the supported work limit.");
    }

    private static RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-activity");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-activity.rul"]);
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, string deployment = "BASE_PASSIVE", bool daily = false)
    {
        var snapshot = CreateSnapshot(content, "Alien-base activity");
        return snapshot with
        {
            RandomState = 2,
            Time = new CampaignTime(1, 1, 1, 1999, daily ? 23 : 1, daily ? 59 : 9, 55),
            World = snapshot.World with { AlienBases = [new(2, deployment, "RACE_A", 0.2, 0.1)] },
        };
    }

    private static CampaignSnapshot Patrol(CampaignSnapshot snapshot, int distance, int fuel, bool known) =>
        (snapshot with
        {
            World = snapshot.World with
            {
                AlienBases = [snapshot.World.AlienBases[0] with
            {
                Longitude = 0.2 + WorldGeometry.Nautical(distance), Latitude = 0, Discovered = known,
            }],
            }
        }).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = fuel,
                Longitude = 0.2,
                Latitude = 0,
                Destination = null,
                Speed = 0,
                LowFuel = false
            },
        });

    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));

    private static ulong StateAfterRolls(ulong seed, int rolls)
    {
        var random = new SplitMix64RandomSource(seed);
        for (var index = 0; index < rolls; index++) _ = random.NextInclusive(0, 99);
        return random.State;
    }

    private static void EqualRows(JsonElement expected, List<object[]> actual) =>
        TestFixtures.AssertJsonEqual(expected, JsonSerializer.SerializeToUtf8Bytes(actual));
}
