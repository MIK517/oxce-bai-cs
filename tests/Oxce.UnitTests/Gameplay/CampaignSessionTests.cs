using Oxce.Core.Diagnostics;
using Oxce.Extensions;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignSessionTests
{
    private static readonly PlayerId First = new(1);
    private static readonly PlayerId Second = new(2);

    /// <summary>A test-only unit command; the first real unit commands arrive with the inventory (branch 3 S4).</summary>
    private sealed record TestUnitCommand(int UnitId) : IUnitCommand;

    private sealed class RecordingTarget : IActorCommandTarget
    {
        public List<(PlayerId Actor, ICampaignCommand Command)> Executed { get; } = [];

        public CampaignCommandResult Execute(ICampaignCommand command) => Execute(PlayerId.Local, command);

        public CampaignCommandResult Execute(PlayerId actor, ICampaignCommand command)
        {
            Executed.Add((actor, command));
            return new([new UfoContactLost(7)]);
        }
    }

    private sealed class Controllers(params (int Unit, PlayerId Player)[] owners) : IUnitControllers
    {
        public PlayerId? ControllerOf(int unitId) =>
            owners.Where(o => o.Unit == unitId).Select(o => (PlayerId?)o.Player).FirstOrDefault();
    }

    private sealed class Recorder : ISessionClient
    {
        public List<SessionEvents> Batches { get; } = [];
        public Action<SessionEvents>? Callback { get; set; }

        public void OnEvents(SessionEvents batch)
        {
            Batches.Add(batch);
            Callback?.Invoke(batch);
        }
    }

    private static CampaignActionBlocked Blocked(CampaignCommandResult result) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events));

    [Fact]
    public void AUnitCommandFromAPlayerWhoDoesNotControlTheUnitIsRefusedBeforeTheSimulation()
    {
        var target = new RecordingTarget();
        var session = new CampaignSession(target, new NoQueries()) { Controllers = new Controllers((10, First), (11, Second)) };
        using var one = session.Attach(First);
        using var two = session.Attach(Second);

        var refused = Blocked(two.Execute(new TestUnitCommand(10)));
        Assert.Equal(SessionReasons.UnitNotControlled, refused.Code);
        Assert.Empty(target.Executed);

        Assert.Equal(SessionReasons.UnitUnknown, Blocked(one.Execute(new TestUnitCommand(99))).Code);
        Assert.Empty(target.Executed);

        Assert.IsType<UfoContactLost>(Assert.Single(one.Execute(new TestUnitCommand(10)).Events));
        Assert.Equal((First, (ICampaignCommand)new TestUnitCommand(10)), Assert.Single(target.Executed));
    }

    [Fact]
    public void UnitCommandsAreRefusedWhenNoBattleSuppliesControllers()
    {
        var target = new RecordingTarget();
        var session = new CampaignSession(target, new NoQueries());
        using var one = session.Attach(First);

        Assert.Equal(SessionReasons.UnitCommandsUnavailable, Blocked(one.Execute(new TestUnitCommand(1))).Code);
        Assert.Empty(target.Executed);
    }

    [Fact]
    public void ADetachedPlayerIsRefusedAndAPlayerAttachesOnce()
    {
        var target = new RecordingTarget();
        var session = new CampaignSession(target, new NoQueries());
        var one = session.Attach(First);
        Assert.Throws<InvalidOperationException>(() => session.Attach(First));
        Assert.Equal([First], session.Players);

        one.Dispose();

        Assert.False(one.IsAttached);
        Assert.Equal(SessionReasons.PlayerNotAttached, Blocked(one.Execute(new AdvanceCampaignTime(1))).Code);
        Assert.Empty(target.Executed);
        Assert.Empty(session.Players);
        using var again = session.Attach(First);
        Assert.True(again.IsAttached);
    }

    [Fact]
    public void EveryAttachedClientReceivesACommittedEventWithItsSender()
    {
        var target = new RecordingTarget();
        var session = new CampaignSession(target, new NoQueries());
        var left = new Recorder();
        var right = new Recorder();
        using var one = session.Attach(First, left);
        using var two = session.Attach(Second, right);
        using var quiet = session.Attach(new PlayerId(3));

        var command = new AdvanceCampaignTime(1);
        two.Execute(command);

        var batch = Assert.Single(left.Batches);
        Assert.Equal(Second, batch.Actor);
        Assert.Same(command, batch.Command);
        Assert.IsType<UfoContactLost>(Assert.Single(batch.Events));
        Assert.Same(batch, Assert.Single(right.Batches));
    }

    [Fact]
    public void ARefusedCommandBroadcastsNothing()
    {
        var session = new CampaignSession(new RecordingTarget(), new NoQueries());
        var listener = new Recorder();
        using var one = session.Attach(First, listener);

        one.Execute(new TestUnitCommand(1));

        Assert.Empty(listener.Batches);
    }

    [Fact]
    public void ABrokenClientIsDetachedWithoutUndoingTheCommandOrStarvingTheOthers()
    {
        var session = new CampaignSession(new RecordingTarget(), new NoQueries());
        var broken = new Recorder { Callback = static _ => throw new InvalidOperationException("boom") };
        var healthy = new Recorder();
        using var one = session.Attach(First, broken);
        using var two = session.Attach(Second, healthy);

        var result = two.Execute(new AdvanceCampaignTime(1));

        Assert.IsType<UfoContactLost>(Assert.Single(result.Events));
        Assert.Single(healthy.Batches);
        Assert.Equal([Second], session.Players);
        Assert.Contains("boom", Assert.Single(session.Faults), StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackCannotSubmitCommands()
    {
        var session = new CampaignSession(new RecordingTarget(), new NoQueries());
        var listener = new Recorder();
        using var one = session.Attach(First, listener);
        listener.Callback = _ => one.Execute(new AdvanceCampaignTime(1));

        one.Execute(new AdvanceCampaignTime(1));

        Assert.Contains("callback", Assert.Single(session.Faults), StringComparison.Ordinal);
    }

    [Fact]
    public void ARealCampaignBroadcastsItsTimeAdvanceAndKeepsTheSingleLocalPlayerWorking()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var session = new CampaignSession(campaign, campaign);
        var watcher = new Recorder();
        using var local = session.Attach(PlayerId.Local);
        using var other = session.Attach(Second, watcher);

        var result = local.Execute(new AdvanceCampaignTime(12));

        Assert.Equal(campaign.QueryOverview().Time, local.QueryOverview().Time);
        var advanced = Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events));
        var received = Assert.IsType<CampaignTimeAdvanced>(Assert.Single(Assert.Single(watcher.Batches).Events));
        Assert.Equal(advanced.Current, received.Current);
        Assert.Equal(PlayerId.Local, watcher.Batches[0].Actor);
    }

    [Fact]
    public void AQuoteSurvivesAnotherActorsQuoteAndATimeAdvance()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var session = new CampaignSession(campaign, campaign);
        using var one = session.Attach(First);
        using var two = session.Attach(Second);
        var mine = Quoted(one.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));

        var theirs = Quoted(two.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));
        Assert.NotEqual(mine.Id, theirs.Id);
        Assert.IsType<CampaignTimeAdvanced>(Assert.Single(one.Execute(new AdvanceCampaignTime(12)).Events));

        var recruit = mine.Rows.Single(r => r.RuleId == "RECRUIT");
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(
            one.Execute(new SubmitLogisticsOrder(mine.Id, [new(recruit.Id, 1)])).Events));
    }

    [Fact]
    public void AQuoteBelongsToItsActor()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var session = new CampaignSession(campaign, campaign);
        using var one = session.Attach(First);
        using var two = session.Attach(Second);
        var mine = Quoted(one.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));
        var recruit = mine.Rows.Single(r => r.RuleId == "RECRUIT");
        var before = campaign.Capture();

        var stolen = Blocked(two.Execute(new SubmitLogisticsOrder(mine.Id, [new(recruit.Id, 1)])));

        Assert.Contains("expired", stolen.Reason, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(
            one.Execute(new SubmitLogisticsOrder(mine.Id, [new(recruit.Id, 1)])).Events));
    }

    [Fact]
    public void AQuoteWhoseBasisChangedIsRefusedOnSubmitWithNoChange()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var session = new CampaignSession(campaign, campaign);
        using var one = session.Attach(First);
        using var two = session.Attach(Second);
        var mine = Quoted(one.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));
        var theirs = Quoted(two.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));
        var recruit = theirs.Rows.Single(r => r.RuleId == "RECRUIT");
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(
            two.Execute(new SubmitLogisticsOrder(theirs.Id, [new(recruit.Id, 1)])).Events));
        var before = campaign.Capture();

        var stale = Blocked(one.Execute(new SubmitLogisticsOrder(mine.Id, [new(recruit.Id, 1)])));

        Assert.Contains("expired", stale.Reason, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ASubmittedQuoteIsConsumedAndLocalCommandsActAsTheLocalPlayer()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var quote = Quoted(campaign.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));
        var recruit = quote.Rows.Single(r => r.RuleId == "RECRUIT");

        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(
            campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(recruit.Id, 1)])).Events));

        Assert.Contains("expired", Blocked(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(recruit.Id, 1)]))).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalFromTheSimulationReachesOnlyTheSender()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var session = new CampaignSession(campaign, campaign);
        var watcher = new Recorder();
        using var one = session.Attach(First);
        using var two = session.Attach(Second, watcher);

        Assert.Contains("expired", Blocked(one.Execute(new SubmitLogisticsOrder(99, [new(0, 1)]))).Reason,
            StringComparison.Ordinal);

        Assert.Empty(watcher.Batches);
    }

    [Fact]
    public void AWrongQuoteIdKeepsTheOpenQuote()
    {
        var campaign = CampaignLogisticsTests.Create(CampaignLogisticsTests.LoadFixture());
        var mine = Quoted(campaign.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Purchase)));
        var recruit = mine.Rows.Single(r => r.RuleId == "RECRUIT");

        Blocked(campaign.Execute(new SubmitLogisticsOrder(mine.Id + 100, [new(recruit.Id, 1)])));

        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(
            campaign.Execute(new SubmitLogisticsOrder(mine.Id, [new(recruit.Id, 1)])).Events));
    }

    [Fact]
    public void TheExtensionSessionPassesTheActorOnWhenTheTargetTakesOne()
    {
        var empty = Directory.CreateTempSubdirectory("oxce-no-extensions-");
        try
        {
            using var host = ManagedExtensionHost.LoadFromDirectory(
                empty.FullName, new DiagnosticCollector(), cancellationToken: TestContext.Current.CancellationToken);
            var target = new RecordingTarget();
            using var extensions = host.AttachCampaign(new NoQueries(), target, TestContext.Current.CancellationToken);

            extensions.Execute(Second, new AdvanceCampaignTime(1));
            extensions.Execute(new AdvanceCampaignTime(1));

            Assert.Equal([Second, PlayerId.Local], target.Executed.Select(e => e.Actor));

            var plain = new PlainTarget();
            using var fallback = host.AttachCampaign(new NoQueries(), plain, TestContext.Current.CancellationToken);
            fallback.Execute(Second, new AdvanceCampaignTime(1));
            Assert.Equal(1, plain.Count);
        }
        finally { empty.Delete(recursive: true); }
    }

    [Fact]
    public void ExtensionCommandsGoThroughTheSessionAndEveryClientHearsThem()
    {
        var empty = Directory.CreateTempSubdirectory("oxce-no-extensions-");
        try
        {
            using var host = ManagedExtensionHost.LoadFromDirectory(
                empty.FullName, new DiagnosticCollector(), cancellationToken: TestContext.Current.CancellationToken);
            var target = new RecordingTarget();
            using var extensions = host.AttachCampaign(new NoQueries(), target, TestContext.Current.CancellationToken);
            var session = new CampaignSession(extensions, new NoQueries());
            var recorder = new Recorder();
            using var player = session.Attach(First, recorder);

            ((Oxce.Extensions.Abstractions.IExtensionCampaignCommands)extensions)
                .Execute(new Oxce.Extensions.Abstractions.ExtensionAdvanceCampaignTime(3));

            var batch = Assert.Single(recorder.Batches);
            Assert.Equal(PlayerId.Host, batch.Actor);
            Assert.Equal(3, Assert.IsType<AdvanceCampaignTime>(batch.Command).FiveSecondTicks);
            Assert.Equal(PlayerId.Host, Assert.Single(target.Executed).Actor);

            // Extension advances are host commands: a cover or a halt of the clock does not stop them.
            session.Clock.SetCovered(First, true);
            session.Clock.Halt("halted");
            ((Oxce.Extensions.Abstractions.IExtensionCampaignCommands)extensions)
                .Execute(new Oxce.Extensions.Abstractions.ExtensionAdvanceCampaignTime(1));
            Assert.Equal(2, target.Executed.Count);
        }
        finally { empty.Delete(recursive: true); }
    }

    [Fact]
    public void AHostCommandFromAClientCallbackIsRefusedAsReentrant()
    {
        var session = new CampaignSession(new RecordingTarget(), new NoQueries());
        var recorder = new Recorder();
        using var player = session.Attach(First, recorder);
        InvalidOperationException? caught = null;
        recorder.Callback = _ =>
        {
            try { session.ExecuteAsHost(new AdvanceCampaignTime(1)); }
            catch (InvalidOperationException error) { caught = error; }
        };
        player.Execute(new AdvanceCampaignTime(1));
        Assert.NotNull(caught);
    }

    [Fact]
    public void ADetachedPlayersCoverNoLongerPausesTheClock()
    {
        var session = new CampaignSession(new RecordingTarget(), new NoQueries());
        var player = session.Attach(First);
        session.Clock.SetCovered(First, true);
        Assert.True(session.Clock.Covered);
        player.Dispose();
        Assert.False(session.Clock.Covered);
    }

    [Fact]
    public void TheHostControlsNoUnit()
    {
        var target = new RecordingTarget();
        var session = new CampaignSession(target, new NoQueries()) { Controllers = new Controllers((10, First)) };
        var refused = Blocked(session.ExecuteAsHost(new TestUnitCommand(10)));
        Assert.Equal(SessionReasons.UnitNotControlled, refused.Code);
        Assert.Empty(target.Executed);
    }

    private sealed class PlainTarget : ICampaignCommandTarget
    {
        public int Count { get; private set; }

        public CampaignCommandResult Execute(ICampaignCommand command)
        {
            Count++;
            return new([]);
        }
    }

    private static LogisticsQuote Quoted(CampaignCommandResult result) =>
        Assert.IsType<LogisticsQuoted>(Assert.Single(result.Events)).Quote;

    private sealed class NoQueries : ICampaignQuery
    {
        public CampaignOverview QueryOverview() => throw new NotSupportedException();
        public CampaignStores QueryStores(int baseId) => throw new NotSupportedException();
    }
}
