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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleSpaceSourceDeletionClearsLinksWithoutInterruptingOrdinaryFlight(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        for (var path = 0; path < 7; path++)
        {
            var content = FlightContent(cache, path);
            var deletedContent = FlightContent(cache, path, "CORE");
            var survivedContent = FlightContent(cache, path, "");
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
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "optional-space-source-flight.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
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
    public void SpaceSourceDeletionPreservesDueRegionalWavesAndFinalObjectives(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var kind = 0; kind < 6; kind++)
        {
            var content = WaveContent(cache, kind);
            var deletedContent = WaveContent(cache, kind, "CORE");
            var survivedContent = WaveContent(cache, kind, "");
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
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "optional-space-source-wave.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SpaceSourceUnlinkingKeepsHunterAndEscortMissionSemantics(bool cache, bool escort)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        var content = FlightContent(cache, 0);
        var deletedContent = FlightContent(cache, 0, "CORE");
        var survivedContent = FlightContent(cache, 0, "");
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
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-space-source-special.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    private static RuntimeContent FlightContent(bool cache, int path, string code = "BONUS") =>
        StrategicWorldConversionResearchTests.Content(cache, respawn: path == 4, interrupt: "", baseLanding: path == 5,
            sourceCode: code, includeSource: true);

    private static RuntimeContent WaveContent(bool cache, int kind, string code = "BONUS") =>
        StrategicWorldDailyWaveResearchTests.Content(cache, kind, interrupt: "", sourceCode: code, includeSource: true);
}
