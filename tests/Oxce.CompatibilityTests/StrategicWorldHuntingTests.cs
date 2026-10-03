using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Gameplay.Campaigns;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Content;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldHuntingTests
{
    [Fact]
    public void AttractionMatchesPinnedMethods()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var actual = new List<int>();
        var expected = new List<int>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "attractionRows"))
        {
            Assert.True(WorldHunting.TryAttraction(row[0].GetInt32(), row[1].GetBoolean(), row[2].GetInt32(),
                row[3].GetInt32(), row[4].GetInt32(), 100, out var attraction));
            actual.Add(attraction);
            expected.Add(row[5].GetInt32());
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
    }

    [Fact]
    public void SelectionMatchesPinnedSavedOrderAndOriginalTargetRules()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "selectionRows"))
        {
            var mode = row[0].GetInt32();
            var variant = row[1].GetInt32();
            var a = new WorldTargetReference(WorldTargetKind.Craft, "A", 1, 0, WorldGeometry.Nautical(100));
            var b = new WorldTargetReference(WorldTargetKind.Craft, "B", 2, 0, WorldGeometry.Nautical(100));
            if (variant == 4)
            {
                a = a with { Latitude = WorldGeometry.Nautical(301) };
                b = b with { Latitude = WorldGeometry.Nautical(300) };
            }
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, 2000, 0, 100, out var attractionA));
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, variant == 1 ? 2000 : 6000, 0, 100, out var attractionB));
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Hunting = variant is 1 or 2 or 6, Destination = a };
            WorldHuntCraft[] crafts =
            [
                new(a, a.Position, attractionA, variant is 3 or 6, variant == 2, true),
                new(b, b.Position, attractionB, variant == 2, variant == 3, true),
            ];
            Assert.Equal(row[2].GetInt32(), WorldHunting.Select(ufo, variant == 5 ? 0 : 300, crafts)?.Id ?? 0);
        }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 100, 2147483647)]
    [InlineData(1, 100, 2147483647)]
    public void MalformedAttractionArithmeticIsRejected(int mode, int damageMaximum, int units) =>
        Assert.False(WorldHunting.TryAttraction(mode, true, units, 6000, 50, damageMaximum, out _));
    [Fact]
    public void RetargetTransitionsMatchPinnedBackupSpeedAndRestore()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "retargetRows"))
        {
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Speed = 900, Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)) };
            var target = new WorldTargetReference(WorldTargetKind.Craft, "SHIP", 1, 0.8, 0.3);
            Assert.True(WorldHunting.TryHunt(ufo, target, 2200, row[0].GetInt32(), out var hunted));
            var escort = WorldHunting.Escort(hunted, new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 4, 1, 0.5) { UniqueId = 10 }, 2200);
            var restored = WorldHunting.Restore(escort);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[0].GetInt32(), hunted.Speed, escort.Speed, restored.Destination!.Longitude,
                restored.Destination.Latitude, restored.Hunting, restored.Escorting,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
            Assert.Null(restored.OriginalWaypoint);
            Assert.Equal(2200, restored.Speed);
        }
    }

    [Fact]
    public void ExplicitSpeedContextMatchesPinnedFastCondition()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "fastRows"))
        {
            var speed = (CampaignTimeSpeed)row[1].GetInt32();
            var fast = row[0].GetBoolean();
            Assert.Equal(row[2].GetBoolean(), WorldHunting.RetargetDue(CampaignTimeTrigger.FiveSeconds, speed, fast));
            Assert.True(WorldHunting.RetargetDue(CampaignTimeTrigger.TenMinutes, speed, fast));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RetargetOptionsKeepReferenceDefaultsCacheAndReload(bool cache, bool disabled)
    {
        var overlay = disabled ? "hunterKillerFastRetarget: false\nfixedUserOptions:\n  oxceGeoSuppressRedundantHKAlert: 'false'\n" : null;
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, extraRules: overlay);
        Assert.Equal(!disabled, content.RuntimeRules.Campaign.HunterKillerFastRetarget);
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Hunt settings"), content, new SplitMix64RandomSource(81));
        Assert.Equal(!disabled, campaign.Options.SuppressRedundantHunterAlert);
        Reload(content, campaign, 81, "hunting-settings.sav");
    }

    private static RuntimeContent Load(bool cache = false, string? overlay = null) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, extraRules: overlay);

    private static CampaignSnapshot HuntingSnapshot(RuntimeContent content, bool escort = false)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0),
            ufoRuleId: escort ? "ESCORT_HUNTER" : "HUNTER", speed: 2200).Capture();
        initial = initial with
        {
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = true, Escort = escort }] },
        };
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = 0.45,
                Latitude = 0.21,
                Destination = null,
            },
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveHuntersSelectRevealMoveAndReload(bool cache)
    {
        var content = Load(cache);
        var initial = HuntingSnapshot(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(10));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var alert = Assert.Single(result.Events.OfType<UfoStartedHunting>());
        Assert.Equal(9, alert.UniqueId);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.True(ufo.Hunting);
        Assert.True(ufo.Detected);
        Assert.False(ufo.Escorting);
        Assert.Equal(new WorldPosition(1, 0.6), ufo.OriginalWaypoint);
        Assert.Equal(WorldTargetKind.Craft, ufo.Destination!.Kind);
        Assert.Equal(3300, ufo.Speed);
        Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, new(0.45, 0.21), WorldGeometry.RadianSpeed(3300)), ufo.Position);
        var restored = Reload(content, campaign, 81, "live-hunter.sav");
        AdvanceOne(restored);
        Assert.True(Assert.Single(restored.Capture().World.Ufos).Hunting);
    }

    [Theory]
    [InlineData(CampaignTimeSpeed.FiveSeconds, true, false, true)]
    [InlineData(CampaignTimeSpeed.OneMinute, true, false, true)]
    [InlineData(CampaignTimeSpeed.FiveMinutes, true, false, false)]
    [InlineData(CampaignTimeSpeed.FiveSeconds, false, false, false)]
    [InlineData(CampaignTimeSpeed.OneDay, false, true, true)]
    public void LiveRetargetingUsesSelectedSpeedAndTenMinuteCaller(CampaignTimeSpeed speed, bool fast, bool tenMinutes, bool hunts)
    {
        var content = Load(overlay: fast ? null : "hunterKillerFastRetarget: false\n");
        var initial = HuntingSnapshot(content);
        if (tenMinutes) initial = initial with { Time = new(1, 1, 1, 1999, 1, 9, 55) };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(hunts ? 10 : 3) { SelectedSpeed = speed });
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(hunts ? 1 : 3, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(hunts, Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Assert.Equal(hunts ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
        Reload(content, campaign, 81, "speed-selected-hunter.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortsFollowUfoListOrderAndRestoreAfterDeletion(bool targetFirst)
    {
        var content = Load();
        var initial = HuntingSnapshot(content, escort: true);
        var hunter = initial.World.Ufos[0];
        var target = hunter with
        { UniqueId = 10, Id = 4, RuleId = "UFO_SCOUT", HunterKiller = false, Escort = false, Longitude = 0.5, Latitude = 0.25 };
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = targetFirst ? [target, hunter] : [hunter, target],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var escorted = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
        var targetPosition = targetFirst ? after.World.Ufos.Single(ufo => ufo.UniqueId == 10).Position : target.Position;
        Assert.True(escorted.Escorting);
        Assert.False(escorted.Hunting);
        Assert.Equal(10, escorted.Destination!.UniqueId);
        Assert.Equal(WorldGeometry.Move(hunter.Position, targetPosition, WorldGeometry.RadianSpeed(2200)), escorted.Position);
        var restored = Reload(content, campaign, 81, "live-escort.sav");
        var deleted = restored.Capture();
        deleted = deleted with
        {
            World = deleted.World with
            {
                Ufos =
            deleted.World.Ufos.Select(ufo => ufo.UniqueId == 10 ? ufo with { Status = UfoStatus.Destroyed } : ufo).ToArray()
            }
        };
        var cleanup = CampaignState.Restore(deleted, content, new SplitMix64RandomSource(81));
        AdvanceOne(cleanup);
        var survivor = Assert.Single(cleanup.Capture().World.Ufos);
        Assert.False(survivor.Escorting);
        Assert.Equal(new WorldPosition(1, 0.6), survivor.Destination!.Position);
        Assert.Null(survivor.OriginalWaypoint);
        Reload(content, cleanup, 81, "deleted-escort-target.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HunterInterceptionAndMarkerExhaustionAreAtomic(bool marker)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with { Id = marker ? 0 : 3, Longitude = marker ? 0.4 : 0.45, Latitude = marker ? 0.2 : 0.21 }]
            }
        };
        if (marker) initial = initial.WithNextId("STR_UFO", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), marker
            ? "UFO contact marker IDs are exhausted or collide with a saved UFO."
            : "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedundantHunterAlertsFollowTheOption(bool suppress)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with { Options = initial.Options with { SuppressRedundantHunterAlert = suppress } };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 }
            }
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(suppress ? 0 : 1, result.Events.OfType<UfoStartedHunting>().Count());
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "redundant-hunt-alert.sav");
    }

    [Fact]
    public void EscortCyclesStopBeforeRecursiveMovementForecasts()
    {
        var content = Load();
        var initial = HuntingSnapshot(content, escort: true);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    Escorting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
                }],
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort chains require additional movement forecasts.");
    }

    [Fact]
    public void IgnoredOriginalCraftRestoresThePreparedWaypoint()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { LowFuel = true } });
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    Hunting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
                }],
            }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.False(ufo.Hunting);
        Assert.Null(ufo.OriginalWaypoint);
        Assert.Equal(new WorldPosition(1, 0.6), ufo.Destination!.Position);
        Reload(content, campaign, 81, "ignored-hunt-target.sav");
    }

    [Fact]
    public void DestroyedFollowerCleanupIsBoundedBeforeItsNestedScan()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 1001 }],
                Ufos = Enumerable.Range(0, 1001).Select(index => initial.World.Ufos[0] with
                { UniqueId = 9 + index, Id = 3 + index, HunterKiller = false, Status = UfoStatus.Destroyed }).ToArray(),
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Destroyed UFO follower cleanup exceeds the supported work limit.");
    }

    [Fact]
    public void HunterRevelationCannotBypassAnImportedCraftInterceptionGuard()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Longitude = 0.403,
                Latitude = 0.2,
                Speed = 6000,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
            }
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(2147000, 2000000000, 0, 100)]
    [InlineData(0, 2000, 21474837, 2147483647)]
    public void UndefinedIntermediateAttractionOverflowIsRejected(int units, int speed, int damage, int damageMaximum) =>
        Assert.False(WorldHunting.TryAttraction(0, true, units, speed, damage, damageMaximum, out _));

}
