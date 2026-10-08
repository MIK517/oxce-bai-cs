using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldLandingBaseResearchTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SelectedLandingBaseRetainsIdentityScoreAndFlightFallthrough(bool cache, bool showBase)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = StrategicWorldConversionResearchTests.Content(cache, baseLanding: true, showBase: showBase);
        var interruptedContent = StrategicWorldConversionResearchTests.Content(cache, interrupt: "CORE", baseLanding: true, showBase: showBase);
        var continuedContent = StrategicWorldConversionResearchTests.Content(cache, interrupt: "", baseLanding: true, showBase: showBase);
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldConversionResearchTests.Snapshot(content, seed, baseLanding: true);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(interrupted, Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(interrupted ? UfoStatus.Destroyed : UfoStatus.Landed, after.World.Ufos[0].Status);
            Assert.Equal(interrupted, result.Events.OfType<UfoContactLost>().Any());
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(interrupted, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
            Assert.Equal(interrupted ? 0UL : 1UL, after.World.AlienBaseBirthSequence);
            Assert.Equal(interrupted ? 70 : 71, after.NextIds["ARRIVAL_BASE"]);
            Assert.Equal(interrupted ? 13 : 18, after.Regions.Sum(region => (long)region.ActivityAlien[^1]) -
                initial.Regions.Sum(region => (long)region.ActivityAlien[^1]));
            if (interrupted) Assert.Empty(after.World.AlienBases);
            else
            {
                var alienBase = Assert.Single(after.World.AlienBases);
                Assert.Equal((70, "DEPLOY_ARRIVAL_BASE", "alien-base:generated:1"),
                    (alienBase.Id, alienBase.DeploymentId, alienBase.PreservationKey));
                Assert.Equal(initial.World.Missions[0].Race, alienBase.Race);
                Assert.Equal(after.World.Ufos[0].Position, alienBase.Position);
                Assert.Equal(initial.MonthsPassed, alienBase.StartMonth);
                // showAlienBase is consulted after mission think, not UFO landing births.
                Assert.False(alienBase.Discovered);
                Assert.DoesNotContain(campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets, target => target.Kind == WorldTargetKind.AlienBase);
            }
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
                campaign = Reload(content, campaign, seed, "landing-base-research-arrival.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(interrupted ? 1 : 2, campaign.Capture().World.Ufos.Count);
            Assert.Equal(interrupted ? 0 : 1, campaign.Capture().World.AlienBases.Count);
            Assert.Equal(interrupted ? 1 : 2, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
            Reload(content, campaign, seed, "landing-base-research-followup.sav");
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleLandingBaseReservesMarkerScoreAndBirthSequenceBeforeSelection(bool cache)
    {
        var content = StrategicWorldConversionResearchTests.Content(cache, baseLanding: true);
        var initial = StrategicWorldConversionResearchTests.Snapshot(content, 17, baseLanding: true);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("ARRIVAL_BASE", int.MaxValue), content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
        var exhausted = initial with { World = initial.World with { AlienBaseBirthSequence = ulong.MaxValue } };
        AssertTimeBlocked(CampaignState.Restore(exhausted, content, new SplitMix64RandomSource(17)),
            "Base objective birth sequence is exhausted.");
        var score = initial with { Regions = initial.Regions.Select(region => region with { ActivityAlien = [int.MaxValue] }).ToArray() };
        AssertTimeBlocked(CampaignState.Restore(score, content, new SplitMix64RandomSource(17)),
            "UFO alien activity exceeds the supported range.");
    }
}
