using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldConditionalLeaderSelectedAreaTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SelectedAreaAirborneArrivalUsesTheNativeWavePredicate(bool cache, bool objectiveWave)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, objectiveWave);
        Assert.Equal(3, content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("FIRST_ARRIVAL_MISSION")].Value.Waves.Count);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("SELECTED_REGION")].Value;
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = Snapshot(content, seed);
            if (!objectiveWave) initial = initial with
            { World = initial.World with { Missions = [initial.World.Missions[0] with { MissionSiteZoneArea = 999 }] } };
            var random = new SplitMix64RandomSource(seed);
            var origins = new WorldPosition[3];
            var destinations = new WorldPosition[3];
            var hunters = new bool[3];
            for (var wave = 0; wave < 3; wave++)
            {
                hunters[wave] = random.NextInclusive(0, 99) < 50;
                if (hunters[wave])
                {
                    random.NextInclusive(0, 1);
                    random.NextInclusive(0, 1);
                }
                origins[wave] = objectiveWave ? new(Degrees(8.01), Degrees(2.01)) :
                    WorldGeometry.RandomPoint(region, 5, -1, random);
                destinations[wave] = objectiveWave ? origins[wave] : WorldGeometry.RandomPoint(region, 5, -1, random);
                if (wave < 2) WorldTrajectory.SpawnCountdown(0, random);
            }
            for (var actor = 0; actor < 4; actor++) random.NextInclusive(0, 99);
            var first = Array.FindIndex(hunters, hunter => !hunter);
            var replacement = new WorldPosition(Degrees(8.01), Degrees(2.01));
            if (first < 0 && !objectiveWave)
                for (var call = 0; call < 2; call++) replacement = WorldGeometry.RandomPoint(region, 5, -1, random);
            for (var wave = 0; wave < 3; wave++)
                if (!objectiveWave && WorldGeometry.Distance(origins[wave], destinations[wave]) <= WorldGeometry.RadianSpeed(2200))
                    for (var call = 0; call < 2; call++) WorldGeometry.RandomPoint(region, 5, -1, random);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal((seed, random.State), (seed, after.RandomState));
            var source = after.World.Ufos[0];
            Assert.Equal(first >= 0, source.Escorting);
            Assert.Equal(first >= 0 ? 0 : 1, source.TrajectoryPoint);
            Assert.Equal(first >= 0 ? 2200 : 1760, source.Speed);
            if (first >= 0)
            {
                Assert.Equal(after.World.Ufos[first + 1].UniqueId, source.Destination!.UniqueId);
                Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, origins[first], WorldGeometry.RadianSpeed(2200)), source.Position);
            }
            else
            {
                Assert.Equal(initial.World.Ufos[0].Position, source.Position);
                Assert.Equal(replacement, source.Destination!.Position);
            }
            Assert.Equal(4, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(12, after.Regions[0].ActivityAlien[^1]);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "conditional-selected-area.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, bool objectiveWave, bool landingSite = false) =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, hunterPercentage: 50,
            waveObjective: objectiveWave, objectiveOnLandingSite: landingSite, spawnZone: 5, extraRules: """
            regions:
              - type: SELECTED_REGION
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[8, 8, 2, 2]]
                  - [[10, 10, 8, 8]]
                  - [[8, 8.005, 2, 2.005], [8.01, 8.015, 2.01, 2.015]]
            """);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumedMissingSelectedAreaFailsBeforeAnyMutation(bool cache)
    {
        var content = Content(cache, true);
        var initial = Snapshot(content, 17);
        initial = initial with
        { World = initial.World with { Missions = [initial.World.Missions[0] with { MissionSiteZoneArea = 2 }] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var error = Assert.Throws<InvalidDataException>(() => campaign.Execute(new AdvanceCampaignTime(1)));
        Assert.Equal("Mission zone 5 has no area 2.", error.Message);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed)
    {
        var initial = StrategicWorldConditionalLeaderArrivalTests.Snapshot(content, seed);
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = "SELECTED_REGION", MissionSiteZoneArea = 1 }],
            },
        };
    }
}
