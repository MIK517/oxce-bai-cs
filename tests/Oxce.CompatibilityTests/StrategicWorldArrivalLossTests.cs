using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldArrivalLossTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ArrivalLossPrecedesTerminalReturnAndNextTickPursuitRepair(bool cache, bool terminal, bool escortFollower)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = StrategicWorldAirborneResearchTests.Content(cache);
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldAirborneResearchTests.Snapshot(content, seed).WithFacility("RADAR_TEST");
            var ufo = initial.World.Ufos[0] with { Detected = true, TrajectoryPoint = terminal ? 2 : 0 };
            var target = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id,
                ufo.Longitude, ufo.Latitude)
            { UniqueId = ufo.UniqueId };
            var later = initial.World.Ufos[1];
            if (escortFollower)
                later = later with
                { HunterKiller = true, Escort = true, Escorting = true, OriginalWaypoint = later.Destination!.Position, Destination = target };
            else initial = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Destination = target } });
            initial = initial with { World = initial.World with { Ufos = [ufo, later] } };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            var destroyed = terminal || interrupted;
            Assert.Equal(destroyed ? UfoStatus.Destroyed : UfoStatus.Flying, after.World.Ufos[0].Status);
            if (destroyed) Assert.Equal(new UfoContactLost(ufo.UniqueId), Assert.Single(result.Events.OfType<UfoContactLost>()));
            else Assert.Empty(result.Events.OfType<UfoContactLost>());
            Assert.Empty(result.Events.OfType<CraftTargetLost>());
            if (destroyed)
            {
                Assert.Equal(initial.World.Ufos[1].Position, after.World.Ufos[1].Position);
                Assert.Equal(initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude,
                    after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude);
            }
            for (var pass = 0; pass < 3; pass++)
                campaign = Reload(content, campaign, seed, "arrival-loss.sav");
            if (destroyed)
            {
                var followup = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(followup.Events.OfType<CampaignActionBlocked>());
                Assert.Empty(followup.Events.OfType<UfoContactLost>());
                Assert.Equal(!escortFollower, followup.Events.OfType<CraftTargetLost>().Any());
                Assert.Single(campaign.Capture().World.Ufos);
                Reload(content, campaign, seed, "arrival-loss-repaired.sav");
            }
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void HiddenOrUnfollowedTerminalContactsDoNotReportArrivalLoss(bool cache, bool detected, bool followed)
    {
        var content = StrategicWorldAirborneResearchTests.Content(cache);
        var initial = StrategicWorldAirborneResearchTests.Snapshot(content, 17) with { Time = new(2, 2, 1, 1999, 0, 30, 5) };
        var ufo = initial.World.Ufos[0] with { Detected = detected, TrajectoryPoint = 2 };
        initial = initial with { World = initial.World with { Ufos = [ufo, initial.World.Ufos[1]] } };
        if (followed)
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id, ufo.Longitude, ufo.Latitude)
                    { UniqueId = ufo.UniqueId },
                },
            });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Empty(result.Events.OfType<UfoContactLost>());
        Assert.Empty(result.Events.OfType<CraftTargetLost>());
        Assert.Equal(UfoStatus.Destroyed, campaign.Capture().World.Ufos[0].Status);
        Reload(content, campaign, 17, "silent-arrival-loss.sav");
    }
}
