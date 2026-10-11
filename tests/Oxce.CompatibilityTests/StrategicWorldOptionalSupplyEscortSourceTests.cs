using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSupplyEscortSourceTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    public void OptionalObjectiveLeadersPreserveBothSavedEscortFlightBranches(bool arrival, int point)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicWorldOptionalSupplySourceTests.Content(0);
        var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(0, "CORE");
        var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(0, "");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, arrival);
            initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { TrajectoryPoint = point }] } };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var source = after.World.Ufos[0];
            Assert.Equal(deleted ? 1 : 2, after.World.Ufos.Count);
            Assert.Equal(after.World.Ufos.Count, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(deleted, after.World.Missions[0].AlienBase is null);
            Assert.False(after.World.Missions[0].Interrupted);
            Assert.Equal(!deleted, source.Escorting);
            Assert.True(source.Escort);
            Assert.False(source.Hunting);
            Assert.Equal(UfoStatus.Flying, source.Status);
            Assert.Equal(deleted && arrival ? 1 : point, source.TrajectoryPoint);
            Assert.Equal(deleted && arrival ? 1760 : 2200, source.Speed);
            var origin = new WorldPosition(Degrees(10), Degrees(8));
            var destination = deleted ? initial.World.Ufos[0].Destination!.Position : origin;
            Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, destination, WorldGeometry.RadianSpeed(2200)), source.Position);
            if (deleted)
            {
                Assert.Equal(WorldTargetKind.Waypoint, source.Destination!.Kind);
                Assert.Null(source.OriginalWaypoint);
            }
            else
            {
                Assert.Equal(after.World.Ufos[1].UniqueId, source.Destination!.UniqueId);
                Assert.Equal(initial.World.Ufos[0].Destination!.Position, source.OriginalWaypoint);
            }
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-supply-escort-source.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OptionalAttachmentCannotHideConsumedNegativeIdleSpeed(int point)
    {
        var content = StrategicWorldOptionalSupplySourceTests.Content(0);
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, false);
            initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { TrajectoryPoint = point, Speed = -1 }] } };
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), "UFO speed is invalid.");
        }
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool arrival)
    {
        var initial = StrategicWorldOptionalSupplySourceTests.Snapshot(content, seed);
        var source = new UfoSnapshot(9, "OPTIONAL_SUPPLY_UFO", 4, "OPTIONAL_SUPPLY_AIR", 0,
            0.4, 0.2, UfoStatus.Flying, WorldAltitudes.High)
        {
            Id = 9,
            Speed = 2200,
            HunterKiller = true,
            Escort = true,
            MissionWaveNumber = 0,
            Destination = WorldTargetReference.ForWaypoint(0, arrival ? new(0.4, 0.2) : new(1, 0.6)),
        };
        return initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [] }],
            World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 1 }], Ufos = [source] },
        };
    }

    [Fact]
    public void GuaranteedReplacementDoesNotConsumeNegativeOldSpeed()
    {
        var content = StrategicWorldOptionalSupplySourceTests.Content(2);
        var deletedContent = StrategicWorldOptionalSupplySourceTests.Content(2, "CORE");
        var survivedContent = StrategicWorldOptionalSupplySourceTests.Content(2, "");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, false);
            initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { Speed = -1 }] } };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var source = after.World.Ufos[0];
            Assert.True(source.Escorting);
            Assert.Equal(2200, source.Speed);
            Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, new(Degrees(10), Degrees(8)), WorldGeometry.RadianSpeed(2200)), source.Position);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-supply-guaranteed-speed.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }
}
