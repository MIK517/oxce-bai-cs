using Oxce.Engine.Geoscape;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GeoscapeTimeControllerTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    [Fact]
    public void FiveSecondSpeedRunsOneTickPerGeoClockStep()
    {
        var commands = new ScriptedCommands();
        var clock = new GeoscapeTimeController(() => TimeSpan.Zero);
        for (var frame = 0; frame < 4; frame++) Assert.Equal(0, clock.Tick(Frame, commands).Ticks);
        Assert.Empty(commands.Requests);
        Assert.Equal(1, clock.Tick(Frame, commands).Ticks);
        Assert.Equal([(1, CampaignTimeSpeed.FiveSeconds)], commands.Requests);
        // Banking is limited to one extra step when the host stalls.
        clock.Tick(TimeSpan.FromSeconds(1), commands);
        Assert.Equal(1, clock.Tick(TimeSpan.Zero, commands).Ticks);
        Assert.Equal(0, clock.Tick(TimeSpan.Zero, commands).Ticks);
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
        var clock = new GeoscapeTimeController(() => TimeSpan.Zero);
        clock.Select(speed);
        Assert.Equal(ticks, GeoscapeTimeController.StepTicks(speed));
        Assert.Equal(ticks, clock.Tick(GeoscapeTimeController.GeoClock, commands).Ticks);
        Assert.Equal(ticks, commands.Requests.Sum(request => request.Ticks));
        Assert.All(commands.Requests, request => Assert.Equal(speed, request.Speed));
        Assert.Equal(0, clock.Owed);
    }

    [Fact]
    public void AStepSpreadsOverFramesWhenTheBudgetRunsOut()
    {
        var commands = new ScriptedCommands();
        var now = TimeSpan.Zero;
        var clock = new GeoscapeTimeController(() => now += TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(12));
        clock.Select(CampaignTimeSpeed.OneDay);
        var first = clock.Tick(GeoscapeTimeController.GeoClock, commands).Ticks;
        Assert.InRange(first, 1, 17_279);
        Assert.Equal(17_280 - first, clock.Owed);
        var total = first;
        // The rest of the step needs no new clock time.
        while (clock.Owed > 0) total += clock.Tick(TimeSpan.Zero, commands).Ticks;
        Assert.Equal(17_280, total);
    }

    [Fact]
    public void NotificationsPauseAndDropTheRestOfTheStep()
    {
        var commands = new ScriptedCommands { PauseAfter = 5, Notification = new UfoContactLost(3) };
        var clock = new GeoscapeTimeController(() => TimeSpan.Zero);
        clock.Select(CampaignTimeSpeed.OneMinute);
        var step = clock.Tick(GeoscapeTimeController.GeoClock, commands);
        Assert.Equal(5, step.Ticks);
        Assert.IsType<UfoContactLost>(Assert.Single(step.Events));
        Assert.Equal(0, clock.Owed);
        Assert.True(clock.Running);
        commands.PauseAfter = null;
        Assert.Equal(0, clock.Tick(Frame, commands).Ticks);
        Assert.Equal(12, clock.Tick(GeoscapeTimeController.GeoClock, commands).Ticks);
    }

    [Fact]
    public void ARefusedTickHaltsUntilASpeedIsSelectedAndResetReturnsToFiveSeconds()
    {
        var commands = new ScriptedCommands { Blocked = "Integrated monthly progression is guarded." };
        var clock = new GeoscapeTimeController(() => TimeSpan.Zero);
        clock.Select(CampaignTimeSpeed.OneHour);
        var step = clock.Tick(GeoscapeTimeController.GeoClock, commands);
        Assert.Equal("Integrated monthly progression is guarded.", step.BlockedReason);
        Assert.False(clock.Running);
        Assert.Equal(step.BlockedReason, clock.HaltReason);
        commands.Blocked = null;
        var requests = commands.Requests.Count;
        Assert.Equal(0, clock.Tick(GeoscapeTimeController.GeoClock, commands).Ticks);
        Assert.Equal(requests, commands.Requests.Count);
        clock.Reset();
        Assert.Equal((CampaignTimeSpeed.FiveSeconds, true, null), (clock.Speed, clock.Running, clock.HaltReason));
        Assert.Equal(1, clock.Tick(GeoscapeTimeController.GeoClock, commands).Ticks);
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
