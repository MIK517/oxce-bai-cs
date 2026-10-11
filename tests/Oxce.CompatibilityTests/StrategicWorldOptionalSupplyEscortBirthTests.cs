using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSupplyEscortBirthTests
{
    [Theory]
    [InlineData(false, 50, false)]
    [InlineData(false, 50, true)]
    [InlineData(true, 50, false)]
    [InlineData(true, 50, true)]
    [InlineData(false, 100, false)]
    [InlineData(false, 100, true)]
    [InlineData(true, 100, false)]
    [InlineData(true, 100, true)]
    public void EscortBirthsRetainOptionalLeaderAndIdleAlternativesAfterSourceSuppression(bool target, int percentage, bool onlyEscort)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var kind = onlyEscort ? 7 : 6;
        var content = StrategicWorldOptionalSupplySourceTests.Content(kind, radarRange: 20000, nonObjectiveHunterPercentage: percentage, nonObjectiveEscort: true);
        var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(kind, "CORE", radarRange: 20000, nonObjectiveHunterPercentage: percentage, nonObjectiveEscort: true);
        var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(kind, "", radarRange: 20000, nonObjectiveHunterPercentage: percentage, nonObjectiveEscort: true);
        HashSet<(bool Deleted, bool Hunter)> outcomes = [];
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = StrategicWorldOptionalSupplySourceTests.Snapshot(content, seed, kind);
            if (target) initial = StrategicWorldOptionalSupplyHunterBirthTests.Target(initial, false);
            else initial = initial with { Bases = [initial.Bases[0] with { Crafts = [] }] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(onlyEscort || deleted ? 1 : 2, after.World.Ufos.Count);
            var independent = Assert.Single(after.World.Ufos, ufo => ufo.MissionWaveNumber == (onlyEscort ? 0 : 1));
            Assert.Equal(independent.HunterKiller, independent.Escort);
            Assert.Equal(!onlyEscort && !target && !deleted && independent.HunterKiller, independent.Escorting);
            Assert.Equal(target && independent.HunterKiller, independent.Hunting);
            Assert.Equal(target && independent.HunterKiller ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
            Assert.Equal(deleted, Assert.Single(after.World.Missions).AlienBase is null);
            Assert.False(after.World.Missions[0].Interrupted);
            if (independent.Escorting)
                Assert.Equal(after.World.Ufos[0].UniqueId, independent.Destination!.UniqueId);
            Assert.Equal(independent.HunterKiller && (target || !onlyEscort && !deleted), independent.OriginalWaypoint is not null);
            outcomes.Add((deleted, independent.HunterKiller));
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-space-supply-escort-birth.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(percentage == 100 ? 2 : 4, outcomes.Count);
    }

    [Fact]
    public void PossibleHunterBirthCannotCrossCombatOrMarkerBoundaries()
    {
        var content = StrategicWorldOptionalSupplySourceTests.Content(6, radarRange: 20000, nonObjectiveEscort: true);
        var initial = StrategicWorldOptionalSupplyHunterBirthTests.Target(StrategicWorldOptionalSupplySourceTests.Snapshot(content, 17, 6), true);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
        initial = StrategicWorldOptionalSupplyHunterBirthTests.Target(StrategicWorldOptionalSupplySourceTests.Snapshot(content, 17, 6), false)
            .WithNextId("STR_UFO", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO contact marker IDs are exhausted or collide with a saved UFO.");
    }

}
