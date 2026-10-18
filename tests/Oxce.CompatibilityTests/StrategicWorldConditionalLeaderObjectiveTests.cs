using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldConditionalLeaderObjectiveTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void ConditionalAirborneArrivalPreservesThinkTimeObjectives(int objective)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "transitionRows"), row =>
            row[0].GetInt32() == objective && row[3].GetInt32() == 0 && row[8].GetInt32() == 1760);
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        foreach (var earth in new[] { false, true })
        {
            var content = StrategicWorldNewbornObjectiveArrivalTests.Content(objective, earth,
                hunterPercentage: 50, waveObjective: objective != 5);
            HashSet<int> patterns = [];
            for (ulong seed = 1; seed <= 64; seed++)
            {
                var initial = StrategicWorldNewbornObjectiveArrivalTests.Snapshot(content, seed, objective);
                var source = StrategicWorldConditionalLeaderArrivalTests.Snapshot(content, seed).World.Ufos[0] with
                {
                    RuleId = "FIRST_OBJECTIVE_UFO",
                    TrajectoryId = "FIRST_OBJECTIVE_AIR",
                };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [] }],
                    World = initial.World with
                    {
                        Missions = [initial.World.Missions[0] with { LiveUfos = 1, MissionSiteZoneArea = -1 }],
                        Ufos = [source],
                    },
                };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var births = after.World.Ufos.Skip(1).ToArray();
                Assert.Equal(3, births.Length);
                var first = Array.FindIndex(births, ufo => !ufo.HunterKiller);
                var escort = after.World.Ufos[0];
                Assert.Equal(first >= 0, escort.Escorting);
                Assert.False(escort.Hunting);
                Assert.Equal(UfoStatus.Flying, escort.Status);
                Assert.Equal(first >= 0 ? 0 : 1, escort.TrajectoryPoint);
                Assert.Equal(first >= 0 ? 2200 : 1760, escort.Speed);
                if (first >= 0)
                {
                    Assert.Equal(births[first].UniqueId, escort.Destination!.UniqueId);
                    Assert.Equal(WorldGeometry.Move(source.Position, new(Degrees(8), Degrees(2)),
                        WorldGeometry.RadianSpeed(2200)), escort.Position);
                }
                else Assert.Equal(source.Position, escort.Position);
                Assert.Equal(4, Assert.Single(after.World.Missions).LiveUfos);
                Assert.Equal(objective is 1 or 2 ? 2 : 1, after.World.AlienBases.Count);
                Assert.Equal(objective == 1, after.Countries[0].NewPact);
                Assert.Equal(objective == 2 ? 19 : 12, after.Regions[0].ActivityAlien[^1]);
                Assert.Empty(after.World.MissionSites);
                patterns.Add(births.Select((ufo, index) => ufo.HunterKiller ? 1 << index : 0).Sum());
                campaign = ReloadRepeatedly(content, campaign, seed, "conditional-objective-escort.sav", 3, after);
                var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 3);
                AdvanceUnblocked(campaign, 2);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            }
            Assert.Equal(8, patterns.Count);
        }
    }
}
