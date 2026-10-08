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

public sealed class StrategicWorldSiteResearchWaveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoObjectChainsRunBeforeSiteInterruptionWithNativeCountersAndDraws(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache);
        foreach (var example in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = example.GetProperty("name").GetString()!;
            var row = TestFixtures.Rows(example, "rows").First();
            var initial = Snapshot(content, name, 1);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(66));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(new[] { row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32() })),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(new[] { mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown })));
            var random = new SplitMix64RandomSource(initial.RandomState);
            for (var call = 0; call < row[4].GetInt32(); call++) random.NextInclusive(0, 0);
            Assert.Equal(random.State, after.RandomState);
            Assert.True(mission.Interrupted);
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 66, "site-research-wave.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedNoObjectMissionIsRemovedBeforeSiteGrant(bool cache)
    {
        var content = Content(cache);
        var campaign = CampaignState.Restore(Snapshot(content, "zero", 0), content, new SplitMix64RandomSource(66));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Equivalent(after, Reload(content, campaign, 66, "site-research-completed-wave.sav").Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcessiveChainsAndActorBirthsStillStopBeforeSiteResearch(bool excessive)
    {
        var content = Content(false);
        var initial = Snapshot(content, excessive ? "excessive" : "ufos", 0);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(66));
        if (excessive)
        {
            var before = campaign.Capture();
            var error = Assert.Throws<InvalidDataException>(() => campaign.Execute(new AdvanceCampaignTime(1)));
            Assert.Equal("Immediate mission waves exceed the supported work limit.", error.Message);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
        else AssertTimeBlocked(campaign, "Mission site research requires mission-wave composition forecasts.");
    }

    internal static RuntimeContent Content(bool cache) => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - {type: CHAIN_mixed, interruptResearch: CORE}
          - {type: CHAIN_zero, interruptResearch: CORE}
          - {type: CHAIN_rounded, interruptResearch: CORE}
          - {type: CHAIN_empty-count, interruptResearch: CORE}
          - {type: CHAIN_pending, interruptResearch: CORE}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, string name, int live)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial with
        {
            RandomState = 66,
            Bases = [initial.Bases[0] with { Research = [] }],
            World = initial.World with
            { Missions = [new(4, "CHAIN_" + name, "REGION", "RACE_A", 0, name == "mixed" ? 1 : 0, name == "pending" ? 90 : 0, live, -1)] },
        };
    }
}
