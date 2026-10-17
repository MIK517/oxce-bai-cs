using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldArrivalResearchEnduranceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SelectedArrivalComposesForSeventyTwoHoursAcrossBatchSizesAndReloads(bool baseLanding, bool respawn)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        TestFixtures.LoadVerifiedManifest("strategic-event");
        var content = StrategicWorldConversionResearchTests.Content(respawn, baseLanding: baseLanding);
        foreach (var interrupted in new[] { false, true })
        {
            var seed = SeedForOutcome(content, baseLanding, interrupted);
            var initial = StrategicWorldConversionResearchTests.Snapshot(content, seed, baseLanding: baseLanding);
            initial = initial with
            {
                World = initial.World with
                {
                    Events = [new("EVENT_DESPAWN", 5790) { PreservationKey = "event:generated:EVENT_DESPAWN:2" },
                        new("EVENT_DESPAWN", 5820) { PreservationKey = "event:generated:EVENT_DESPAWN:3" }],
                    GeneratedEvents = new Dictionary<string, int>(StringComparer.Ordinal) { ["EVENT_DESPAWN"] = 3, ["REMOVED_EVENT"] = 2 },
                },
            };
            var stepped = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var hourly = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var batched = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(stepped, 1);
            AdvanceUnblocked(hourly, 1);
            AdvanceUnblocked(batched, 1);
            Assert.Equal(interrupted, stepped.Capture().CompletedResearch.Contains("BONUS"));
            stepped = Reload(content, stepped, seed, "arrival-research-endurance-transition.sav");
            var selected = stepped.Capture();
            for (var day = 1; day <= 3; day++)
            {
                for (var tick = 0; tick < 17_280; tick++) AdvanceUnblocked(stepped, 1);
                for (var hour = 0; hour < 24; hour++) AdvanceUnblocked(hourly, 720);
                AdvanceUnblocked(batched, 17_280);
                hourly = Reload(content, hourly, seed, "arrival-research-endurance-day.sav");
                var after = stepped.Capture();
                Assert.Equivalent(after, hourly.Capture(), strict: true);
                Assert.Equivalent(after, batched.Capture(), strict: true);
                Assert.Equivalent(selected.ResearchDiary, after.ResearchDiary, strict: true);
                Assert.Empty(after.World.Ufos);
                Assert.Empty(after.World.MissionSites);
                Assert.Empty(after.World.Missions);
                Assert.Equal(baseLanding && !interrupted ? 1 : 0, after.World.AlienBases.Count);
                Assert.Equal([5760 - day * 1440, 5790 - day * 1440], after.World.Events.Select(item => item.SpawnCountdown));
                Assert.Equal(["event:generated:EVENT_DESPAWN:2", "event:generated:EVENT_DESPAWN:3"],
                    after.World.Events.Select(item => item.PreservationKey));
                Assert.Equivalent(initial.World.GeneratedEvents, after.World.GeneratedEvents, strict: true);
            }
            for (var pass = 0; pass < 3; pass++)
                stepped = Reload(content, stepped, seed, "arrival-research-endurance-complete.sav");
            var end = stepped.Capture();
            var due = end with
            {
                Time = end.Time with { Hour = 0, Minute = 29, Second = 55 },
                World = end.World with { Events = [end.World.Events[0] with { SpawnCountdown = 30 }] },
            };
            AssertTimeBlocked(CampaignState.Restore(due, content, new SplitMix64RandomSource(seed)),
                "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
            var month = end with { Time = new(1, 31, 1, 1999, 23, 59, 55) };
            // Branch 6 S3: the month runs and its report holds the clock.
            var monthly = CampaignState.Restore(month, content, new SplitMix64RandomSource(seed));
            Assert.Single(monthly.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>());
            AssertTimeBlocked(monthly, CampaignState.MonthlyReportOpen);
        }
    }

    private static ulong SeedForOutcome(Oxce.Mods.Rulesets.Content.RuntimeContent content, bool baseLanding, bool interrupted)
    {
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldConversionResearchTests.Snapshot(content, seed, baseLanding: baseLanding);
            var probe = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(probe, 1);
            if (probe.Capture().CompletedResearch.Contains("BONUS") == interrupted) return seed;
        }
        Assert.Fail("The fixed seed range must exercise both research outcomes.");
        return 0;
    }
}
