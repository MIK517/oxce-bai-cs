using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseAirborneCraftTests
{
    [Theory]
    [InlineData(false, 0, false, false)]
    [InlineData(true, 0, false, false)]
    [InlineData(false, 5, false, false)]
    [InlineData(true, 5, false, false)]
    [InlineData(false, 0, true, false)]
    [InlineData(true, 0, true, false)]
    [InlineData(false, 5, true, false)]
    [InlineData(true, 5, true, false)]
    [InlineData(false, 0, false, true)]
    [InlineData(true, 0, false, true)]
    [InlineData(false, 5, false, true)]
    [InlineData(true, 5, false, true)]
    [InlineData(false, 0, true, true)]
    [InlineData(true, 0, true, true)]
    [InlineData(false, 5, true, true)]
    [InlineData(true, 5, true, true)]
    public void EqualSpeedOptionalReturnsPreserveFuelHuntingAndOrderedCraftEscort(bool cache, int takeoff, bool escort, bool hunter)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache);
        var deletedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "CORE");
        var survivedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "");
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, takeoff, escort, hunter);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var craft = after.Bases[0].Crafts[0].Logistics!;
            Assert.Equal("STR_OUT", craft.Status);
            Assert.Equal(6000, craft.Speed);
            Assert.Equal(940, craft.Fuel);
            Assert.Equal(Math.Max(0, takeoff - 1), craft.Takeoff);
            Assert.Equal(deleted ? WorldTargetKind.Base : WorldTargetKind.AlienBase, craft.Destination!.Kind);
            var target = deleted ? new WorldPosition(after.Bases[0].Longitude, after.Bases[0].Latitude) : new(0.4, 0.2);
            var expected = takeoff > 0 ? new WorldPosition(1, 0.6) : WorldGeometry.Move(new(1, 0.6), target, WorldGeometry.RadianSpeed(6000));
            Assert.Equal(expected, new(craft.Longitude, craft.Latitude));
            if (escort)
            {
                var follower = after.Bases[0].Crafts[1].Logistics!;
                Assert.Equal(WorldTargetKind.Craft, follower.Destination!.Kind);
                Assert.Equal(after.Bases[0].Crafts[0].Id, follower.Destination.Id);
                Assert.Equal(940, follower.Fuel);
                Assert.Equal(WorldGeometry.Move(new(1.0004, 0.6002), expected, WorldGeometry.RadianSpeed(6000)), new(follower.Longitude, follower.Latitude));
            }
            if (hunter)
            {
                var ufo = Assert.Single(after.World.Ufos);
                Assert.True(ufo.Hunting);
                Assert.Equal(after.Bases[0].Crafts[0].Id, ufo.Destination!.Id);
            }
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-airborne-craft.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedSpeedArrivalAndMalformedTakeoffKeepPreciseAtomicBoundaries(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache);
        var initial = Snapshot(content, 17, 0, false, false).WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 3000 } });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft speed alternatives for uncertain research.");
        initial = Snapshot(content, 17, 0, false, false).WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Longitude = 0.2, Latitude = 0.1 } });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft arrival alternatives for uncertain research.");
        initial = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Takeoff = 5 } });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft arrival alternatives for uncertain research.");
        initial = Snapshot(content, 17, -1, false, false);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Airborne craft fuel or takeoff state is out of range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearHomeOptionalReturnBlocksBeforeResearchUsingNativeEquality(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache);
        var initial = Snapshot(content, 17, 5, false, false);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Longitude = Math.BitIncrement(initial.Bases[0].Longitude),
                Latitude = initial.Bases[0].Latitude,
            },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft arrival alternatives for uncertain research.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int takeoff, bool escort, bool hunter)
    {
        var initial = StrategicWorldOptionalBaseCraftTests.Snapshot(content, seed, "STR_OUT").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Speed = 6000, Fuel = 1000, Takeoff = takeoff, Longitude = 1, Latitude = 0.6 },
        });
        if (hunter)
        {
            var source = StrategicWorldOptionalBaseAutoPatrolTests.Snapshot(content, seed, "STR_READY").World;
            initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 1 }], Ufos = source.Ufos } };
        }
        if (escort)
        {
            var leader = initial.Bases[0].Crafts[0];
            var follower = leader with
            {
                Id = 2,
                Logistics = leader.Logistics! with
                {
                    Takeoff = 0,
                    Longitude = 1.0004,
                    Latitude = 0.6002,
                    Destination = new(WorldTargetKind.Craft, "SHIP", leader.Id, 1, 0.6),
                },
            };
            initial = initial with { Bases = [initial.Bases[0] with { Crafts = [leader, follower] }] };
        }
        return initial;
    }
}
