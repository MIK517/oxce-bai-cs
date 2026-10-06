using System.Diagnostics;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void PopulatedHunterRetargetingBoundsAllocationAndPreservesSelections(bool cache, int bases)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(cache, "ufos: [{type: HUNTER, huntSpeed: 0}]\ncrafts: [{type: HUNT_SLOW, speedMax: 2000, damageMax: 100, fuelMax: 300}, {type: HUNT_FAST, speedMax: 6000, damageMax: 100, fuelMax: 300}]\n");
        const int ticks = 24;
        List<object> measurements = [];
        _ = Measure(8);
        var small = Measure(32);
        var large = Measure(128);
        var perHunter = (large - small) / (96.0 * ticks);
        var path = TestFixtures.RepositoryPath("artifacts", $"hunter-selection-{bases}-{cache}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(measurements));
        Assert.True(perHunter <= 1024, $"{bases} bases: {perHunter:F1} B per additional hunter/tick.");

        long Measure(int hunters)
        {
            var initial = HuntingSnapshot(content);
            var owner = initial.Bases[0];
            var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
            initial = initial with
            {
                Bases = [.. Enumerable.Range(0, bases).Select(baseIndex => owner with
                {
                    Id = baseIndex,
                    Soldiers = [],
                    Crafts = [.. Enumerable.Range(0, 16).Select(index => template with
                    {
                        RuleId = index % 2 == 0 ? "HUNT_SLOW" : "HUNT_FAST",
                        Id = baseIndex * 16 + index + 1,
                        Logistics = template.Logistics! with { Weapons = [] },
                    })],
                })],
                World = initial.World with
                {
                    Missions = [initial.World.Missions[0] with { LiveUfos = hunters }],
                    Ufos = [.. Enumerable.Range(0, hunters).Select(index => initial.World.Ufos[0] with
                    {
                        UniqueId = 9 + index, Id = 3 + index, HuntMode = index % 2,
                        Speed = 0, SpeedRadian = 0, SpeedLongitude = 0, SpeedLatitude = 0,
                    })],
                },
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            var start = Stopwatch.GetTimestamp();
            var before = GC.GetAllocatedBytesForCurrentThread();
            AdvanceUnblocked(campaign, 1);
            var coldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var coldMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            AdvanceUnblocked(campaign, 3);
            start = Stopwatch.GetTimestamp();
            before = GC.GetAllocatedBytesForCurrentThread();
            var result = campaign.Execute(new AdvanceCampaignTime(ticks) { SelectedSpeed = CampaignTimeSpeed.FiveSeconds });
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.Equal(ticks, Assert.Single(result.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
            var after = campaign.Capture();
            Assert.Equal(hunters, after.World.Ufos.Count);
            Assert.Equal(bases * 16, after.Bases.Sum(baseState => baseState.Crafts.Count));
            foreach (var ufo in after.World.Ufos)
            {
                Assert.True(ufo.Hunting);
                Assert.Equal(ufo.HuntMode == 0 ? "HUNT_FAST" : "HUNT_SLOW", ufo.Destination!.TypeId);
                Assert.Equal(ufo.HuntMode == 0 ? 2 : 1, ufo.Destination.Id);
            }
            campaign = Reload(content, campaign, 81, "populated-hunter-selection.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.All(campaign.Capture().World.Ufos, ufo => Assert.True(ufo.Hunting));
            measurements.Add(new { hunters, bases, crafts = bases * 16, ticks, coldBytes, coldMs, bytes, elapsed });
            return bytes;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedHunterModesReplaceCandidatesBetweenActorsTicksAndReloads(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(cache, "crafts: [{type: HUNT_SLOW, speedMax: 2000, damageMax: 100, fuelMax: 300}, {type: HUNT_FAST, speedMax: 6000, damageMax: 100, fuelMax: 300}]\n");
        var initial = HuntingSnapshot(content);
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        initial = initial with
        {
            Bases = [owner with { Soldiers = [], Crafts = [template with { RuleId = "HUNT_SLOW", Id = 1,
                Logistics = template.Logistics! with { Weapons = [] } }, template with { RuleId = "HUNT_FAST", Id = 2,
                Logistics = template.Logistics! with { Weapons = [] } }] }, .. initial.Bases.Skip(1)],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 3 }],
                Ufos = [.. Enumerable.Range(0, 3).Select(index => initial.World.Ufos[0] with
                    { UniqueId = 9 + index, Id = 3 + index, HuntMode = index % 2 })],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        int[] expectedTargets = [2, 1, 2];
        for (var tick = 0; tick < 6; tick++)
        {
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(expectedTargets, after.World.Ufos.Select(ufo => ufo.Destination!.Id));
            Assert.All(after.World.Ufos, ufo => Assert.True(ufo.Hunting));
            if (tick == 2) campaign = Reload(content, campaign, 81, "mixed-hunter-modes.sav");
        }
        Assert.DoesNotContain(campaign.Execute(new RecallCraft(owner.Id, "HUNT_FAST", 2)).Events,
            item => item is CampaignActionBlocked);
        var recalled = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FAST").Logistics!;
        Assert.False(recalled.MissionComplete);
        AdvanceUnblocked(campaign, 1);
        // Manual recall does not set the native HK ignore flags. The current target
        // remains selected, but its coordinates must come from this pass.
        Assert.Equal(expectedTargets, campaign.Capture().World.Ufos.Select(ufo => ufo.Destination!.Id));
        var moved = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FAST").Logistics!;
        Assert.NotEqual(new WorldPosition(recalled.Longitude, recalled.Latitude), new(moved.Longitude, moved.Latitude));
        Assert.Equal(new WorldPosition(moved.Longitude, moved.Latitude),
            campaign.Capture().World.Ufos[0].Destination!.Position);
        Reload(content, campaign, 81, "mixed-hunter-recall.sav");
    }
}
