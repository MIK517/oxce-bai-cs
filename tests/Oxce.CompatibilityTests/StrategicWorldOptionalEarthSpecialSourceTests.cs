using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalEarthSpecialSourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EarthSourceLossUsesNativeSavedHunterAndEscortFlightRules(int path)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        foreach (var escort in new[] { false, true })
        {
            var content = Content(path);
            var deletedContent = Content(path, "CORE");
            var survivedContent = Content(path, "");
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldOptionalBaseMissionTests.Source(
                    StrategicWorldDailyMissionResearchTests.Snapshot(content, seed, path));
                initial = initial with
                {
                    World = initial.World with
                    {
                        Ufos = [initial.World.Ufos[0] with { HunterKiller = true, Escort = escort }, initial.World.Ufos[1]],
                    },
                };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                Assert.Equal(deleted, Assert.Single(after.World.Missions).Interrupted);
                Assert.Equal(deleted, after.World.Missions[0].AlienBase is null);
                Assert.Equal(!deleted, after.World.AlienBases.Any(alienBase => alienBase.Id == 6));
                Assert.True(after.World.Ufos[0].HunterKiller);
                Assert.Equal(escort, after.World.Ufos[0].Escort);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent,
                    new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "optional-earth-special-source.sav");
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

    private static RuntimeContent Content(int path, string code = "BONUS") =>
        StrategicWorldConversionResearchTests.Content(respawn: path == 4, interrupt: "", baseLanding: path == 5,
            operation: path % 6 + 1, sourceCode: code);
}
