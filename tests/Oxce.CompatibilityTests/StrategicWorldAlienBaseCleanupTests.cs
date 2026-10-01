using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldAlienBaseCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyDeletionLinksAndPactsMatchExtractedReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-cleanup");
        var enabled = Load(cache);
        var disabled = Load(cache, false);
        Assert.True(enabled.RuntimeRules.Campaign.AllowCountriesToCancelAlienPact);
        Assert.False(disabled.RuntimeRules.Campaign.AllowCountriesToCancelAlienPact);
        var rows = new List<int[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var complete = row[0].GetInt32(); var cancel = row[1].GetInt32(); var pact = row[2].GetInt32();
            var content = cancel == 1 ? enabled : disabled;
            var snapshot = Snapshot(content) with { CompletedResearch = complete == 1 ? ["UNLOCK"] : [] };
            var country = snapshot.Countries[0];
            snapshot = snapshot with
            {
                Countries = [country with { Pact = pact == 1, NewPact = true, CancelPact = false }],
                World = snapshot.World with
                {
                    AlienBases = [.. snapshot.World.AlienBases, new(3, "CLEANUP_PASSIVE", "RACE_A", 0.3, 0.1)],
                    Missions = [.. snapshot.World.Missions.Select(mission => mission.Id == 6 ? mission with { AlienBase =
                        new(WorldTargetKind.AlienBase, "STR_OTHER_BASE", 3, 0.3, 0.1) } : mission)]
                }
            };
            var campaign = Restore(content, snapshot);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var space = after.World.Missions.Single(mission => mission.Id == 4);
            var earth = after.World.Missions.SingleOrDefault(mission => mission.Id == 5);
            rows.Add([complete, cancel, pact, after.World.AlienBases.Count - 1,
                space.AlienBase is null ? 0 : 1, space.Interrupted ? 1 : 0,
                earth?.AlienBase is null ? 0 : 1, earth is null || earth.Interrupted ? 1 : 0,
                after.World.Missions.Single(mission => mission.Id == 6).AlienBase is null ? 0 : 1,
                after.Countries[0].Pact ? 1 : 0, after.Countries[0].NewPact ? 1 : 0, after.Countries[0].CancelPact ? 1 : 0]);
            Assert.Equal(snapshot.RandomState, after.RandomState);
            Reload(content, campaign, 40, "base-cleanup.sav");
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(oracle.RootElement.GetProperty("rows"))),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false, "UNLOCK")]
    [InlineData(true, "UNLOCK")]
    [InlineData(false, "CLEANUP_SOURCE")]
    [InlineData(true, "CLEANUP_SOURCE")]
    public void SameDayResearchDeletesBeforeGenerationScoringAndLanding(bool cache, string research)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "CLEANUP_BAD_GENERATION").WithFacility("CLEANUP_LAB");
        snapshot = snapshot with
        {
            Bases = [snapshot.Bases[0] with { Research = [new(research, 1, 0, 1)] }],
            Regions = [.. snapshot.Regions.Select(region => region with { ActivityAlien = [int.MaxValue] })]
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_OUT", Longitude = 0.2, Latitude = 0.1, Speed = 0, Destination = BaseTarget() }
        });
        var campaign = Restore(content, snapshot);
        var events = campaign.Execute(new AdvanceCampaignTime(2)).Events;
        Assert.Empty(events.OfType<CampaignActionBlocked>());
        Assert.Single(events.OfType<CampaignResearchCompleted>());
        Assert.Empty(campaign.Capture().World.AlienBases);
        Assert.Null(campaign.Capture().World.Missions.Single(mission => mission.Id == 4).AlienBase);
        Assert.DoesNotContain(campaign.Capture().World.Missions, mission => mission.Id == 5);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
        var follower = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Null(follower.Destination);
        Assert.NotEqual("STR_OUT", follower.Status);
        Reload(content, campaign, 40, "base-research-cleanup.sav");
    }

    [Fact]
    public void UncertainRewardAndMissingResearchStopBeforeAnyDailyMutation()
    {
        var content = Load();
        var snapshot = Snapshot(content).WithFacility("CLEANUP_LAB");
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Research = [new("CLEANUP_REWARD", 1, 0, 1)] }] };
        AssertTimeBlocked(Restore(content, snapshot), "Alien base self-destruction depends on uncertain daily research outcomes.");
        AssertTimeBlocked(Restore(content, Snapshot(content, "CLEANUP_MISSING_RESEARCH")),
            "Alien base self-destruction references undefined research.");
    }

    [Fact]
    public void DeletionNeverResurrectsUnknownBaseSidecarsOnLoadedRewrites()
    {
        var content = Load();
        var snapshot = Snapshot(content) with { CompletedResearch = ["UNLOCK"] };
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        var start = yaml.IndexOf("alienBases:\n", StringComparison.Ordinal);
        var end = yaml.IndexOf('\n', start + "alienBases:\n".Length);
        yaml = yaml.Insert(end + 1, "    futureCleanupBase: preserved\n");
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40);
        AdvanceOne(loaded.Campaign);
        var written = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.DoesNotContain("futureCleanupBase", written, StringComparison.Ordinal);
        var again = TestFixtures.LoadLogisticsSave(written, content, 40);
        Assert.Empty(again.Campaign.Capture().World.AlienBases);
        Assert.DoesNotContain("futureCleanupBase", OxceSaveAdapter.EmitLoadedCampaign(again.Campaign.Capture(), again.Source), StringComparison.Ordinal);
    }

    [Fact]
    public void LiveEarthOperationsAndPreDailyChecksRetainPreciseBoundaries()
    {
        var content = Load();
        var snapshot = Snapshot(content) with { CompletedResearch = ["UNLOCK"] };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [.. snapshot.World.Missions.Select(mission =>
            mission.Id == 5 ? mission with { LiveUfos = 1 } : mission)]
            }
        };
        AssertTimeBlocked(Restore(content, snapshot),
            "Alien base self-destruction with live Earth-operation UFOs requires combined forecasts.");
        snapshot = snapshot with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.Single(campaign.Capture().World.AlienBases);
        Assert.NotNull(campaign.Capture().World.Missions.Single(mission => mission.Id == 5).AlienBase);
    }

    private static RuntimeContent Load(bool cache = false, bool cancel = true)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        return StrategicReadinessTestContent.Load(cancel ?
            ["strategic-world.rul", "strategic-alien-base-cleanup.rul", "strategic-alien-base-cleanup-pacts.rul"] :
            ["strategic-world.rul", "strategic-alien-base-cleanup.rul"], cache);
    }

    private static WorldTargetReference BaseTarget() => new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 2, 0.2, 0.1);
    private static CampaignSnapshot Snapshot(RuntimeContent content, string deployment = "CLEANUP_BASE")
    {
        var snapshot = CreateSnapshot(content, "Alien-base cleanup");
        return snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            RandomState = 0,
            World = snapshot.World with
            {
                AlienBases = [new(2, deployment, "RACE_A", 0.2, 0.1)
                { PactCountryId = snapshot.Countries[0].RuleId }],
                Missions = [
                new(4, "CLEANUP_SPACE", "REGION", "RACE_A", 0, 0, 9000, 0, -1) { AlienBase = BaseTarget() },
                new(5, "CLEANUP_EARTH", "REGION", "RACE_A", 0, 0, 9000, 0, -1) { AlienBase = BaseTarget() },
                new(6, "CLEANUP_SPACE", "REGION", "RACE_A", 0, 0, 9000, 0, -1) ]
            }
        };
    }
    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
}
