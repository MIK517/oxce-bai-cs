using System.Diagnostics;
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

public sealed class StrategicWorldEnduranceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeventyTwoHoursComposeFiniteMissionsCraftOperationsAndReloads(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        var snapshot = CreateLifecycleSnapshot(content);
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts.Single(item => item.RuleId == "SHIP");
        snapshot = snapshot with
        {
            Options = snapshot.Options with { UfoLandingAlert = true },
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Bases = [owner with
            {
                Soldiers = [],
                Crafts = [craft with { RuleId = "SHIP_SLOW_REFUEL", Logistics = craft.Logistics! with
                    { Fuel = 300, Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [], Status = "STR_READY" } }],
                Facilities = [.. owner.Facilities,
                    new("RADAR_HYPER_TEST", 1, 0, 0, 0, false, false, false),
                    new("MIND_SCREEN_TEST", 2, 0, 1, 0, false, false, false)],
            }],
            World = snapshot.World with
            {
                Ufos = [],
                Missions = [snapshot.World.Missions[0] with
                    { RuleId = "MISSION_ENDURANCE", NextWave = 0, LiveUfos = 0, SpawnCountdown = 30 }],
            },
        };
        var stepped = Run(1, false);
        var batched = Run(120, false);
        var reloaded = Run(120, true);
        Assert.Equivalent(stepped, batched, strict: true);
        Assert.Equivalent(stepped, reloaded, strict: true);

        CampaignSnapshot Run(int maximumBatch, bool reload)
        {
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            var ticks = 0;
            var contacts = 0;
            var landings = 0;
            var serviced = false;
            var relaunched = false;
            var lowFuelReturn = false;
            var lastStatus = "STR_READY";
            var peak = 0;
            // The recalled craft needs two half-hour refuelling passes before redispatch.
            int[] commands = [0, 120, 240, 1440, 1560, 72 * 720];
            var command = 0;
            while (ticks < 72 * 720)
            {
                if (ticks == commands[command])
                {
                    ICampaignCommand action = command switch
                    {
                        0 or 3 => new DispatchCraftToWaypoint(0, "SHIP_SLOW_REFUEL", craft.Id, 0.2005, 0.1),
                        1 or 4 => new PatrolCraft(0, "SHIP_SLOW_REFUEL", craft.Id),
                        _ => new RecallCraft(0, "SHIP_SLOW_REFUEL", craft.Id),
                    };
                    var response = Assert.Single(campaign.Execute(action).Events);
                    Assert.True(response is CraftDestinationChanged, $"Command {command} at tick {ticks}: {response}");
                    command++;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(Math.Min(maximumBatch, commands[command] - ticks)));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var advanced = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
                Assert.InRange(advanced, 1, maximumBatch);
                ticks += advanced;
                contacts += result.Events.Count(item => item is UfoContactDetected);
                landings += result.Events.Count(item => item is UfoLanded);
                if (ticks % 120 != 0 && result.Events.Count == 1) continue;
                var current = campaign.Capture();
                peak = Math.Max(peak, current.World.Ufos.Count);
                Assert.InRange(current.World.Ufos.Count, 0, 2);
                foreach (var mission in current.World.Missions)
                    Assert.Equal(current.World.Ufos.Count(ufo => ufo.MissionId == mission.Id), mission.LiveUfos);
                var state = current.Bases[0].Crafts[0].Logistics!;
                serviced |= state.Status == "STR_REFUELLING";
                // No command recalls the craft after the second patrol order, so a patrolling
                // craft that is low on fuel or refuelling must have made a low-fuel return.
                lowFuelReturn |= ticks > commands[4] && state.IsAutoPatrolling &&
                    (state.LowFuel || state.Status == "STR_REFUELLING");
                relaunched |= lastStatus == "STR_REFUELLING" && state.Status == "STR_OUT" && state.IsAutoPatrolling;
                lastStatus = state.Status;
                if (reload)
                {
                    campaign = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(current), content,
                        seed: 17, name: "world-endurance.sav").Campaign;
                    Assert.Equivalent(current, campaign.Capture(), strict: true);
                }
            }
            var after = campaign.Capture();
            Assert.Equal(36, contacts);
            Assert.Equal(36, landings);
            Assert.True(serviced);
            Assert.True(lowFuelReturn);
            Assert.True(relaunched);
            Assert.Equal(2, peak);
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.Missions);
            Assert.Equal(snapshot.DaysPassed + 3, after.DaysPassed);
            Assert.Equal(0, Assert.Single(after.Bases[0].Facilities,
                facility => facility.RuleId == "MIND_SCREEN_TEST").BuildTime);
            Assert.Equal(snapshot.NextIds.GetValueOrDefault("STR_UFO_UNIQUE", 1) + 36, after.NextIds["STR_UFO_UNIQUE"]);
            return after;
        }
    }

    [Fact]
    public void PopulatedTicksScaleAndRecordSimulationAndPersistenceCosts()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        List<object> measurements = [];
        foreach (var bases in new[] { 1, 8 })
            foreach (var operation in new[] { "flight", "boundary", "cleanup" })
            {
                _ = Measure(8, bases, operation, record: false); // JIT and shared caches
                var small = Measure(32, bases, operation, record: true);
                var large = Measure(128, bases, operation, record: true);
                // Fixed per-command costs cancel; what remains is the cost of each additional UFO.
                var perUfo = (large - small) / 96.0;
                Assert.True(perUfo <= MaximumBytesPerUfo(operation),
                    $"{operation}/{bases} bases: {perUfo:F1} B per additional UFO ({small} B for 32, {large} B for 128).");
            }
        var path = TestFixtures.RepositoryPath("artifacts", "world-closure-measurements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(measurements));

        // Regression bounds per additional UFO, measured 2026-09-26 with 1 and 8 bases:
        // flight replaces one immutable UFO record per tick (208 B/tick measured over 12 ticks);
        // the half-hour boundary scores and scans every base (752 B with 1 base, 1536 B with 8);
        // cleanup removes the destroyed UFO (80 B).
        static double MaximumBytesPerUfo(string operation) => operation switch
        {
            "flight" => 12 * 256,
            "boundary" => 2048,
            "cleanup" => 128,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        long Measure(int count, int bases, string operation, bool record)
        {
            var snapshot = CreateLifecycleSnapshot(content);
            var owner = snapshot.Bases[0];
            var ship = owner.Crafts.Single(item => item.RuleId == "SHIP");
            var ufo = snapshot.World.Ufos[0] with
            {
                Status = operation == "cleanup" ? UfoStatus.Destroyed : UfoStatus.Flying,
                Speed = 2200,
                Altitude = WorldAltitudes.VeryLow,
                Detected = true,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = WorldTargetReference.ForWaypoint(0, new(1.2, 0.3)),
            };
            var campaign = CampaignState.Restore(snapshot with
            {
                Time = new CampaignTime(1, 1, 1, 1999, 1, operation == "flight" ? 0 : 29, operation == "flight" ? 0 : 55),
                Bases = Enumerable.Range(0, bases).Select(index => owner with
                {
                    Id = index,
                    Soldiers = [],
                    Crafts = [ship with { Id = index + 1,
                        Logistics = ship.Logistics! with { Status = "STR_OUT", Fuel = 100, Weapons = [],
                            Destination = WorldTargetReference.ForWaypoint(index + 1, new(0.8, 0.1)),
                            Longitude = 0.2, Latitude = 0.1, Speed = 6000 } }],
                }).ToArray(),
                World = snapshot.World with
                {
                    Missions = Enumerable.Range(0, bases).Select(index => snapshot.World.Missions[0] with
                    { Id = index + 4, LiveUfos = count / bases }).ToArray(),
                    Ufos = Enumerable.Range(0, count).Select(index => ufo with
                    { UniqueId = index + 9, Id = index + 3, MissionId = index % bases + 4 }).ToArray(),
                    Waypoints = Enumerable.Range(1, bases).Select(id => new WaypointSnapshot(id, 0.8, 0.1)).ToArray(),
                },
            }, content, new SplitMix64RandomSource(17));
            var ticks = operation == "flight" ? 12 : 1;
            var start = Stopwatch.GetTimestamp();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = campaign.Execute(new AdvanceCampaignTime(ticks));
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.Equal(ticks, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            var after = campaign.Capture();
            Assert.Equal(operation == "cleanup" ? 0 : count, after.World.Ufos.Count);
            Assert.Equal(after.World.Ufos.Count, after.World.Missions.Sum(item => item.LiveUfos));
            start = Stopwatch.GetTimestamp();
            before = GC.GetAllocatedBytesForCurrentThread();
            var captured = campaign.Capture();
            var yaml = OxceSaveAdapter.EmitNewCampaign(captured);
            var writeBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var writeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            before = GC.GetAllocatedBytesForCurrentThread();
            var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 17, name: "populated-closure.sav").Campaign;
            var loadBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var loadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.Equivalent(captured, loaded.Capture(), strict: true);
            if (record)
                measurements.Add(new { count, bases, operation, ticks, bytes, elapsed, writeBytes, writeMs, loadBytes, loadMs });
            return bytes;
        }
    }
}
