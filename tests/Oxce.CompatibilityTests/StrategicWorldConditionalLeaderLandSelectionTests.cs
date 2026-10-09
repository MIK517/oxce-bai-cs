using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldConditionalLeaderLandSelectionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ConditionalAirborneArrivalPreservesBothNativeLandSelections(bool cache, bool selected, bool permitted)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "landRectangleOriginRows"), row =>
            row[0].GetInt32() == 1 && row[6].GetInt32() == 304);
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, selected, permitted);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("CONDITIONAL_LAND")].Value;
        var globe = content.RuntimeRules.Campaign.Globe;
        HashSet<int> patterns = [];
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = Snapshot(content, seed, selected);
            var random = new SplitMix64RandomSource(seed);
            WorldPosition Land() => selected ? WorldGeometry.MissionSiteLandPoint(region, globe, 5, 1, permitted ? 100 : 0, random) :
                WorldGeometry.LandPoint(region, globe, 5, permitted ? 100 : 0, random);
            var origins = new WorldPosition[3];
            var hunters = new bool[3];
            for (var wave = 0; wave < 3; wave++)
            {
                hunters[wave] = random.NextInclusive(0, 99) < 50;
                if (hunters[wave])
                {
                    random.NextInclusive(0, 1);
                    random.NextInclusive(0, 1);
                }
                origins[wave] = selected ? Land() : WorldGeometry.RandomPoint(region, 5, -1, random);
                Land();
                if (wave < 2) WorldTrajectory.SpawnCountdown(0, random);
            }
            for (var actor = 0; actor < 4; actor++) random.NextInclusive(0, 99);
            var first = Array.FindIndex(hunters, hunter => !hunter);
            WorldPosition? replacement = null;
            if (first < 0)
            {
                Land();
                replacement = Land();
            }
            var replacements = new WorldPosition[3];
            for (var wave = 0; wave < 3; wave++)
            {
                Land();
                replacements[wave] = Land();
            }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal((seed, random.State), (seed, after.RandomState));
            var source = after.World.Ufos[0];
            Assert.Equal(first >= 0, source.Escorting);
            Assert.Equal(UfoStatus.Flying, source.Status);
            Assert.Equal(first >= 0 ? 0 : 1, source.TrajectoryPoint);
            if (first >= 0)
            {
                Assert.Equal(after.World.Ufos[first + 1].UniqueId, source.Destination!.UniqueId);
                Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, origins[first], WorldGeometry.RadianSpeed(2200)), source.Position);
            }
            else Assert.Equal(replacement, source.Destination!.Position);
            for (var wave = 0; wave < 3; wave++)
            {
                Assert.Equal(1, after.World.Ufos[wave + 1].TrajectoryPoint);
                Assert.Equal(replacements[wave], after.World.Ufos[wave + 1].Destination!.Position);
            }
            Assert.Equal(4, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(12, after.Regions[0].ActivityAlien[^1]);
            patterns.Add(hunters.Select((hunter, index) => hunter ? 1 << index : 0).Sum());
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "conditional-land-selection.sav");
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LoadedLandRetryWorkIsBoundedBeforeAnyBirthOrArrival(bool cache, bool conditional)
    {
        var content = Content(cache, false, false, polygons: 5000);
        var initial = Snapshot(content, 17, false);
        var source = initial.World.Ufos[0];
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 2, SpawnCountdown = conditional ? 0 : 60 }],
                Ufos = [source, source with { UniqueId = 10, Id = 10 }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO waypoint land selection exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedAndNewbornRetriesShareOneBudget(bool cache)
    {
        var content = Content(cache, false, false, polygons: 280);
        var initial = Snapshot(content, 17, false);
        var control = initial with
        { World = initial.World with { Ufos = [], Missions = [initial.World.Missions[0] with { LiveUfos = 0 }] } };
        AdvanceUnblocked(CampaignState.Restore(control, content, new SplitMix64RandomSource(17)), 1);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission land selection exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalCityLookaheadPreservesTheNativeRetryBypass(bool cache)
    {
        var polygons = string.Join("\n", Enumerable.Repeat("    - [10, 0, -10, 20, -10, 20, 10, 0, 10]", 5000));
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, hunterPercentage: 50,
            nextAltitude: 0, groundTimer: 2, extraRulesAfterOverlays: true,
            extraRules: "globe:\n  polygons:\n" + polygons);
        var initial = StrategicWorldConditionalLeaderArrivalTests.Snapshot(content, 17);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.All(after.World.Ufos, ufo => Assert.Equal(UfoStatus.Flying, ufo.Status));
        campaign = Reload(content, campaign, 17, "conditional-city-lookahead.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
    }

    internal static RuntimeContent Content(bool cache, bool selected, bool permitted, int polygons = 0) =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, hunterPercentage: 50, nextAltitude: 0,
            groundTimer: 2, waveObjective: selected, objectiveOnLandingSite: selected, spawnZone: 5,
            fakeWaterLandingChance: permitted ? 100 : 0, extraRulesAfterOverlays: polygons != 0,
            extraRules: (polygons == 0 ? "" : "globe:\n  polygons:\n" +
                string.Join("\n", Enumerable.Repeat("    - [11, 20, -10, 30, -10, 30, 10, 20, 10]", polygons)) + "\n") + """
            regions:
              - type: CONDITIONAL_LAND
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22, 2, 2]]
                  - [[22, 22.01, 2, 2.01, 11], [22.01, 22.02, 2.01, 2.02, 11]]
            """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool selected)
    {
        var initial = StrategicWorldConditionalLeaderArrivalTests.Snapshot(content, seed);
        var position = new WorldPosition(Degrees(22.1), Degrees(2));
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = "CONDITIONAL_LAND", MissionSiteZoneArea = selected ? 1 : -1 }],
                Ufos = [initial.World.Ufos[0] with
                { Longitude = position.Longitude, Latitude = position.Latitude, Destination = WorldTargetReference.ForWaypoint(0, position) }],
            },
        };
    }
}
