using System.Diagnostics;
using System.Globalization;
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

public sealed class StrategicWorldEscortEnduranceTests
{
    [Fact]
    public void EscortRingsComposeCraftPursuitPatrolFuelAndReloadsForSeventyTwoHours()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], "ufos: [{type: HUNTER, scripts: {detectUfoFromBase: 'return DETECTION_RADAR 100;'}}]");
        var initial = Ring(content, 3);
        var ship = CreateSnapshot(content, "Escort craft operations").Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        ship = ship with
        {
            RuleId = "SHIP_SLOW_REFUEL",
            Logistics = ship.Logistics! with
            { Fuel = 300, Status = "STR_READY", Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [] }
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [ship] }],
            World = initial.World with { Ufos = initial.World.Ufos.Select(ufo => ufo with { Detected = true }).ToArray() },
        };
        var stepped = Run(1, false);
        Assert.Equivalent(stepped, Run(120, false), strict: true);
        Assert.Equivalent(stepped, Run(120, true), strict: true);

        CampaignSnapshot Run(int batch, bool reload)
        {
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            int[] commands = [0, 120, 240, 1440, 1560, 72 * 720];
            var command = 0;
            var ticks = 0;
            var servicing = false;
            var lowFuel = false;
            var pursuing = false;
            while (ticks < commands[^1])
            {
                if (ticks == commands[command])
                {
                    ICampaignCommand action = command switch
                    {
                        0 => new DispatchCraftToTarget(0, ship.RuleId, ship.Id,
                            new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 }),
                        1 or 4 => new PatrolCraft(0, ship.RuleId, ship.Id),
                        2 => new RecallCraft(0, ship.RuleId, ship.Id),
                        _ => new DispatchCraftToWaypoint(0, ship.RuleId, ship.Id, 0.2005, 0.1),
                    };
                    Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(action).Events));
                    command++;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(Math.Min(batch, commands[command] - ticks)));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var advanced = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
                Assert.InRange(advanced, 1, batch);
                ticks += advanced;
                if (ticks % 120 != 0 && result.Events.Count == 1 && ticks >= commands[1]) continue;
                var current = campaign.Capture();
                Assert.Equal(3, current.World.Ufos.Count);
                Assert.Equal(3, Assert.Single(current.World.Missions).LiveUfos);
                var state = Assert.Single(current.Bases[0].Crafts).Logistics!;
                pursuing |= ticks <= commands[1] && state.Destination is { Kind: WorldTargetKind.Ufo, UniqueId: 9 } &&
                    WorldGeometry.Distance(new(state.Longitude, state.Latitude),
                        new(initial.Bases[0].Longitude, initial.Bases[0].Latitude)) > 0;
                servicing |= state.Status == "STR_REFUELLING";
                // Native checkup clears LowFuel when the nearby patrol returns in the
                // same tick. No recall is issued after the second patrol, so only a
                // fuel return can cause this later servicing state.
                lowFuel |= ticks > commands[4] && state.IsAutoPatrolling &&
                    state.Status == "STR_REFUELLING" && state.Fuel < 300;
                if (reload) campaign = Reload(content, campaign, 81, "escort-craft-endurance.sav");
            }
            Assert.True(pursuing);
            Assert.True(servicing);
            Assert.True(lowFuel);
            var after = campaign.Capture();
            Assert.Equal(initial.DaysPassed + 3, after.DaysPassed);
            return after;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortRingDetectionExecutesLiveFlagsAndInstanceTagsAcrossRewrites(bool reverse)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var tagValue = TestFixtures.Rows(oracle.RootElement, "tagValues")[2][1].GetInt32();
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], $$"""
            extended:
              tags:
                Ufo: {ESCORT_INSTANCE: int}
            ufos:
              - type: HUNTER
                scripts:
                  detectUfoFromBase: |
                    var int value;
                    ufo.isEscorting value;
                    if neq value 1; return DETECTION_NONE 0; end;
                    ufo.isHunting value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    ufo.isHunterKiller value;
                    if neq value 0; return DETECTION_NONE 0; end;
                    ufo.getTag value Tag.ESCORT_INSTANCE;
                    if neq value {{tagValue}}; return DETECTION_NONE 0; end;
                    return DETECTION_HYPERWAVE 100;
            """);
        var initial = Ring(content, 3, reverse);
        initial = initial with
        {
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            World = initial.World with
            {
                Ufos = initial.World.Ufos.Select(ufo => ufo with
                { Detected = false, HyperDetected = false, ScriptValues = [new("ESCORT_INSTANCE", "int", tagValue)] }).ToArray()
            },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "escort-script-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(3, result.Events.OfType<UfoContactDetected>().Count());
        for (var boundary = 0; boundary < 4; boundary++)
        {
            var after = campaign.Capture();
            Assert.All(after.World.Ufos, ufo =>
            {
                Assert.True(ufo.Detected);
                Assert.True(ufo.HyperDetected);
                Assert.True(ufo.Escorting);
                Assert.Equal(tagValue, Assert.Single(ufo.ScriptValues).Value);
            });
            campaign = Reload(content, campaign, 81, "escort-script-rewrite.sav");
            campaign = Reload(content, campaign, 81, "escort-script-second-rewrite.sav");
            if (boundary < 3) AdvanceUnblocked(campaign, 360);
        }
        Assert.Equal(3, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortChainCleanupRepairsOnlyDirectFollowersAcrossReloads(bool reverse)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"]);
        var initial = Ring(content, 4, reverse);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = initial.World.Ufos.Select(ufo => ufo with
                {
                    Longitude = 0.4,
                    Latitude = 0.2,
                    TrajectoryPoint = content.RuntimeRules.UfoTrajectories[
                    content.RuntimeRules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value.Waypoints.Count - 1,
                    Escorting = ufo.UniqueId != 9,
                    OriginalWaypoint = ufo.UniqueId != 9 ? new(0.400001, 0.2) : null,
                    Destination = ufo.UniqueId == 9 ? WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2))
                    : new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id - 1, 0.4, 0.2) { UniqueId = ufo.UniqueId - 1 },
                }).ToArray()
            },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "escort-deletion-before.sav");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "deletedUfoChainRows"))
        {
            AdvanceOne(campaign); // Terminal arrival retains the dead actor and skips subsequent movement/cleanup.
            Assert.Contains(campaign.Capture().World.Ufos, ufo => ufo.Status == UfoStatus.Destroyed);
            campaign = Reload(content, campaign, 81, "escort-terminal.sav");
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Equal(4 - row[0].GetInt32(), Assert.Single(after.World.Missions).LiveUfos);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { row[0].GetInt32() }.Concat(
                after.World.Ufos.OrderBy(ufo => ufo.UniqueId).Select(ufo => (object)new object[]
                {
                    ufo.UniqueId, ufo.Escorting, ufo.OriginalWaypoint is not null,
                    ufo.Destination!.UniqueId, ufo.Speed,
                    ufo.Destination.Kind == WorldTargetKind.Ufo ? 0 : ufo.Destination.Longitude,
                    ufo.Destination.Kind == WorldTargetKind.Ufo ? 0 : ufo.Destination.Latitude,
                })).ToArray());
            TestFixtures.AssertJsonEqual(row, actual);
            campaign = Reload(content, campaign, 81, "escort-deletion-after.sav");
        }
        AdvanceUnblocked(campaign, 2);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
        Reload(content, campaign, 81, "escort-deletion-empty.sav");
    }

    [Fact]
    public void PopulatedEscortTicksRecordColdAndWarmAllocationCosts()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"]);
        List<object> measurements = [];
        foreach (var reverse in new[] { false, true })
        {
            _ = Measure(8, reverse, false);
            _ = Measure(8, reverse, true);
            var small = Measure(32, reverse, true);
            var large = Measure(128, reverse, true);
            // Both saved orders stay below 1 KB per actor/tick after target memoization.
            Assert.InRange((large.Cold - small.Cold) / 96.0, 0, 12 * 1024);
            Assert.InRange((large.Warm - small.Warm) / 96.0, 0, 12 * 1024);
        }
        var path = TestFixtures.RepositoryPath("artifacts", "world-escort-closure-measurements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(measurements));

        (long Cold, long Warm) Measure(int count, bool reverse, bool record)
        {
            var campaign = CampaignState.Restore(Ring(content, count, reverse), content, new SplitMix64RandomSource(81));
            var cold = Tick();
            AdvanceUnblocked(campaign, 720 - 12);
            var warm = Tick();
            var after = campaign.Capture();
            Assert.Equal(count, after.World.Ufos.Count);
            Assert.Equal(count, Assert.Single(after.World.Missions).LiveUfos);
            Reload(content, campaign, 81, "populated-escort.sav");
            if (record) measurements.Add(new { count, reverse, ticks = 12, cold.Bytes, cold.Milliseconds, warmBytes = warm.Bytes, warmMilliseconds = warm.Milliseconds });
            return (cold.Bytes, warm.Bytes);

            (long Bytes, double Milliseconds) Tick()
            {
                var start = Stopwatch.GetTimestamp();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var result = campaign.Execute(new AdvanceCampaignTime(12));
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                Assert.Equal(12, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
                return (bytes, milliseconds);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortRingsComposeSeventyTwoHoursAcrossBatchesAndReloads(bool reverse)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"]);
        var initial = Ring(content, 3, reverse);
        var native = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "escortMovement")
            .Where(row => row[0].GetInt32() == 1 && row[1].GetInt32() == (reverse ? 5 : 0)))
        {
            AdvanceOne(native);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { 1, reverse ? 5 : 0, row[2].GetInt32() }
                .Concat(native.Capture().World.Ufos.OrderBy(ufo => ufo.UniqueId).SelectMany(ufo => new object[]
                {
                    decimal.Parse(ufo.Longitude.ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                    decimal.Parse(ufo.Latitude.ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                })).ToArray());
            TestFixtures.AssertJsonEqual(row, actual);
        }
        var stepped = Run(1, false);
        Assert.Equivalent(stepped, Run(120, false), strict: true);
        Assert.Equivalent(stepped, Run(120, true), strict: true);

        CampaignSnapshot Run(int batch, bool reload)
        {
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            var ticks = 0;
            while (ticks < 72 * 720)
            {
                var result = campaign.Execute(new AdvanceCampaignTime(Math.Min(batch, 72 * 720 - ticks)));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var advanced = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
                Assert.InRange(advanced, 1, batch);
                ticks += advanced;
                if (ticks % 720 != 0) continue;
                var current = campaign.Capture();
                Assert.Equal(3, current.World.Ufos.Count);
                Assert.Equal(3, Assert.Single(current.World.Missions).LiveUfos);
                Assert.All(current.World.Ufos, ufo =>
                {
                    Assert.True(ufo.Escorting);
                    Assert.Equal(UfoStatus.Flying, ufo.Status);
                    Assert.Contains(current.World.Ufos, target => target.UniqueId == ufo.Destination!.UniqueId);
                });
                if (reload) campaign = Reload(content, campaign, 81, "escort-endurance.sav");
            }
            var after = campaign.Capture();
            Assert.Equal(initial.DaysPassed + 3, after.DaysPassed);
            Assert.Equal(initial.World.Ufos.Select(ufo => ufo.UniqueId), after.World.Ufos.Select(ufo => ufo.UniqueId));
            return after;
        }
    }

    private static CampaignSnapshot Ring(RuntimeContent content, int count, bool reverse = false)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0), "HUNTER", 2200).Capture();
        var actors = Enumerable.Range(0, count).Select(index => initial.World.Ufos[0] with
        {
            UniqueId = 9 + index,
            Id = 3 + index,
            HunterKiller = false,
            Escorting = true,
            OriginalWaypoint = new(1, 0.6),
            Longitude = 0.4 + index * 0.1 / (count / 3.0),
            Latitude = 0.2 + index * 0.05 / (count / 3.0),
            Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                3 + (index == 0 ? count - 1 : index - 1), 0, 0)
            { UniqueId = 9 + (index == 0 ? count - 1 : index - 1) },
        }).ToArray();
        return initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [], Research = [], Transfers = [], Productions = [] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = count }],
                Ufos = reverse ? actors.Reverse().ToArray() : actors,
            },
        };
    }
}
