using System.Globalization;
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

public sealed class StrategicWorldUfoSiteConversionTests
{
    private static RuntimeContent Load(bool cache = false, string? overlay = null) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-site-spawn.rul", "strategic-ufo-site-conversion.rul"], cache, extraRules: overlay);

    private static CampaignSnapshot Snapshot(RuntimeContent content, bool landing = false, bool custom = false, bool respawn = false)
    {
        var snapshot = CreateSnapshot(content, "UFO site conversion");
        return snapshot.WithNextId("DIRECT_MARKER", 7) with
        {
            Time = new(1, 1, 1, 1999, 1, 0, 0),
            RandomState = 0,
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, respawn ? landing ? "UFO_SITE_RESPAWN_LANDING" : "UFO_SITE_RESPAWN" :
                    landing ? "UFO_SITE_AT_LANDING" : "UFO_SITE_CONVERSION",
                    "SITE_SPAWN_REGION", "RACE_A", 1, 0, 1500, 1, 0)],
                Ufos = [new UfoSnapshot(9, custom ? "UFO_SITE_CUSTOM" : "UFO_SCOUT", 4, "UFO_SITE_CONVERSION", 0, 0.25, 0.15,
                    UfoStatus.Flying, WorldAltitudes.Low)
                {
                    Speed = 2200, MissionWaveNumber = 0, Detected = true, Id = 3,
                    Destination = WorldTargetReference.ForWaypoint(0, new(0.25, 0.15)),
                }],
            },
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedObjectiveConversionMatchesCapturedSiteAndStopsFollowingTicks(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        var content = Load(cache);
        var count = 0;
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "conversionRows"))
        {
            count++;
            var landing = row[0].GetBoolean();
            var respawn = row[1].GetBoolean();
            var initial = Snapshot(content, landing, row[2].GetBoolean(), respawn);
            var campaign = CampaignState.Restore(initial, content, new QuarterChoices());
            var result = campaign.Execute(new AdvanceCampaignTime(10));
            Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
            Assert.IsType<MissionSiteDetected>(result.Events[1]);
            var after = campaign.Capture();
            var site = Assert.Single(after.World.MissionSites);
            var ufo = Assert.Single(after.World.Ufos);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                site.Longitude.ToString("F9", CultureInfo.InvariantCulture),
                site.Latitude.ToString("F9", CultureInfo.InvariantCulture), site.SecondsRemaining,
                after.RandomState - (landing ? 402UL : 0), 1, site.CustomDeploymentId,
                site.UfoUniqueId != 0, ufo.Status == UfoStatus.IgnoreMe ? "ignore" : "destroyed",
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Assert.True(site.Detected);
            Assert.Equal(7, site.Id);
            Assert.Equal("CITY", site.City);
            Assert.Equal(10, site.Texture);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 5, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
            var restored = Reload(content, campaign, 17, "ufo-converted.sav");
            AdvanceOne(restored);
            if (respawn)
            {
                Assert.Equal(UfoStatus.IgnoreMe, Assert.Single(restored.Capture().World.Ufos).Status);
                Assert.Equal(9, Assert.Single(restored.Capture().World.MissionSites).UfoUniqueId);
                Assert.Equal(1, Assert.Single(restored.Capture().World.Missions).LiveUfos);
            }
            else
            {
                Assert.Empty(restored.Capture().World.Ufos);
                Assert.Equal(0, Assert.Single(restored.Capture().World.Missions).LiveUfos);
            }
        }
        Assert.Equal(8, count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertedDormantUfoReactivatesAfterSiteExpiryAndReload(bool cache)
    {
        var content = Load(cache);
        var campaign = CampaignState.Restore(Snapshot(content, respawn: true), content, new QuarterChoices());
        Assert.Contains(campaign.Execute(new AdvanceCampaignTime(1)).Events, item => item is MissionSiteDetected);
        var converted = Reload(content, campaign, 17, "converted-dormant.sav").Capture();
        var dormant = Assert.Single(converted.World.Ufos);
        Assert.Equal(UfoStatus.IgnoreMe, dormant.Status);
        Assert.Equal(WorldAltitudes.Ground, dormant.Altitude);
        Assert.Equal(0, dormant.Speed);
        var due = converted with
        {
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            World = converted.World with { MissionSites = [converted.World.MissionSites[0] with { SecondsRemaining = 0 }] },
        };
        var restored = CampaignState.Restore(due, content, new QuarterChoices());
        AdvanceOne(restored);
        var after = restored.Capture();
        Assert.Empty(after.World.MissionSites);
        var lifted = Assert.Single(after.World.Ufos);
        Assert.Equal(UfoStatus.Flying, lifted.Status);
        Assert.Equal(WorldAltitudes.VeryLow, lifted.Altitude);
        Assert.Equal(1100, lifted.Speed);
        Assert.NotEqual(dormant.Position, lifted.Position);
        Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
        Reload(content, restored, 17, "converted-reactivated.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomDeploymentUsesEffectiveRaceStatsAndOptionalLookup(bool missing)
    {
        var content = Load(overlay: missing ? "ufos:\n  - type: UFO_SITE_MISSING\n    speedMax: 2200\n    damageMax: 50\n    missionCustomDeploy: MISSING_CUSTOM\n" : null);
        if (missing) Assert.Equal("MISSING_CUSTOM", content.RuntimeRules.Ufos[
            content.RuntimeRules.Ufos.GetRequired("UFO_SITE_MISSING")].Value.Stats.MissionCustomDeployment);
        var snapshot = Snapshot(content, custom: true);
        if (missing) snapshot = snapshot with
        { World = snapshot.World with { Ufos = [snapshot.World.Ufos[0] with { RuleId = "UFO_SITE_MISSING" }] } };
        if (!missing) snapshot = snapshot with
        { World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }] } };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        Assert.Contains(campaign.Execute(new AdvanceCampaignTime(1)).Events, item => item is MissionSiteDetected);
        var site = Assert.Single(campaign.Capture().World.MissionSites);
        Assert.Equal(missing ? string.Empty : "RULE_DEPLOY", site.CustomDeploymentId);
        Assert.Equal("DIRECT_DEPLOY", site.DeploymentId);
        Assert.Equal(7200, site.SecondsRemaining);
        Assert.Equal(7, site.Id);
        Reload(content, campaign, 17, "ufo-custom-deployment.sav");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConversionIdAndScoreFailuresAreAtomic(bool marker)
    {
        var content = Load();
        var snapshot = Snapshot(content);
        snapshot = marker ? snapshot.WithNextId("DIRECT_MARKER", int.MaxValue) : snapshot with
        { Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 4] }, .. snapshot.Regions.Skip(1)] };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new QuarterChoices()), marker
            ? "Mission site marker IDs are exhausted or collide with a saved target."
            : "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void ConversionRetainsPreciseCombinedTickGuard()
    {
        var content = Load();
        var snapshot = Snapshot(content) with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new QuarterChoices()),
            "UFO site conversion requires combined half-hour counter forecasts.");
    }

    [Fact]
    public void SuppressedRespawnConversionEndsThePassBeforeAnUnrelatedGuardedLanding()
    {
        var content = Load();
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { RuleId = "UFO_SITE_SUPPRESSED", RegionId = "SITE_EMPTY" }],
                MissionSites = [new MissionSiteSnapshot(8, "DIRECT_OVERRIDE", "DIRECT_DEPLOY", "RACE_A", 0.2, 0.1, 7200)
                { Detected = true }],
            },
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Speed = 6000,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = new(WorldTargetKind.MissionSite, "DIRECT_MARKER", 8, 0.2, 0.1),
            },
        });
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with { Soldiers =
            [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } },
                .. owner.Soldiers.Skip(1)] }, .. snapshot.Bases.Skip(1)],
        };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(after.World.Ufos).Status);
        Assert.Single(after.World.MissionSites); // No new site or dormant link was created.
        Assert.Equivalent(snapshot.Bases[0].Crafts, after.Bases[0].Crafts, strict: true);
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 5, after.Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 17, "suppressed-conversion.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyDormantConversionsRechargeAndAllowLaterUfoMovement(bool respawn)
    {
        const string overlay = "ufos:\n  - type: UFO_SITE_SHIELDS\n    speedMax: 2200\n    damageMax: 50\n" +
            "    shieldCapacity: 10\n    shieldRechargeInGeoscape: 100\n";
        var content = Load(overlay: overlay);
        var snapshot = Snapshot(content, respawn: respawn);
        var later = snapshot.World.Ufos[0] with
        {
            UniqueId = 10,
            Id = 4,
            TrajectoryId = "TRAJ_PATROL",
            TrajectoryPoint = 0,
            Longitude = 0.4,
            Latitude = 0.2,
            Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
        };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = [snapshot.World.Ufos[0] with { RuleId = "UFO_SITE_SHIELDS", Shield = 0 }, later],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        var result = campaign.Execute(new AdvanceCampaignTime(10));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.IsType<MissionSiteDetected>(result.Events[1]);
        var after = campaign.Capture();
        Assert.Equal(respawn ? 1 : 0, after.World.Ufos[0].Shield);
        Assert.Equal(respawn ? WorldGeometry.Move(later.Position, later.Destination.Position,
            WorldGeometry.RadianSpeed(later.Speed)) : later.Position, after.World.Ufos[1].Position);
        Assert.Equal(2, Assert.Single(after.World.Missions).LiveUfos);
        Reload(content, campaign, 17, "conversion-fallthrough.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedSiteConversionMatchesCapturedTerminalOutcome(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "suppressedConversionRows"))
        {
            var respawn = row[0].GetBoolean();
            var snapshot = Snapshot(content);
            snapshot = snapshot with
            {
                World = snapshot.World with
                {
                    Missions = [snapshot.World.Missions[0] with
                    { RuleId = respawn ? "UFO_SITE_SUPPRESSED" : "UFO_SITE_SUPPRESSED_NO_RESPAWN", RegionId = "SITE_EMPTY" }],
                },
            };
            var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                respawn, Assert.Single(after.World.Ufos).Status == UfoStatus.Destroyed ? "destroyed" : "ignore",
                after.World.MissionSites.Count, 1, after.RandomState,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
            Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 5, after.Regions[0].ActivityAlien[^1]);
            Reload(content, campaign, 17, "suppressed-captured-conversion.sav");
        }
    }

    private sealed class QuarterChoices : IStatefulRandomSource
    {
        public ulong State { get; set; }
        public void Restore(ulong state) => State = state;
        public int NextExclusive(int maximum) { State++; return 0; }
        public uint NextUInt32() { State++; return 0; }
        public ulong NextUInt64() { State++; return 0; }
        public int NextInclusive(int minimum, int maximum) { State++; return minimum; }
        public double NextUnit() { State++; return 0.25; }
    }
}
