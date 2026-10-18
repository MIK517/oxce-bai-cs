using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S2 (ADR 0035): a tick with a stage point preflights the rest of the tick on a shadow copy that has run
/// the handlers down to the stage level. A refusal there stops time before the tick with the campaign and its
/// random state untouched; otherwise the real tick runs and its own recheck agrees. The test assemblies also compare
/// every shadow's snapshot with the original's (<see cref="CampaignState.VerifyStagedSnapshots"/>).
/// </summary>
public sealed class StrategicStagedTickTests
{
    private const string Refusal = "Test stage refusal after the day handlers.";

    [Fact]
    public void ARefusalAfterTheStagePointStopsBeforeTheTickWithNothingChanged()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content) with { Time = new CampaignTime(5, 1, 1, 1999, 23, 59, 55) };
        var campaign = WithDayStage(content, snapshot, refuseAfterDay: snapshot.DaysPassed + 1);
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(3));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(Refusal, Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(before.RandomState, campaign.Random.State);
    }

    [Fact]
    public void WhenTheShadowPassesTheRealTickRunsAndItsRecheckAgrees()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content) with { Time = new CampaignTime(5, 1, 1, 1999, 23, 59, 55) };
        var staged = WithDayStage(content, snapshot, refuseAfterDay: -1);
        var direct = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        var stagedBefore = CampaignState.StagedTicksOnThisThread;

        Unstaged(() => AdvanceUnblocked(staged, 3));
        var stagedTicks = CampaignState.StagedTicksOnThisThread - stagedBefore;
        Unstaged(() => AdvanceUnblocked(direct, 3));

        Assert.Equal(1, stagedTicks);
        Assert.Equal(snapshot.DaysPassed + 1, staged.DaysPassed);
        Assert.Equivalent(direct.Capture(), staged.Capture(), strict: true);
    }

    [Fact]
    public void StagingEveryTickLeavesARunUnchanged()
    {
        // Two days of a landed UFO's lifecycle, every tick above five seconds staged on a shadow copy.
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        var direct = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        var staged = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        const int ticks = 2 * 24 * 720;
        List<string> directEvents = [];
        Unstaged(() => directEvents = Run(direct, ticks));
        var stagedBefore = CampaignState.StagedTicksOnThisThread;
        var stagedEvents = Staged(() => Run(staged, ticks));

        // Every ten-minute boundary of the two days is staged.
        Assert.Equal(2 * 24 * 6, CampaignState.StagedTicksOnThisThread - stagedBefore);
        Assert.Equal(snapshot.Time.Day + 2, staged.Time.Day);
        Assert.Equal(directEvents, stagedEvents);
        Assert.Equivalent(direct.Capture(), staged.Capture(), strict: true);
    }

    [Fact]
    public void AShadowCopyCarriesTheWindowsAndQueuesThatSavesDrop()
    {
        // An ignored contact is transient world state that half-hour detection reads (ADR 0035): a shadow must see it.
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var lifecycle = CreateLifecycleSnapshot(content);
        var snapshot = lifecycle with { World = lifecycle.World with { Ufos = [lifecycle.World.Ufos[0] with { Detected = true }] } };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        Assert.Single(campaign.Execute(new IgnoreUfoContact(9, 3)).Events.OfType<UfoContactIgnored>());

        var shadow = campaign.CreateShadow();

        Assert.True(Ufo(campaign).DetectionAlertIgnored);
        Assert.True(Ufo(shadow).DetectionAlertIgnored);
        Assert.Equivalent(campaign.Capture(), shadow.Capture(), strict: true);

        static CampaignWorldTarget Ufo(CampaignState state) =>
            Assert.Single(state.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets, static target => target.Kind == WorldTargetKind.Ufo);
    }

    private static List<string> Run(CampaignState campaign, int ticks)
    {
        var events = new List<string>();
        while (ticks > 0)
        {
            var result = campaign.Execute(new AdvanceCampaignTime(ticks));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            events.AddRange(result.Events.Select(static e => e.GetType().Name + JsonSerializer.Serialize(e, e.GetType())));
            var advanced = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
            Assert.InRange(advanced, 1, ticks);
            ticks -= advanced;
        }
        return events;
    }

    private static void Unstaged(Action action)
    {
        var previous = CampaignState.StageEveryTick;
        CampaignState.StageEveryTick = false;
        try { action(); }
        finally { CampaignState.StageEveryTick = previous; }
    }

    private static T Staged<T>(Func<T> action)
    {
        var previous = CampaignState.StageEveryTick;
        CampaignState.StageEveryTick = true;
        try { return action(); }
        finally { CampaignState.StageEveryTick = previous; }
    }

    /// <summary>A campaign with a test stage point after the day handlers and a refusal that sees their result.</summary>
    private static CampaignState WithDayStage(RuntimeContent content, CampaignSnapshot snapshot, int refuseAfterDay)
    {
        var previous = CampaignState.TestCapabilities.Value;
        CampaignState.TestCapabilities.Value = campaign => [new DelegatedCampaignCapability(registry =>
        {
            registry.StagePoint(CampaignTimeTrigger.OneDay, "test day stage", static (_, highest) => highest >= CampaignTimeTrigger.OneDay);
            registry.Preflight(9_999, "test stage refusal", (_, highest) =>
                highest < CampaignTimeTrigger.OneDay && campaign.DaysPassed == refuseAfterDay ? Refusal : null);
        })];
        try { return CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState)); }
        finally { CampaignState.TestCapabilities.Value = previous; }
    }
}
