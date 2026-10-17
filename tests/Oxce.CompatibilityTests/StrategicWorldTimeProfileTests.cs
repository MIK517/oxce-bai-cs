using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldTimeProfileTests
{
    [Fact]
    public void AProfileTimesEveryPhaseWithoutChangingTheCampaign()
    {
        // Counts the calls of unstaged ticks, also in a staging verification run (ADR 0035).
        CampaignState.StageEveryTick = false;
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Profile").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_READY", Fuel = 100, Weapons = [], Items = new Dictionary<string, int>() },
        });
        var plain = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));
        var profiled = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));
        var profile = new CampaignTimeProfile();
        profiled.TimeProfile = profile;
        foreach (var campaign in new[] { plain, profiled })
        {
            var owner = campaign.Capture().Bases[0].Id;
            Assert.All(campaign.Execute(new DispatchCraftToWaypoint(owner, "SHIP", 1, 0.3, 0.1)).Events,
                evt => Assert.IsNotType<CampaignActionBlocked>(evt));
            AdvanceUnblocked(campaign, 240);
        }

        Assert.Equivalent(plain.Capture(), profiled.Capture(), strict: true);
        Assert.Same(profile, profiled.TimeProfile);
        Assert.Null(plain.TimeProfile);
        var calls = profile.Entries.ToDictionary(static entry => entry.Name, static entry => entry.Calls);
        Assert.Equal(240, calls[CampaignTimeProfile.PreflightPrefix + "world simulation"]);
        Assert.Equal(240, calls[CampaignTimeProfile.WorldPrefix + "UfoMovementReason"]);
        Assert.Equal(240, calls[CampaignTimeProfile.WorldPrefix + "EventSchedulingReason"]);
        Assert.Equal(240, calls[CampaignTimeProfile.ApplyPrefix + "world craft movement"]);
        Assert.All(profile.Entries, static entry => Assert.True(entry.Elapsed >= TimeSpan.Zero));

        profile.Reset();
        Assert.All(profile.Entries, static entry => Assert.Equal((TimeSpan.Zero, 0L), (entry.Elapsed, entry.Calls)));
    }
}
