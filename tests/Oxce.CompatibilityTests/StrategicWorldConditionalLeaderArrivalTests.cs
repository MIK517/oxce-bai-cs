using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldConditionalLeaderArrivalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossiblePointLeadersOrSafeOldAirborneArrivalPreserveNativeOrder(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = Content(cache);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        HashSet<int> patterns = [];
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = Snapshot(content, seed);
            var random = new SplitMix64RandomSource(seed);
            var origins = new WorldPosition[3];
            var destinations = new WorldPosition[3];
            var hunters = new bool[3];
            var pattern = 0;
            for (var wave = 0; wave < 3; wave++)
            {
                hunters[wave] = random.NextInclusive(0, 99) < 50;
                if (hunters[wave])
                {
                    pattern |= 1 << wave;
                    random.NextInclusive(0, 1);
                    random.NextInclusive(0, 1);
                }
                origins[wave] = WorldGeometry.RandomPoint(region, 5, -1, random);
                destinations[wave] = WorldGeometry.RandomPoint(region, 5, -1, random);
                if (wave < 2) WorldTrajectory.SpawnCountdown(0, random);
            }
            for (var actor = 0; actor < 4; actor++) random.NextInclusive(0, 99);
            var first = Array.FindIndex(hunters, hunter => !hunter);
            if (first < 0)
                for (var call = 0; call < 2; call++) WorldGeometry.RandomPoint(region, 5, -1, random);
            for (var wave = 0; wave < 3; wave++)
                if (WorldGeometry.Distance(origins[wave], destinations[wave]) <= WorldGeometry.RadianSpeed(hunters[wave] ? 550 : 2200))
                    for (var call = 0; call < 2; call++) WorldGeometry.RandomPoint(region, 5, -1, random);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(random.State, after.RandomState);
            Assert.Equal(4, after.World.Ufos.Count);
            Assert.Equal(4, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(12, after.Regions[0].ActivityAlien[^1]);
            var source = after.World.Ufos[0];
            Assert.Equal(first >= 0, source.Escorting);
            Assert.False(source.Hunting);
            Assert.Equal(UfoStatus.Flying, source.Status);
            Assert.Equal(first >= 0 ? 0 : 1, source.TrajectoryPoint);
            Assert.Equal(first >= 0 ? 2200 : 1760, source.Speed);
            if (first >= 0)
            {
                Assert.Equal(after.World.Ufos[first + 1].UniqueId, source.Destination!.UniqueId);
                Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, origins[first], WorldGeometry.RadianSpeed(2200)), source.Position);
                Assert.Equal(initial.World.Ufos[0].Destination!.Position, source.OriginalWaypoint);
            }
            else
            {
                Assert.Equal(WorldTargetKind.Waypoint, source.Destination!.Kind);
                Assert.Equal(initial.World.Ufos[0].Position, source.Position);
                Assert.Null(source.OriginalWaypoint);
            }
            patterns.Add(pattern);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "conditional-leader-arrival.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(8, patterns.Count);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ConditionalAttachmentRetainsFarPursuitAndCloseCombatBoundaries(bool cache, bool close, bool takeoff)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, "crafts: [{type: SHIP, radarRange: 20000, radarChance: 100}]\n");
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = Snapshot(content, seed);
            var source = initial.World.Ufos[0] with { Detected = true };
            var from = close ? source.Position : new WorldPosition(1, 0.6);
            initial = initial with
            {
                Bases = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed).Bases,
                World = initial.World with { Ufos = [source] },
            };
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Fuel = 1000,
                    Damage = 0,
                    Speed = 6000,
                    Takeoff = takeoff ? 1 : 0,
                    Longitude = from.Longitude,
                    Latitude = from.Latitude,
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, source.Id,
                        source.Longitude, source.Latitude)
                    { UniqueId = source.UniqueId },
                },
            });
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            if (close)
            {
                AssertTimeBlocked(campaign, "Loaded idle escorts require same-tick newborn craft pursuit forecasts.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var escort = after.World.Ufos[0];
            var state = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Equal(after.World.Ufos.Skip(1).Any(ufo => !ufo.HunterKiller), escort.Escorting);
            Assert.True(escort.Detected);
            Assert.Equal(escort.UniqueId, state.Destination!.UniqueId);
            Assert.Equal(escort.Position, state.Destination.Position);
            var expected = takeoff ? from : WorldGeometry.Move(from, escort.Position, WorldGeometry.RadianSpeed(6000));
            Assert.Equal(expected, new(state.Longitude, state.Latitude));
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "regional-leader-pursuit.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, string extraRules = "") =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, hunterPercentage: 50, extraRules: extraRules,
            finalRules: "crafts: [{type: SHIP, fuelMax: 1000}]\n");

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed)
    {
        var initial = StrategicWorldRegionalNewLeaderTests.Snapshot(content, seed);
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = "REGION" }],
                Ufos = [initial.World.Ufos[0] with { TrajectoryPoint = 0 }],
            },
        };
    }
}
