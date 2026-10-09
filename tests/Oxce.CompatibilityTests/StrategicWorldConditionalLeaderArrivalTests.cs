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

    internal static RuntimeContent Content(bool cache, string extraRules = "") =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, hunterPercentage: 50, extraRules: extraRules);

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
