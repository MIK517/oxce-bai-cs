using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Oxce.TestSupport;
using Oxce.UnitTests.Gameplay;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GeoscapeClientTests
{
    private static readonly TimeSpan Step = GeoscapeTimeController.GeoClock;

    [Fact]
    public void TimeWaitsForTheFirstBaseThenRunsAtTheSelectedSpeed()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Geoscape");
        var client = Client(campaign, content);
        Assert.False(client.TimeCanRun);
        var start = campaign.QueryOverview().Time;
        client.Tick(Step);
        Assert.Equal(start, campaign.QueryOverview().Time);

        // The base screens place the base; returning enters the world like GeoscapeState::init.
        Key(client, 'b');
        Assert.True(client.BasesOpen);
        var revision = client.PresentationRevision;
        Key(client, 'p');
        Key(client, 27);
        Assert.False(client.BasesOpen);
        Assert.True(client.PresentationRevision > revision);
        Assert.Equal(0, campaign.MonthsPassed);
        Assert.True(client.TimeCanRun);

        Key(client, '2');
        Assert.Equal(CampaignTimeSpeed.OneMinute, client.Time.Speed);
        var before = campaign.QueryOverview().Time;
        client.Tick(Step);
        var after = campaign.QueryOverview().Time;
        Assert.Equal((before.Minute + 1) % 60, after.Minute);
        Assert.Equal(before.Second, after.Second);
    }

    [Fact]
    public void PopupsQueueStopTimeAndApplyTheirButtons()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = Placed(content);
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(campaign.QueryOverview().Bases[0].Id, LogisticsOperation.Purchase)).Events)).Quote;
        var supply = quote.Rows.First(row => row.RuleId == "SUPPLY");
        Assert.IsType<LogisticsOrderCompleted>(campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 1)])).Events[0]);
        var client = Client(campaign, content);
        Key(client, '5');
        for (var step = 0; step < 200 && client.Popups.Count == 0; step++) client.Tick(Step);

        var popup = Assert.Single(client.Popups);
        Assert.Equal("STR_ITEMS_ARRIVING", popup.Title);
        Assert.False(client.TimeCanRun);
        var paused = campaign.QueryOverview().Time;
        for (var step = 0; step < 5; step++) client.Tick(Step);
        Assert.Equal(paused, campaign.QueryOverview().Time);

        // ItemsArrivingState binds OK to Esc; Enter belongs to "Go to base".
        Assert.IsType<BaseIntent>(Assert.Single(popup.Actions, action => action.OkKey).Intent);
        Key(client, 27);
        Assert.Empty(client.Popups);
        Assert.True(client.TimeCanRun);
        Assert.Equal(CampaignTimeSpeed.OneHour, client.Time.Speed);
        client.Tick(Step);
        Assert.NotEqual(paused, campaign.QueryOverview().Time);
    }

    [Fact]
    public void QuickSaveAndConfirmedQuickLoadOnlyFromTheGlobe()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = Placed(content);
        var saves = 0;
        var loads = 0;
        var client = Client(campaign, content, save: () => saves++, load: () =>
        {
            loads++;
            return CampaignUiSession.ForCampaign(campaign, campaign, campaign);
        });

        Key(client, 0x4000003E);
        Assert.Equal(1, saves);
        Key(client, 0x40000042);
        Assert.Equal("Load saved campaign?", Assert.Single(client.Popups).Title);
        // A popup takes the keys: F5 does not save behind it, Esc cancels the load.
        Key(client, 0x4000003E);
        Assert.Equal(1, saves);
        Key(client, 27);
        Assert.Equal(0, loads);
        Key(client, 0x40000042);
        Key(client, 13);
        Assert.Equal(1, loads);
        Assert.Equal("Campaign loaded.", client.Status);
    }

    [Fact]
    public void GlobeInputRotatesZoomsCentresAndSelects()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = Placed(content);
        var client = Client(campaign, content);
        var view = client.View;

        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 0x4000004F, InputKeyModifiers.None));
        client.Tick(TimeSpan.FromMilliseconds(30));
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyReleased, 0, 0, 0, 0x4000004F, InputKeyModifiers.None));
        client.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal(3 * 0.09, view.CenterLongitude, 12);

        client.HandleInput(GameInputEvent.PointerWheel(0, 0, 256, 200, 0, 1));
        Assert.Equal(1, view.Zoom);
        Key(client, '-');
        Assert.Equal(0, view.Zoom);
        Assert.True(view.TryUnproject(300, 200, out var longitude, out _));
        Click(client, 300, 200, 3);
        Assert.Equal(longitude, view.CenterLongitude, 12);

        // Speed buttons on the right.
        Click(client, 600, 64 + 18 * 3 + 5, 1);
        Assert.Equal(CampaignTimeSpeed.ThirtyMinutes, client.Time.Speed);

        var owner = client.World.Bases[0];
        view.Center(owner.Longitude, owner.Latitude);
        Key(client, '.');
        Assert.Equal(WorldTargetKind.Base, client.Selected!.Kind);
        // MultipleTargetsState::popupTarget opens a base's InterceptState.
        Key(client, 13);
        Assert.Equal("STR_LAUNCH_INTERCEPTION", Assert.Single(client.Popups).Title);
        Key(client, 27);
        Assert.Empty(client.Popups);
        var (x, y) = view.Project(owner.Longitude, owner.Latitude);
        Click(client, x, y, 1);
        Assert.Equal("STR_LAUNCH_INTERCEPTION", Assert.Single(client.Popups).Title);
    }

    [Fact]
    public void BaseScreensCannotAdvanceTimeAndFailuresStayInTheSession()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = Placed(content);
        var client = Client(campaign, content, save: () => throw new IOException("Disk full."),
            load: () => throw new FileNotFoundException("No quick save."));
        var start = campaign.QueryOverview().Time;
        Key(client, 'b');
        Key(client, ' ');
        Assert.Equal(start, campaign.QueryOverview().Time);
        client.Tick(Step);
        Assert.Equal(start, campaign.QueryOverview().Time);
        Key(client, 27);

        Key(client, 0x4000003E);
        Assert.Equal("Disk full.", client.Status);
        Key(client, 0x40000042);
        Key(client, 13);
        Assert.Equal("No quick save.", client.Status);
        Assert.Empty(client.Popups);
        Assert.False(client.ExitRequested);
    }

    [Fact]
    public void AnInternalTimeFailureHaltsTheClockInsteadOfEndingTheSession()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = Placed(content);
        var failing = new FailingCommands(campaign);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(), new Dictionary<int, IndexedSurface>(),
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        var client = new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, campaign, failing),
            content.RuntimeRules, assets, IndexedInterfaceFont.Create(), static text => text)
        {
            Stopwatch = static () => TimeSpan.Zero,
        });
        failing.Fail = true;
        client.Tick(Step);
        Assert.False(client.Time.Running);
        Assert.Contains("internal error", client.Time.HaltReason, StringComparison.Ordinal);
    }

    private sealed class FailingCommands(CampaignState campaign) : ICampaignCommandTarget
    {
        public bool Fail { get; set; }

        public CampaignCommandResult Execute(ICampaignCommand command) => Fail && command is AdvanceCampaignTime
            ? throw new InvalidOperationException("Monthly world preflight failed after calendar advancement.")
            : campaign.Execute(command);
    }

    private static CampaignState Placed(RuntimeContent content)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Geoscape");
        // A base at exactly (0, 0) is hidden like Base::getMarker does, so place it elsewhere.
        var site = campaign.QueryBaseSites(true).First(site => site.Longitude != 0 || site.Latitude != 0);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", site.Longitude, site.Latitude));
        return campaign;
    }

    private static GeoscapeClient Client(CampaignState campaign, RuntimeContent content, Action? save = null,
        Func<CampaignUiSession>? load = null)
    {
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        return new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            content.RuntimeRules, assets, IndexedInterfaceFont.Create(), static text => text)
        {
            Save = save,
            Load = load,
            Stopwatch = static () => TimeSpan.Zero,
        });
    }

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));

    private static void Click(GeoscapeClient client, int x, int y, byte button) =>
        client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, button, 1));
}
