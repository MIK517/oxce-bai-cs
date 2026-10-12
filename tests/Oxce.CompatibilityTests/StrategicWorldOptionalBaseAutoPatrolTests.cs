using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseAutoPatrolTests
{
    [Theory]
    [InlineData("STR_READY")]
    [InlineData("STR_REPAIRS")]
    [InlineData("STR_REARMING")]
    [InlineData("STR_REFUELLING")]
    public void OptionalGroundedReturnComposesRelaunchBeforeActualHunterSelection(string status)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        using var hunting = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.NotEmpty(TestFixtures.Rows(hunting.RootElement, "maintenanceRows"));
        var content = StrategicWorldOptionalBaseCraftTests.Content();
        var deletedContent = StrategicWorldOptionalBaseCraftTests.Content("CORE");
        var survivedContent = StrategicWorldOptionalBaseCraftTests.Content("");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, status);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var launched = status is "STR_REARMING" or "STR_REFUELLING";
            var craft = Assert.Single(after.Bases[0].Crafts).Logistics!;
            var hunter = Assert.Single(after.World.Ufos);
            Assert.Equal(launched, craft.Status == "STR_OUT");
            Assert.Equal(launched, hunter.Hunting);
            Assert.Equal(launched ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
            Assert.True(craft.IsAutoPatrolling);
            Assert.Equal(launched || deleted ? 6000 : 0, craft.Speed);
            Assert.Equal(launched ? 59 : deleted ? 60 : 7, craft.Takeoff);
            if (launched)
            {
                Assert.Equal(WorldTargetKind.Waypoint, craft.Destination!.Kind);
                Assert.Equal(new WorldPosition(1, 0.6), craft.Destination.Position);
                Assert.Equal("SHIP", hunter.Destination!.TypeId);
                Assert.Equal(after.Bases[0].Crafts[0].Id, hunter.Destination.Id);
            }
            else Assert.Equal(deleted ? WorldTargetKind.Base : WorldTargetKind.AlienBase, craft.Destination!.Kind);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-auto-patrol.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Fact]
    public void OptionalReturnKeepsRelaunchIdentityAndHuntingCombatLimitsAtomic()
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content();
        var initial = Snapshot(content, 17, "STR_REFUELLING").WithNextId(WorldTargetReference.WaypointType, int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Waypoint IDs are exhausted or collide with a saved waypoint.");
        initial = Snapshot(content, 17, "STR_REFUELLING").WithNextId("STR_UFO", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        initial = Snapshot(content, 17, "STR_REFUELLING");
        var craft = initial.Bases[0].Crafts[0].Logistics!;
        initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = craft.Longitude, Latitude = craft.Latitude }] } };
        AssertInterceptionStarts(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)));
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, string status)
    {
        var initial = StrategicWorldOptionalBaseCraftTests.Snapshot(content, seed, status).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { IsAutoPatrolling = true, AutoPatrolLongitude = 1, AutoPatrolLatitude = 0.6 },
        });
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 1 }],
                Ufos = [new(9, "OPTIONAL_RETURN_HUNTER", 4, "OPTIONAL_RETURN_AIR", 0, 0.8, 0.4, UfoStatus.Flying, WorldAltitudes.High)
                { Speed = 2200, HunterKiller = true, MissionWaveNumber = 0, Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)) }],
            },
        };
    }
}
