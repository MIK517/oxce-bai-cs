using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldReturnedCraftHuntingTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 5)]
    [InlineData(true, true, 5)]
    public void ArrivalStopsOnlyMatchingHuntersWithoutWaitingForRetarget(bool cache, bool disableFast, int takeoff)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache, disableFast);
        var initial = Snapshot(content, takeoff);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var speed = disableFast ? CampaignTimeSpeed.FiveSeconds : CampaignTimeSpeed.FiveMinutes;
        var result = campaign.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = speed });
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.Single(result.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        Assert.Empty(result.Events.OfType<UfoStartedHunting>());
        var after = campaign.Capture();
        var craft = after.Bases[0].Crafts[0].Logistics!;
        Assert.Equal("STR_READY", craft.Status);
        Assert.Null(craft.Destination);
        Assert.Equal(0, craft.Speed);
        Assert.Equal(0, craft.Takeoff);
        foreach (var ufo in after.World.Ufos)
        {
            var match = ufo.UniqueId == 9;
            var expected = TestFixtures.Rows(oracle.RootElement, "returnedCraftHuntRows").Single(row => row[0].GetBoolean() == match && row[1].GetBoolean());
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                match, true, false, ufo.Hunting, ufo.Escorting, ufo.OriginalWaypoint is not null,
                ufo.Destination!.Kind == WorldTargetKind.Craft, ufo.Speed,
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            var target = match ? new WorldPosition(0.2, 0.1) : new(1.5, 0.7);
            var moved = WorldGeometry.Move(initial.World.Ufos.Single(before => before.UniqueId == ufo.UniqueId).Position,
                target, WorldGeometry.RadianSpeed(3300));
            Assert.Equal(moved, ufo.Position);
            if (match)
            {
                Assert.Equal(new WorldPosition(1, 0.6), ufo.Destination.Position);
                var vector = WorldGeometry.SpeedVector(moved, ufo.Destination.Position, WorldGeometry.RadianSpeed(3300));
                Assert.Equal(vector.Longitude, ufo.SpeedLongitude);
                Assert.Equal(vector.Latitude, ufo.SpeedLatitude);
            }
            else Assert.Equal(2, ufo.Destination.Id);
        }
        var uninterrupted = CampaignState.Restore(after, content, new SplitMix64RandomSource(81));
        for (var rewrite = 0; rewrite < 3; rewrite++)
        {
            campaign = Reload(content, campaign, 81, "returned-craft-hunting.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        campaign.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = speed });
        uninterrupted.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = speed });
        Assert.Equivalent(uninterrupted.Capture(), campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonArrivalRetainsHuntingAndCorruptOriginalRemainsAtomic(bool cache)
    {
        var content = Content(cache, true);
        var initial = Snapshot(content, 5);
        var ship = initial.Bases[0].Crafts[0];
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Crafts = [ship with { Logistics = ship.Logistics! with { Longitude = 0.21 } }, initial.Bases[0].Crafts[1]],
            }],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceUnblocked(campaign, 1);
        Assert.True(campaign.Capture().World.Ufos[0].Hunting);
        initial = Snapshot(content, 0);
        initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { OriginalWaypoint = null }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting or escort state requires a valid original waypoint.");
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with { Status = UfoStatus.IgnoreMe, HunterKiller = false }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting or escort state requires a valid original waypoint.");
    }

    internal static RuntimeContent Content(bool cache, bool disableFast) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache,
            "crafts: [{type: SHIP, fuelMax: 1000}]\n" + (disableFast ? "hunterKillerFastRetarget: false\n" : ""), extraRulesAfterOverlays: true);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, int takeoff)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0), "HUNTER", 3300).Capture();
        var owner = initial.Bases[0];
        var ship = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        ship = ship with
        {
            Logistics = ship.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 1000,
                Speed = 6000,
                Takeoff = takeoff,
                Longitude = owner.Longitude,
                Latitude = owner.Latitude,
                LowFuel = false,
                MissionComplete = false,
                Destination = new(WorldTargetKind.Base, WorldTargetReference.BaseType, owner.Id, owner.Longitude, owner.Latitude),
            },
        };
        var other = ship with { Id = 2, Logistics = ship.Logistics! with { Longitude = 1.5, Latitude = 0.7, Destination = null, Takeoff = 0 } };
        var hunter = initial.World.Ufos[0] with
        {
            HunterKiller = true,
            Hunting = true,
            OriginalWaypoint = new(1, 0.6),
            Destination = new(WorldTargetKind.Craft, "SHIP", 1, owner.Longitude, owner.Latitude),
        };
        var unrelated = hunter with { Id = 4, UniqueId = 10, Destination = new(WorldTargetKind.Craft, "SHIP", 2, 1.5, 0.7) };
        return initial with
        {
            Bases = [owner with { Crafts = [ship, other], Soldiers = [], Transfers = [], Productions = [], Research = [] }],
            World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 2 }], Ufos = [hunter, unrelated] },
        };
    }
}
