using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class SessionClockTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    [Fact]
    public void FiveSecondSpeedRunsOneTickPerGeoClockStep()
    {
        var commands = new ScriptedCommands();
        var clock = new SessionClock(commands, () => TimeSpan.Zero);
        for (var frame = 0; frame < 4; frame++) Assert.Equal(0, clock.Tick(Frame).Ticks);
        Assert.Empty(commands.Requests);
        Assert.Equal(1, clock.Tick(Frame).Ticks);
        Assert.Equal([(1, CampaignTimeSpeed.FiveSeconds)], commands.Requests);
        // Banking is limited to one extra step when the host stalls.
        clock.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(1, clock.Tick(TimeSpan.Zero).Ticks);
        Assert.Equal(0, clock.Tick(TimeSpan.Zero).Ticks);
    }

    [Theory]
    [InlineData(CampaignTimeSpeed.OneMinute, 12)]
    [InlineData(CampaignTimeSpeed.FiveMinutes, 60)]
    [InlineData(CampaignTimeSpeed.ThirtyMinutes, 360)]
    [InlineData(CampaignTimeSpeed.OneHour, 720)]
    [InlineData(CampaignTimeSpeed.OneDay, 17_280)]
    public void EachReferenceSpeedOwesItsTimeSpanWithTheSelectedSpeedContext(CampaignTimeSpeed speed, int ticks)
    {
        var commands = new ScriptedCommands();
        var clock = new SessionClock(commands, () => TimeSpan.Zero);
        clock.Select(speed);
        Assert.Equal(ticks, SessionClock.StepTicks(speed));
        Assert.Equal(ticks, clock.Tick(SessionClock.GeoClock).Ticks);
        Assert.Equal(ticks, commands.Requests.Sum(request => request.Ticks));
        Assert.All(commands.Requests, request => Assert.Equal(speed, request.Speed));
        Assert.Equal(0, clock.Owed);
    }

    [Fact]
    public void AStepSpreadsOverFramesWhenTheBudgetRunsOut()
    {
        var commands = new ScriptedCommands();
        var now = TimeSpan.Zero;
        var clock = new SessionClock(commands, () => now += TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(12));
        clock.Select(CampaignTimeSpeed.OneDay);
        var first = clock.Tick(SessionClock.GeoClock).Ticks;
        Assert.InRange(first, 1, 17_279);
        Assert.Equal(17_280 - first, clock.Owed);
        var total = first;
        // The rest of the step needs no new clock time.
        while (clock.Owed > 0) total += clock.Tick(TimeSpan.Zero).Ticks;
        Assert.Equal(17_280, total);
    }

    [Fact]
    public void NotificationsPauseAndDropTheRestOfTheStep()
    {
        var commands = new ScriptedCommands { PauseAfter = 5, Notification = new UfoContactLost(3) };
        var clock = new SessionClock(commands, () => TimeSpan.Zero);
        clock.Select(CampaignTimeSpeed.OneMinute);
        var step = clock.Tick(SessionClock.GeoClock);
        Assert.Equal(5, step.Ticks);
        Assert.IsType<UfoContactLost>(Assert.Single(step.Events));
        Assert.Equal(0, clock.Owed);
        Assert.True(clock.Running);
        commands.PauseAfter = null;
        Assert.Equal(0, clock.Tick(Frame).Ticks);
        Assert.Equal(12, clock.Tick(SessionClock.GeoClock).Ticks);
    }

    [Fact]
    public void ARefusedTickHaltsUntilASpeedIsSelectedAndResetReturnsToFiveSeconds()
    {
        var commands = new ScriptedCommands { Blocked = "Integrated monthly progression is guarded." };
        var clock = new SessionClock(commands, () => TimeSpan.Zero);
        clock.Select(CampaignTimeSpeed.OneHour);
        var step = clock.Tick(SessionClock.GeoClock);
        Assert.Equal("Integrated monthly progression is guarded.", step.BlockedReason);
        Assert.False(clock.Running);
        Assert.Equal(step.BlockedReason, clock.HaltReason);
        commands.Blocked = null;
        var requests = commands.Requests.Count;
        Assert.Equal(0, clock.Tick(SessionClock.GeoClock).Ticks);
        Assert.Equal(requests, commands.Requests.Count);
        // timerReset changes the speed but does not resume a halted clock; selecting a speed does.
        clock.Reset();
        Assert.Equal((CampaignTimeSpeed.FiveSeconds, false), (clock.Speed, clock.Running));
        Assert.Equal(0, clock.Tick(SessionClock.GeoClock).Ticks);
        clock.Select(CampaignTimeSpeed.FiveSeconds);
        Assert.Equal((CampaignTimeSpeed.FiveSeconds, true, null), (clock.Speed, clock.Running, clock.HaltReason));
        Assert.Equal(1, clock.Tick(SessionClock.GeoClock).Ticks);
    }

    [Fact]
    public void AClientCoverPausesTheHostClockUntilEveryCoverIsReleased()
    {
        var commands = new ScriptedCommands();
        var clock = new SessionClock(commands, () => TimeSpan.Zero);
        var other = new PlayerId(2);
        clock.SetCovered(PlayerId.Local, true);
        clock.SetCovered(other, true);
        Assert.True(clock.Covered);
        Assert.Equal(0, clock.Tick(SessionClock.GeoClock).Ticks);
        Assert.Empty(commands.Requests);
        clock.SetCovered(PlayerId.Local, false);
        Assert.Equal(0, clock.Tick(SessionClock.GeoClock).Ticks);
        clock.SetCovered(other, false);
        // Covered time was not banked: one step still needs the full clock.
        Assert.Equal(0, clock.Tick(Frame).Ticks);
        Assert.Equal(1, clock.Tick(SessionClock.GeoClock).Ticks);
    }

    [Fact]
    public void ASessionClockSendsItsAdvancesThroughTheSessionAndEveryClientHearsThem()
    {
        var target = new ScriptedActorTarget();
        var session = new CampaignSession(target, new NoQueries(), () => TimeSpan.Zero);
        var heard = new List<SessionEvents>();
        using var local = session.Attach(PlayerId.Local, new Listener(heard));
        using var second = session.Attach(new PlayerId(2), new Listener(heard));
        Assert.Equal(1, session.Clock.Tick(SessionClock.GeoClock).Ticks);
        Assert.Equal(PlayerId.Host, Assert.Single(target.Actors));
        Assert.Equal(2, heard.Count);
        Assert.All(heard, batch => Assert.Equal(PlayerId.Host, batch.Actor));
        Assert.Same(session.Clock, local.Clock);
    }

    [Fact]
    public void ABattleRefusalStopsTheSessionClockUntilASpeedIsSelected()
    {
        var target = new ScriptedActorTarget { Blocked = "A tactical battle is in progress." };
        var session = new CampaignSession(target, new NoQueries(), () => TimeSpan.Zero);
        var step = session.Clock.Tick(SessionClock.GeoClock);
        Assert.Equal("A tactical battle is in progress.", step.BlockedReason);
        Assert.False(session.Clock.Running);
        var requests = target.Actors.Count;
        Assert.Equal(0, session.Clock.Tick(SessionClock.GeoClock).Ticks);
        Assert.Equal(requests, target.Actors.Count);
    }

    [Fact]
    public void AManualAdvanceFollowsTheHostPolicy()
    {
        var commands = new ScriptedCommands();
        var clock = new SessionClock(commands, () => TimeSpan.Zero);
        clock.SetCovered(PlayerId.Local, true);
        Assert.Equal(SessionReasons.ClockCovered, Assert.IsType<CampaignActionBlocked>(Assert.Single(clock.Advance(12).Events)).Code);
        Assert.Empty(commands.Requests);
        clock.SetCovered(PlayerId.Local, false);
        Assert.IsType<CampaignTimeAdvanced>(Assert.Single(clock.Advance(12).Events));
        commands.Blocked = "battle";
        Assert.Contains(clock.Advance(12).Events, e => e is CampaignActionBlocked);
        Assert.False(clock.Running);
        commands.Blocked = null;
        Assert.Equal(SessionReasons.ClockHalted, Assert.IsType<CampaignActionBlocked>(Assert.Single(clock.Advance(12).Events)).Code);
        clock.Select(CampaignTimeSpeed.FiveSeconds);
        Assert.IsType<CampaignTimeAdvanced>(Assert.Single(clock.Advance(12).Events));
    }

    private sealed class Listener(List<SessionEvents> sink) : ISessionClient
    {
        public void OnEvents(SessionEvents batch) => sink.Add(batch);
    }

    private sealed class ScriptedActorTarget : IActorCommandTarget
    {
        private readonly ScriptedCommands _inner = new();
        public List<PlayerId> Actors { get; } = [];
        public string? Blocked { get => _inner.Blocked; set => _inner.Blocked = value; }
        public CampaignCommandResult Execute(ICampaignCommand command) => Execute(PlayerId.Local, command);

        public CampaignCommandResult Execute(PlayerId actor, ICampaignCommand command)
        {
            Actors.Add(actor);
            return _inner.Execute(command);
        }
    }

    private sealed class NoQueries : ICampaignQuery
    {
        public CampaignOverview QueryOverview() => throw new NotSupportedException();
        public CampaignStores QueryStores(int baseId) => throw new NotSupportedException();
    }

    private sealed class ScriptedCommands : ICampaignCommandTarget
    {
        private static readonly CampaignTime Time = new(1, 1, 1, 1999, 0, 0, 0);
        public List<(int Ticks, CampaignTimeSpeed Speed)> Requests { get; } = [];
        public int? PauseAfter { get; set; }
        public ICampaignEvent? Notification { get; set; }
        public string? Blocked { get; set; }

        public CampaignCommandResult Execute(ICampaignCommand command)
        {
            var advance = Assert.IsType<AdvanceCampaignTime>(command);
            Requests.Add((advance.FiveSecondTicks, advance.SelectedSpeed));
            var done = Blocked is not null ? 0 : Math.Min(advance.FiveSecondTicks, PauseAfter ?? int.MaxValue);
            List<ICampaignEvent> events = [new CampaignTimeAdvanced(Time, Time, new(done, done, 0, 0, 0, 0, 0))];
            if (Blocked is not null) events.Add(new CampaignActionBlocked(Blocked));
            else if (PauseAfter is not null && Notification is not null) events.Add(Notification);
            return new(events);
        }
    }
}
