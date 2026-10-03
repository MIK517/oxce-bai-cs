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
    public void ImmediateSitesReserveSharedMarkersAndScoreAfterExistingSites(bool cache)
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

    private static RuntimeContent Load(bool cache) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-site-spawn.rul"], cache);

    private static CampaignSnapshot Snapshot(RuntimeContent content, string mission, int area = -1)
    {
        var snapshot = CreateSnapshot(content, "Direct mission sites");
        return snapshot with
        {
            RandomState = 0,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, mission, "SITE_SPAWN_REGION", "RACE_A", 0, 0, 0, 0, area)],
            },
        };
    }

    private sealed class QuarterChoices : IStatefulRandomSource
    {
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int maximum) { State++; return minimum; }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() { State++; return 0.25; }
    }
}
