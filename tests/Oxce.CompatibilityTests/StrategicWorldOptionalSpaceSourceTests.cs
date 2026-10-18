using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalSpaceSourceTests
{
    [Fact]
    public void PossibleSpaceSourceDeletionClearsLinksWithoutInterruptingOrdinaryFlight()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        for (var path = 0; path < 7; path++)
        {
            var content = FlightContent(path);
            var deletedContent = FlightContent(path, "CORE");
            var survivedContent = FlightContent(path, "");
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldOptionalBaseMissionTests.Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, seed, path));
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                var mission = Assert.Single(after.World.Missions);
                Assert.False(mission.Interrupted);
                Assert.Equal(deleted, mission.AlienBase is null);
                Assert.Equal(!deleted, after.World.AlienBases.Any(alienBase => alienBase.Id == 6));
                Assert.Equal(path is 3 or 6, after.World.Ufos[0].Status == UfoStatus.Destroyed);
                Assert.Equal(path is 3 or 6, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                campaign = ReloadRepeatedly(content, campaign, seed, "optional-space-source-flight.sav", 3, after);
                AssertContinuesLike(control, campaign, 1);
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }

    [Fact]
    public void SpaceSourceDeletionPreservesDueRegionalWavesAndFinalObjectives()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var kind = 0; kind < 6; kind++)
        {
            var content = WaveContent(kind);
            var deletedContent = WaveContent(kind, "CORE");
            var survivedContent = WaveContent(kind, "");
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldOptionalBaseMissionTests.Source(StrategicWorldDailyWaveResearchTests.Snapshot(content, seed, kind));
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                Assert.Equal(!deleted, after.World.AlienBases.Any(alienBase => alienBase.Id == 6));
                Assert.All(after.World.Missions, mission =>
                {
                    Assert.False(mission.Interrupted);
                    Assert.Equal(deleted, mission.AlienBase is null);
                });
                Assert.Equal(kind == 1 ? 1 : 0, after.World.Ufos.Count);
                Assert.Equal(kind == 5 ? 1 : 0, after.World.MissionSites.Count);
                Assert.Equal((deleted ? 0 : 1) + (kind is 3 or 4 ? 1 : 0), after.World.AlienBases.Count);
                Assert.Equal(kind == 4, after.Countries.Any(country => country.NewPact));
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                campaign = ReloadRepeatedly(content, campaign, seed, "optional-space-source-wave.sav", 3, after);
                AssertContinuesLike(control, campaign, 1);
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpaceSourceUnlinkingKeepsHunterAndEscortMissionSemantics(bool escort)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        var content = FlightContent(0);
        var deletedContent = FlightContent(0, "CORE");
        var survivedContent = FlightContent(0, "");
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldOptionalBaseMissionTests.Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, seed, 0));
            initial = initial with
            {
                World = initial.World with
                {
                    Ufos =
                [initial.World.Ufos[0] with { HunterKiller = true, Escort = escort }, initial.World.Ufos[1]]
                }
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.False(Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(deleted, after.World.Missions[0].AlienBase is null);
            Assert.True(after.World.Ufos[0].HunterKiller);
            Assert.Equal(escort, after.World.Ufos[0].Escort);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            campaign = ReloadRepeatedly(content, campaign, seed, "optional-space-source-special.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    private static RuntimeContent FlightContent(int path, string code = "BONUS") =>
        StrategicWorldConversionResearchTests.Content(respawn: path == 4, interrupt: "", baseLanding: path == 5,
            sourceCode: code, includeSource: true);

    private static RuntimeContent WaveContent(int kind, string code = "BONUS") =>
        StrategicWorldDailyWaveResearchTests.Content(kind, interrupt: "", sourceCode: code, includeSource: true);
}
