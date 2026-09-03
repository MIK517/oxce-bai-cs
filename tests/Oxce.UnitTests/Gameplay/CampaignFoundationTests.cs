using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignFoundationTests
{
    [Fact]
    public void FiveSecondCalendarMatchesReferenceTriggerPrecedenceAndLeapYear()
    {
        var time = new CampaignTime(7, 29, 2, 2000, 23, 59, 55);

        var next = time.Advance(out var trigger);

        Assert.Equal(new CampaignTime(1, 1, 3, 2000, 0, 0, 0), next);
        Assert.Equal(CampaignTimeTrigger.OneMonth, trigger);
    }

    [Fact]
    public void NewCampaignCreatesLinkedWorldAndExecutesValidatedCommands()
    {
        var content = LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            Request(),
            new SplitMix64RandomSource(42),
            new FixedClock());

        var initial = campaign.Capture();
        Assert.Equal(6_000_000, Assert.Single(initial.Funds));
        Assert.Equal("COUNTRY", Assert.Single(initial.Countries).RuleId);
        Assert.Equal("REGION", Assert.Single(initial.Regions).RuleId);
        var startingBase = Assert.Single(initial.Bases);
        Assert.Equal("FACILITY", Assert.Single(startingBase.Facilities).RuleId);
        Assert.Equal("CRAFT", Assert.Single(startingBase.Crafts).RuleId);
        Assert.Equal(3, Assert.Single(startingBase.Crafts).Id);
        Assert.Equal(3, startingBase.Soldiers.Count);
        Assert.Equal(5, startingBase.Items["ITEM"]);

        var placed = campaign.Execute(new PlaceStartingBase(0, "Alpha", 1.25, -0.5));
        Assert.IsType<StartingBasePlaced>(Assert.Single(placed.Events));
        var advanced = campaign.Execute(new AdvanceCampaignTime(12));
        var timeAdvanced = Assert.IsType<CampaignTimeAdvanced>(Assert.Single(advanced.Events));
        Assert.Equal(12, timeAdvanced.Summary.TickCount);
        Assert.Equal(12, timeAdvanced.Summary.FiveSeconds);
        Assert.Equal(7, campaign.Capture().Time.Minute);
        Assert.Throws<InvalidOperationException>(() =>
            campaign.Execute(new PlaceStartingBase(0, "Again", 1, 0)));
    }

    [Fact]
    public void RestoreValidatesCompleteGraphBeforePublishing()
    {
        var content = LoadFixture();
        var campaign = CampaignFactory.Create(
            content, Request(), new SplitMix64RandomSource(7), new FixedClock());
        var snapshot = campaign.Capture();
        var facility = Assert.Single(Assert.Single(snapshot.Bases).Facilities);
        var invalidBase = Assert.Single(snapshot.Bases) with
        {
            Facilities = Array.AsReadOnly(new[] { facility, facility }),
        };
        var invalid = snapshot with { Bases = Array.AsReadOnly(new[] { invalidBase }) };

        Assert.Throws<InvalidDataException>(() =>
            CampaignState.Restore(invalid, content, new SplitMix64RandomSource(0)));

        var restored = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(0));
        Assert.Equivalent(snapshot, restored.Capture(), strict: true);
    }

    [Fact]
    public void FailedRestoreDoesNotPublishRandomState()
    {
        var content = LoadFixture();
        var campaign = CampaignFactory.Create(
            content, Request(), new SplitMix64RandomSource(7), new FixedClock());
        var snapshot = campaign.Capture();
        var random = new SplitMix64RandomSource(99);
        var previous = random.State;
        var invalid = snapshot with { Funds = Array.AsReadOnly(Array.Empty<long>()) };

        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(invalid, content, random));
        Assert.Equal(previous, random.State);
    }

    [Fact]
    public void LongRunningTimeSimulationIsDeterministic()
    {
        var content = LoadFixture();
        var first = CampaignFactory.Create(content, Request(), new SplitMix64RandomSource(7), new FixedClock());
        var second = CampaignFactory.Create(content, Request(), new SplitMix64RandomSource(7), new FixedClock());

        first.Execute(new AdvanceCampaignTime(200_000));
        second.Execute(new AdvanceCampaignTime(200_000));

        Assert.Equivalent(first.Capture(), second.Capture(), strict: true);
        Assert.True(first.Capture().DaysPassed > 0);
    }

    [Fact]
    public void TimeAdvanceRetainsConstantSizeSummaryAndReplaysOrderedTriggers()
    {
        var campaign = CampaignFactory.Create(
            LoadFixture(), Request(), new SplitMix64RandomSource(7), new FixedClock());
        campaign.Execute(new AdvanceCampaignTime(1));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var result = campaign.Execute(new AdvanceCampaignTime(CampaignState.MaximumCommandTicks));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var advanced = Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events));
        Assert.InRange(allocated, 0, 16_384);
        Assert.Equal(CampaignState.MaximumCommandTicks, advanced.Summary.TickCount);
        var replayed = new int[Enum.GetValues<CampaignTimeTrigger>().Length];
        foreach (var trigger in advanced.Triggers) replayed[(int)trigger]++;
        foreach (var trigger in Enum.GetValues<CampaignTimeTrigger>())
            Assert.Equal(advanced.Summary.Count(trigger), replayed[(int)trigger]);
        Assert.Equal(advanced.Summary.TickCount, replayed.Sum());
    }

    [Fact]
    public void MinimalCampaignViewPlacesBaseAdvancesTimeAndSuppressesIdleWork()
    {
        var campaign = CampaignFactory.Create(
            LoadFixture(), Request(), new SplitMix64RandomSource(7), new FixedClock());
        var client = new CampaignOverviewClient(campaign, campaign);
        var initialRevision = client.PresentationRevision;

        client.Tick(TimeSpan.FromMilliseconds(16));
        Assert.Equal(initialRevision, client.PresentationRevision);

        var place = GameInputEvent.PointerButtonChange(
            GameInputEventKind.PointerPressed, 0, 1, 160, 92, button: 1, clickCount: 1);
        client.HandleInput(place);
        var placedBase = Assert.Single(client.Overview.Bases);
        Assert.Equal("First Base", placedBase.Name);
        Assert.Equal(1, Assert.Single(placedBase.Facilities).SizeX);
        Assert.Equal(initialRevision + 1, client.PresentationRevision);

        var previous = client.Overview.Time;
        var advance = GameInputEvent.Key(
            GameInputEventKind.KeyPressed, 0, 1, 0, CampaignOverviewClient.AdvanceMinuteKey,
            InputKeyModifiers.None);
        client.HandleInput(advance);
        Assert.NotEqual(previous, client.Overview.Time);
        Assert.Equal(initialRevision + 2, client.PresentationRevision);
    }

    internal static RuntimeContent LoadFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "runtime-rule-linking");
        var discovery = ModDiscovery.ScanDirectory(fixture);
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("runtime-master", true), new ModActivation("runtime-addon", true)],
            "runtime-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        return ContentSnapshotBuilder.Build(plan).Content;
    }

    internal static NewCampaignRequest Request() => new(
        new CampaignId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
        "Campaign",
        "runtime-master",
        ["runtime-master", "runtime-addon"],
        CampaignDifficulty.Beginner);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    internal sealed class FixedClock : ICampaignClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);
    }
}
