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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedInstantEventsExpireWithoutSchedulingOrHistoryMutation(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-event");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"], cache);
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
            Time = new(1, 1, 1, 1999, 1, 29, 55), DebugMode = true,
        }, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(debug, "Mission site despawn requires research or event payloads.");
    }

    [Fact]
    public void OverflowingEventWeightsFailBeforeTheTick()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-expiry.rul"]);
        var snapshot = CreateSnapshot(content, "Malformed despawn weights") with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        snapshot = snapshot with { World = snapshot.World with
        {
            MissionSites = [new MissionSiteSnapshot(1, "MISSION_SITE", "SITE_OVERFLOW_EVENT", "RACE_A", 0.2, 0.1, 0)
            { Detected = true }],
        } };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Throws<InvalidDataException>(() => campaign.Execute(new AdvanceCampaignTime(1)));
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
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
