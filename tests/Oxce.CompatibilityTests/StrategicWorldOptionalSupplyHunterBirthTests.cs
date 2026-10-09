using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSupplyHunterBirthTests
{
    [Theory]
    [InlineData(false, false, 50)]
    [InlineData(true, false, 50)]
    [InlineData(false, true, 50)]
    [InlineData(true, true, 50)]
    [InlineData(false, false, 100)]
    [InlineData(true, false, 100)]
    [InlineData(false, true, 100)]
    [InlineData(true, true, 100)]
    public void IndependentHunterBirthsRemainExecutableAfterOptionalObjectiveSuppression(bool cache, bool target, int percentage)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = StrategicWorldOptionalSupplySourceTests.Content(cache, 6, radarRange: 20000, nonObjectiveHunterPercentage: percentage);
        var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, 6, "CORE", radarRange: 20000, nonObjectiveHunterPercentage: percentage);
        var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(cache, 6, "", radarRange: 20000, nonObjectiveHunterPercentage: percentage);
        HashSet<(bool Deleted, bool Hunter)> outcomes = [];
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = StrategicWorldOptionalSupplySourceTests.Snapshot(content, seed, 6);
            if (target) initial = Target(initial, false);
            else initial = initial with { Bases = [initial.Bases[0] with { Crafts = [] }] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(deleted ? 1 : 2, after.World.Ufos.Count);
            var independent = Assert.Single(after.World.Ufos, ufo => ufo.MissionWaveNumber == 1);
            Assert.False(independent.Escort);
            Assert.False(independent.Escorting);
            Assert.Equal(target && independent.HunterKiller, independent.Hunting);
            Assert.Equal(target && independent.HunterKiller ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
            Assert.Equal(deleted, Assert.Single(after.World.Missions).AlienBase is null);
            Assert.False(after.World.Missions[0].Interrupted);
            outcomes.Add((deleted, independent.HunterKiller));
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-space-supply-hunter-birth.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(percentage == 100 ? 2 : 4, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleHunterBirthCannotCrossCombatOrMarkerBoundaries(bool cache)
    {
        var content = StrategicWorldOptionalSupplySourceTests.Content(cache, 6, radarRange: 20000);
        var initial = Target(StrategicWorldOptionalSupplySourceTests.Snapshot(content, 17, 6), true);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
        initial = Target(StrategicWorldOptionalSupplySourceTests.Snapshot(content, 17, 6), false)
            .WithNextId("STR_UFO", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO contact marker IDs are exhausted or collide with a saved UFO.");
    }

    private static CampaignSnapshot Target(CampaignSnapshot initial, bool close)
    {
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Damage = 0,
                Fuel = 1000,
                Speed = 6000,
                Takeoff = 0,
                Longitude = close ? Degrees(10) : 1,
                Latitude = close ? Degrees(8) : 0.6,
                Destination = WorldTargetReference.ForWaypoint(1, new(2, 0.7)),
            },
        });
        return initial with { World = initial.World with { Waypoints = [new(1, 2, 0.7)] } };
    }
}
