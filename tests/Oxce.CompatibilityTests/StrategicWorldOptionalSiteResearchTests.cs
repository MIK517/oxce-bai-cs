using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSiteResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomDurationBirthsKeepActualGrantsInterruptionAndConditionalEventState(bool generated)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = Content(generated, eventInterrupt: "BONUS");
        var seenExpired = false;
        var seenSurvived = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, generated) with { RandomState = seed };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "optional-duration-before.sav");
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var expired = after.World.MissionSites.Count != 2;
            Assert.Equal(expired, after.CompletedResearch.Contains("BONUS"));
            Assert.Equal(expired, Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(expired ? 2 : 1, after.ResearchDiary.Count);
            Assert.Equal("CORE", after.ResearchDiary[0].RuleId);
            if (expired) Assert.Equal("BONUS", after.ResearchDiary[1].RuleId);
            Assert.All(after.World.MissionSites, site => Assert.Equal(1800, site.SecondsRemaining));
            Assert.Empty(after.World.Events);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Equivalent(control.Capture().World.MissionSites, after.World.MissionSites, strict: true);
            seenExpired |= expired;
            seenSurvived |= !expired;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-duration-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.True(seenExpired);
        Assert.True(seenSurvived);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleGrantCannotBypassDueEventAndOutcomeLimitsRemainAtomic(bool generated)
    {
        var content = Content(generated, eventInterrupt: "BONUS");
        var initial = Snapshot(content, generated);
        AssertEventTickRuns(CampaignState.Restore(initial with { World = initial.World with { Events = [new GeoscapeEventSnapshot("NEWBORN_EVENT", 30)] } }, content, new SplitMix64RandomSource(17)));
        content = Content(generated, count: 9);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, generated), content, new SplitMix64RandomSource(17)),
            "Mission research exceeds the supported outcome limit.");
    }

    [Fact]
    public void OptionalForecastPreservesCampaignAndSeparatesConditionalFromGuaranteedResearch()
    {
        var content = Content(generated: false);
        var campaign = CampaignState.Restore(Snapshot(content, generated: false), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.Advance("CORE"));
        Assert.Null(forecast.AdvanceOptional("BONUS", out var whenGranted));
        Assert.Contains("BONUS", whenGranted);
        Assert.DoesNotContain("BONUS", forecast.GuaranteedResearch);
        Assert.Contains("BONUS", forecast.PossibleResearch);
        Assert.DoesNotContain("BONUS", forecast.GuaranteedExplicitTopics);
        Assert.Contains("BONUS", forecast.PossibleExplicitTopics);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    internal static RuntimeContent Content(bool generated, string eventInterrupt = "", int count = 2) =>
        StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, maximumDuration: 1, generated: generated,
            interrupt: "BONUS", eventInterrupt: eventInterrupt, count: count);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, bool generated) => generated
        ? StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content)
        : StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
}
