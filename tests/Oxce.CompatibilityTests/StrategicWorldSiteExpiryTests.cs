using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteExpiryTests
{
    [Fact]
    public void SuppressedInstantEventsExpireWithoutSchedulingOrHistoryMutation()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"]);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "despawnChoices"))
        {
            var name = row[0].GetString()!;
            var deployment = name switch
            {
                "empty" => "SITE_NO_EVENT",
                "zero" => "SITE_ZERO_EVENT",
                "missing" => "SITE_MISSING_EVENT",
                "mixed" => "SITE_MIXED_EVENT",
                _ => "SITE_INTERRUPTED_EVENT",
            };
            var snapshot = CreateSnapshot(content, "Suppressed instant event") with
            {
                Time = new(1, 1, 1, 1999, 1, 29, 55),
                RandomState = 0,
            };
            snapshot = snapshot with
            {
                CompletedResearch = name is "interrupted" or "mixed" ? [.. snapshot.CompletedResearch, "STOP"] : snapshot.CompletedResearch,
                World = snapshot.World with
                {
                    MissionSites = [new MissionSiteSnapshot(1, "MISSION_SITE", deployment, "RACE_A", 0.2, 0.1, 0)
                    { Detected = true }],
                },
            };
            var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
            if (name == "active")
            {
                AssertTimeBlocked(campaign, "Mission site despawn requires research or event payloads.");
                continue;
            }
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Events);
            Assert.Equal(snapshot.World.GeneratedEvents, after.World.GeneratedEvents);
            Assert.Equal((ulong)row[2].GetInt32(), after.RandomState);
            Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 9, after.Regions[0].ActivityAlien[^1]);
            Reload(content, campaign, 17, "suppressed-site-event.sav");
        }
    }

    [Fact]
    public void WeightedEventChoicesMatchCapturedOrdinalSelection()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "despawnChoices"))
        {
            var name = row[0].GetString()!;
            var weights = new Dictionary<string, ulong>(StringComparer.Ordinal);
            if (name == "missing") weights.Add("MISSING", 1);
            if (name is "interrupted" or "active" or "mixed") weights.Add("INSTANT", 1);
            if (name == "mixed") weights.Add("MISSING", 1);
            var random = new MinimumChoices();
            var selected = WorldAlienMissionGeneration.Choose(weights, random);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { name, selected, random.State });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Fact]
    public void SameDayResearchInterruptsInstantEventButDebugModeDoesNot()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"]);
        var snapshot = CreateSnapshot(content, "Daily event interruption") with { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with { Research = [new ResearchProjectSnapshot("STOP", 0, 1, 1)] }],
            World = snapshot.World with
            {
                MissionSites = [new MissionSiteSnapshot(1, "MISSION_SITE", "SITE_INTERRUPTED_EVENT", "RACE_A", 0.2, 0.1, 0)
                { Detected = true }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Contains("STOP", campaign.Capture().CompletedResearch);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Empty(campaign.Capture().World.GeneratedEvents);
        var debug = CampaignState.Restore(snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            DebugMode = true,
        }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(debug, "Mission site despawn requires research or event payloads.");
    }

    [Fact]
    public void OverflowingEventWeightsFailBeforeTheTick()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"]);
        var snapshot = CreateSnapshot(content, "Malformed despawn weights") with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                MissionSites = [new MissionSiteSnapshot(1, "MISSION_SITE", "SITE_OVERFLOW_EVENT", "RACE_A", 0.2, 0.1, 0)
                { Detected = true }],
            }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Throws<InvalidDataException>(() => campaign.Execute(new AdvanceCampaignTime(1)));
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
    }

    [Fact]
    public void UfoFollowerRestorationMatchesCapturedDestructorAndReset()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-timer");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "ufoFollowerRows"))
        {
            var original = row[0].GetBoolean();
            var ufo = new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, 0.2, 0.1,
                UfoStatus.Flying, WorldAltitudes.High)
            {
                OriginalWaypoint = original ? new(0.4, 0.2) : null,
                Hunting = row[1].GetBoolean(),
                Escorting = row[2].GetBoolean(),
                Destination = new(WorldTargetKind.MissionSite, "STR_TERROR_SITE", 1, 0.2, 0.1),
            };
            var after = CampaignWorld.RestoreUfoOriginalWaypoint(ufo);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                original, ufo.Hunting, ufo.Escorting, after.OriginalWaypoint is not null,
                original ? after.Destination!.Longitude : 0, original ? after.Destination!.Latitude : 0,
                after.Hunting, after.Escorting,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Fact]
    public void ForcedSiteExpiryRestoresUfoWaypointBeforeMovementAndReload()
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-timer.rul", "strategic-site-expiry.rul"]);
        var snapshot = CreateSnapshot(content, "UFO follows site") with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        var ufo = new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, 0.4, 0.2, UfoStatus.Flying, WorldAltitudes.High)
        {
            Speed = 2200,
            MissionWaveNumber = 1,
            Shield = 0,
            Hunting = true,
            Escorting = true,
            OriginalWaypoint = new(1, 0.6),
            Destination = new(WorldTargetKind.MissionSite, "STR_TERROR_SITE", 1, 0.2, 0.1),
        };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_SCOUT", "REGION", "RACE_A", 2, 0, 1500, 1, -1)],
                Ufos = [ufo],
                MissionSites = [new MissionSiteSnapshot(1, "MISSION_SITE_FORCE", "SITE_NO_EVENT", "RACE_A", 0.2, 0.1, 0)
                { Detected = true }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        // finishLoading restores site destinations as anonymous waypoints. This
        // assignment models a live follower before Target's deletion callback.
        ((List<UfoSnapshot>)((CampaignWorld)campaign.GetQuery<ICampaignWorldQuery>()!).Ufos)[0] = ufo;
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Single(result.Events.OfType<MissionSiteLost>());
        Assert.Empty(campaign.Capture().World.MissionSites);
        var after = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Null(after.OriginalWaypoint);
        Assert.False(after.Hunting);
        Assert.False(after.Escorting);
        Assert.Equal(WorldTargetKind.Waypoint, after.Destination!.Kind);
        Assert.Equal(0, after.Destination.Id);
        Assert.Equal(new WorldPosition(1, 0.6), after.Destination.Position);
        Assert.Equal(WorldGeometry.Move(ufo.Position, new(1, 0.6), WorldGeometry.RadianSpeed(2200)), after.Position);
        Reload(content, campaign, 17, "site-ufo-follower-restored.sav");

        var unsafeCampaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Ufos = [ufo with { OriginalWaypoint = null }] },
        }, content, new SplitMix64RandomSource(17));
        ((List<UfoSnapshot>)((CampaignWorld)unsafeCampaign.GetQuery<ICampaignWorldQuery>()!).Ufos)[0] =
            ufo with { OriginalWaypoint = null };
        AssertTimeBlocked(unsafeCampaign, "Targeted mission site despawn requires UFO follower destination repair.");
    }

    [Fact]
    public void InstantEligibilityCaptureAgreesWithTheDeferredPayloadBoundary()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"]);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "instantRows"))
        {
            var snapshot = CreateSnapshot(content, "Instant eligibility") with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
            snapshot = snapshot with
            {
                CompletedResearch = row[1].GetBoolean() ? [.. snapshot.CompletedResearch, "STOP"] : snapshot.CompletedResearch,
                World = snapshot.World with
                {
                    MissionSites = [new MissionSiteSnapshot(1, "MISSION_SITE",
                        row[0].GetBoolean() ? "SITE_INTERRUPTED_EVENT" : "SITE_MISSING_EVENT", "RACE_A", 0.2, 0.1, 0)
                    { Detected = true }],
                },
            };
            var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
            if (row[2].GetBoolean())
            {
                Assert.Equal(1, row[3].GetInt32()); // Reference would record generation immediately before executing its payload.
                AssertTimeBlocked(campaign, "Mission site despawn requires research or event payloads.");
            }
            else
            {
                Assert.Equal(0, row[3].GetInt32());
                AdvanceOne(campaign);
                Assert.Empty(campaign.Capture().World.GeneratedEvents);
            }
        }
    }

    [Fact]
    public void ExistingSiteDespawnChoicesHaveAnAggregateWorkLimit()
    {
        var weights = string.Join(", ", Enumerable.Range(0, 1000).Select(index => $"MISSING_{index}: 1"));
        var overlay = "alienDeployments:\n  - type: SITE_LARGE_EVENTS\n    markerName: STR_TERROR_SITE\n" +
            $"    despawnEvents: {{{weights}}}\n";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"], extraRules: overlay);
        var snapshot = CreateSnapshot(content, "Bounded site despawn") with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                MissionSites = Enumerable.Range(1, 1000).Select(id => new MissionSiteSnapshot(id, "MISSION_SITE", "SITE_LARGE_EVENTS",
                    "RACE_A", 0.2, 0.1, 0)
                { Detected = true }).ToArray(),
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
        AssertTimeBlocked(campaign, "Mission site despawn checks exceed the supported work limit.");
    }

    private sealed class MinimumChoices : IStatefulRandomSource
    {
        public ulong State { get; set; }
        public void Restore(ulong state) => State = state;
        public ulong NextUInt64() => State++;
        public int NextInclusive(int minimum, int maximum) { State++; return minimum; }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() { State++; return 0; }
    }
}
