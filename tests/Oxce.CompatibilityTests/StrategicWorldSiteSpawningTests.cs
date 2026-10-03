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

public sealed class StrategicWorldSiteSpawningTests
{
    private static readonly int[] ChainIds = [7, 8, 9];
    private static readonly int[] ChainTimers = [5400, 5400, 9000];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitDeploymentAndSelectedAreaMatchCapturedCreation(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = row.GetProperty("name").GetString();
            if (name is not ("override" or "selected")) continue;
            var selected = name == "selected";
            var snapshot = Snapshot(content, selected ? "DIRECT_SELECTED" : "DIRECT_OVERRIDE", selected ? 1 : -1)
                .WithNextId("DIRECT_MARKER", 7);
            var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var site = Assert.Single(after.World.MissionSites);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                site.DeploymentId,
                site.Longitude.ToString("F9", CultureInfo.InvariantCulture),
                site.Latitude.ToString("F9", CultureInfo.InvariantCulture),
                site.Id, site.SecondsRemaining + 1800, site.Race, site.Texture, site.City,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetProperty("site").GetRawText())),
                CanonicalJson.Normalize(actual));
            Assert.Equal((ulong)row.GetProperty("calls").GetInt32(), after.RandomState);
            Assert.False(site.Detected);
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.Missions);
            Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 4, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(snapshot.Countries[0].ActivityAlien[^1] + 4, after.Countries[0].ActivityAlien[^1]);
            Reload(content, campaign, 17, "direct-site-created.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateSitesReserveSharedMarkersAndScoreInWaveOrder(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "DIRECT_CHAIN").WithNextId("DIRECT_MARKER", 7).WithNextId("RULE_MARKER", 9);
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(ChainIds, after.World.MissionSites.Select(site => site.Id));
        Assert.Equal(ChainTimers, after.World.MissionSites.Select(site => site.SecondsRemaining));
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 15, after.Regions[0].ActivityAlien[^1]);
        Assert.Empty(after.World.Missions);
        Assert.Equal(14UL, after.RandomState); // Four draws per site, plus each zero-timer follow-up.
        Reload(content, campaign, 17, "direct-site-chain.sav");
    }

    [Theory]
    [InlineData("DIRECT_ZERO_DURATION")]
    [InlineData("DIRECT_OVERFLOW_DURATION")]
    public void UnsafeDurationsStopBeforeAnyTickMutation(string mission)
    {
        var content = Load(false);
        var campaign = CampaignState.Restore(Snapshot(content, mission), content, new QuarterChoices());
        AssertTimeBlocked(campaign, "Direct mission site duration is outside the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeChainMarkerAndActivityReservationsBlockAtomically(bool marker)
    {
        var content = Load(false);
        var snapshot = Snapshot(content, "DIRECT_CHAIN");
        if (marker) snapshot = snapshot.WithNextId("DIRECT_MARKER", int.MaxValue - 1);
        else snapshot = snapshot with { Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 10] }] };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AssertTimeBlocked(campaign, marker ? "Mission site marker IDs are exhausted or collide with a saved target." :
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuleAndTerrainDeploymentChoicesMatchCapturedReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = row.GetProperty("name").GetString();
            if (name is not ("rule" or "terrain")) continue;
            var snapshot = Snapshot(content, name == "rule" ? "DIRECT_RULE" : "DIRECT_TERRAIN")
                .WithNextId(name == "rule" ? "RULE_MARKER" : "DIRECT_MARKER", 7);
            var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var site = Assert.Single(after.World.MissionSites);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                site.DeploymentId, site.Longitude.ToString("F9", CultureInfo.InvariantCulture),
                site.Latitude.ToString("F9", CultureInfo.InvariantCulture), site.Id,
                site.SecondsRemaining + 1800, site.Race, site.Texture, site.City,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetProperty("site").GetRawText())),
                CanonicalJson.Normalize(actual));
            Assert.Equal((ulong)row.GetProperty("calls").GetInt32(), after.RandomState);
            Reload(content, campaign, 17, "terrain-site.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleZeroWeightStillSpawnsAndEmptyOrAllZeroTablesSuppressWithoutDraws(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = row.GetProperty("name").GetString();
            if (name is not ("single-zero" or "all-zero" or "empty-terrain" or "null-choice")) continue;
            var region = name switch
            {
                "single-zero" => "SITE_SINGLE_ZERO",
                "all-zero" => "SITE_ALL_ZERO",
                "null-choice" => "SITE_NULL_CHOICE",
                _ => "SITE_EMPTY",
            };
            var campaign = CampaignState.Restore(Snapshot(content, "DIRECT_TERRAIN", region: region)
                .WithNextId("DIRECT_MARKER", 7), content, new QuarterChoices());
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Equal((ulong)row.GetProperty("calls").GetInt32(), after.RandomState);
            if (name == "single-zero") Assert.Equal("DIRECT_DEPLOY", Assert.Single(after.World.MissionSites).DeploymentId);
            else Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Missions);
            Reload(content, campaign, 17, "empty-terrain.sav");
        }
    }

    private static RuntimeContent Load(bool cache) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-site-spawn.rul"], cache);

    private static CampaignSnapshot Snapshot(RuntimeContent content, string mission, int area = -1, string region = "SITE_SPAWN_REGION")
    {
        var snapshot = CreateSnapshot(content, "Direct mission sites");
        return snapshot with
        {
            RandomState = 0,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, mission, region, "RACE_A", 0, 0, 0, 0, area)],
            },
        };
    }

    [Theory]
    [InlineData("SITE_MISSING_TEXTURE")]
    [InlineData("SITE_OVERFLOW_WEIGHTS")]
    public void MalformedTerrainChoicesFailBeforeTickMutation(string region)
    {
        var content = Load(false);
        var campaign = CampaignState.Restore(Snapshot(content, "DIRECT_TERRAIN", region: region),
            content, new QuarterChoices());
        var before = campaign.Capture();
        Assert.Throws<InvalidDataException>(() => campaign.Execute(new AdvanceCampaignTime(1)));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroWeightMissingChoiceIsUnreachableAndExplicitTypeIgnoresMissingTexture(bool cache)
    {
        var content = Load(cache);
        foreach (var scenario in new[] { ("DIRECT_TERRAIN", "SITE_ZERO_MISSING", "DIRECT_DEPLOY"),
                     ("DIRECT_RULE", "SITE_MISSING_TEXTURE", "RULE_DEPLOY") })
        {
            var campaign = CampaignState.Restore(Snapshot(content, scenario.Item1, region: scenario.Item2),
                content, new QuarterChoices());
            AdvanceOne(campaign);
            Assert.Equal(scenario.Item3, Assert.Single(campaign.Capture().World.MissionSites).DeploymentId);
            Reload(content, campaign, 17, "explicit-site-texture.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeightedTailIsReachableAndItsScoreIsReservedBeforeAnyDraw(bool cache)
    {
        var content = Load(cache);
        var campaign = CampaignState.Restore(Snapshot(content, "DIRECT_TERRAIN"), content, new QuarterChoices(true));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal("RULE_DEPLOY", Assert.Single(after.World.MissionSites).DeploymentId);
        Assert.Equal(12600, after.World.MissionSites[0].SecondsRemaining);
        var snapshot = Snapshot(content, "DIRECT_TERRAIN");
        snapshot = snapshot with { Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 5] }] };
        var blocked = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AssertTimeBlocked(blocked, "UFO alien activity exceeds the supported range.");
    }

    private sealed class QuarterChoices(bool maximum = false) : IStatefulRandomSource
    {
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int upper) { State++; return maximum ? upper : minimum; }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() { State++; return 0.25; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerBaseSitesMatchCapturedSingleMultipleAndSuppressedChoices(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = row.GetProperty("name").GetString();
            if (name is not ("base" or "multiple-bases" or "missing-base")) continue;
            var snapshot = Snapshot(content, "DIRECT_BASE").WithNextId("DIRECT_MARKER", 7);
            var owner = snapshot.Bases[0];
            if (name == "multiple-bases") snapshot = snapshot with
            {
                Bases = [owner, owner with { Id = 2, Name = "Beta", Longitude = 0.4, Crafts = [], Soldiers = [] }],
            };
            if (name == "missing-base") snapshot = snapshot with { Bases = [owner with { Longitude = 2 }] };
            var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var actual = JsonSerializer.SerializeToUtf8Bytes(after.World.MissionSites.Count == 0 ? [] : new object[]
            {
                after.World.MissionSites[0].DeploymentId,
                after.World.MissionSites[0].Longitude.ToString("F9", CultureInfo.InvariantCulture),
                after.World.MissionSites[0].Latitude.ToString("F9", CultureInfo.InvariantCulture),
                after.World.MissionSites[0].Id, after.World.MissionSites[0].SecondsRemaining + 1800,
                after.World.MissionSites[0].Race, after.World.MissionSites[0].Texture, after.World.MissionSites[0].City,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetProperty("site").GetRawText())),
                CanonicalJson.Normalize(actual));
            Assert.Equal((ulong)row.GetProperty("calls").GetInt32(), after.RandomState);
            Assert.Empty(after.World.Missions);
            Reload(content, campaign, 17, "base-site.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBaseSuppressesEvenInvalidDurationAndMultipleChoicesCanSelectLastBase(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "DIRECT_BASE_ZERO");
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Longitude = 2 }] };
        var absent = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AdvanceOne(absent);
        Assert.Empty(absent.Capture().World.MissionSites);
        Assert.Equal(1UL, absent.Capture().RandomState);

        snapshot = Snapshot(content, "DIRECT_BASE");
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner, owner with { Id = 2, Name = "Beta", Longitude = 0.4, Crafts = [], Soldiers = [] },
                owner with { Id = 3, Name = "Outside", Longitude = 2, Crafts = [], Soldiers = [] }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices(true));
        AdvanceOne(campaign);
        var site = Assert.Single(campaign.Capture().World.MissionSites);
        Assert.Equal(0.4, site.Longitude);
        Assert.Equal(owner.Latitude, site.Latitude);
        Assert.Equal(string.Empty, site.City);
        Assert.Equal(5UL, campaign.Capture().RandomState);
        Reload(content, campaign, 17, "last-base-site.sav");
    }

    [Fact]
    public void MaximumIntegerSiteScoreIsForecastAsARealChoice()
    {
        var content = Load(false);
        var snapshot = Snapshot(content, "DIRECT_BASE_MAX_SCORE");
        snapshot = snapshot with { Regions = [snapshot.Regions[0] with { ActivityAlien = [1] }] };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteCreatedAfterHourlyPassIsDetectedNextHourAcrossReload(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "DIRECT_OVERRIDE") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
        };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AdvanceOne(campaign);
        Assert.False(Assert.Single(campaign.Capture().World.MissionSites).Detected);
        campaign = Reload(content, campaign, 17, "site-awaiting-detection.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(720));
        Assert.Single(result.Events.OfType<MissionSiteDetected>());
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(720, Assert.Single(result.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        var site = Assert.Single(campaign.Capture().World.MissionSites);
        Assert.True(site.Detected);
        Assert.Equal(1800, site.SecondsRemaining);
        Reload(content, campaign, 17, "new-site-detected.sav");
    }

    [Fact]
    public void SuppressedTerrainChoiceCannotSupplyNegativeScoreHeadroomForLaterWave()
    {
        var content = Load(false);
        var snapshot = Snapshot(content, "DIRECT_NULL_CHAIN", region: "SITE_NULL_CHOICE") with
        {
            Regions = [Snapshot(content, "DIRECT_NULL_CHAIN").Regions[0] with { ActivityAlien = [int.MaxValue] }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void ExistingNegativeSiteScoresBeforeNewPositiveSites()
    {
        var content = Load(false);
        var snapshot = Snapshot(content, "DIRECT_CHAIN").WithNextId("DIRECT_MARKER", 7).WithNextId("RULE_MARKER", 9);
        snapshot = snapshot with
        {
            Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 14] }],
            World = snapshot.World with
            {
                MissionSites = [new MissionSiteSnapshot(1, "DIRECT_OVERRIDE", "DIRECT_NEGATIVE", "RACE_A", 0.2, 0.1, 3600)
                { Detected = true }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new QuarterChoices());
        AdvanceOne(campaign);
        Assert.Equal(int.MaxValue - 3, campaign.Capture().Regions[0].ActivityAlien[^1]);
        Assert.Equal(4, campaign.Capture().World.MissionSites.Count);
        Reload(content, campaign, 17, "existing-site-score-order.sav");
    }

    [Fact]
    public void UnreachableDeploymentTableEntriesStillConsumeBoundedPreflightWork()
    {
        var weights = string.Join(", ", Enumerable.Range(0, 1000).Select(index => $"MISSING_{index}: 0"));
        var overlay = $"globe:\n  textures:\n    - {{id: 36, deployments: {{{weights}}}}}\n" +
            "regions:\n  - type: SITE_LARGE_EMPTY\n    missionZones:\n      - [[10, 10, 5, 5, 36]]\n" +
            "alienMissions:\n  - type: DIRECT_LARGE_EMPTY\n    objective: 3\n    spawnZone: 0\n" +
            "    waves: [{ufo: UNKNOWN_DIRECT_UFO, count: 1000, trajectory: DIRECT_SITE, timer: 0, objective: true}]\n";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], extraRules: overlay);
        var campaign = CampaignState.Restore(Snapshot(content, "DIRECT_LARGE_EMPTY", region: "SITE_LARGE_EMPTY"),
            content, new QuarterChoices());
        AssertTimeBlocked(campaign, "Direct mission site creation exceeds the supported work limit.");
    }

    [Fact]
    public void EmptyMarkerNamespaceStopsBeforeCreationInsteadOfThrowingDuringIdAllocation()
    {
        const string overlay = "alienDeployments:\n  - type: EMPTY_MARKER\n    markerName: ''\n    duration: [2, 2]\n" +
            "alienMissions:\n  - type: DIRECT_EMPTY_MARKER\n    objective: 3\n    spawnZone: 0\n" +
            "    waves: [{ufo: EMPTY_MARKER, count: 1, trajectory: DIRECT_SITE, timer: 3000, objective: true}]\n";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], extraRules: overlay);
        var campaign = CampaignState.Restore(Snapshot(content, "DIRECT_EMPTY_MARKER"), content, new QuarterChoices());
        AssertTimeBlocked(campaign, "Direct mission site marker names require a nonempty ID namespace.");
    }
}
