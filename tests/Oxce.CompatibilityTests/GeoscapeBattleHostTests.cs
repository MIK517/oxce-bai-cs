using Oxce.Core.Graphics;
using Oxce.Engine;
using Oxce.Engine.Battlescape;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Gameplay.Sessions;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 3 S6b: the host loop opens a generated battle's screens above the geoscape, the screens cover the host clock, and
/// the turn-1 save of <c>BattlescapeState::init</c> is requested when the first turn starts.
/// </summary>
public sealed class GeoscapeBattleHostTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(80);

    private static GeoscapeClient Client(CampaignState campaign, List<CampaignSaveKind> saves, out CampaignSession host, out SessionHandle local)
    {
        host = new CampaignSession(campaign, campaign, static () => TimeSpan.Zero);
        local = host.Attach(PlayerId.Local);
        host.Controllers = new BattleUnitControllers(campaign.GetQuery<IBattleQuery>()!);
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        return new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, local, local),
            campaign.Content.RuntimeRules, assets, IndexedInterfaceFont.Create(), static text => text)
        {
            SaveRequested = saves.Add,
        });
    }

    private static CampaignState Briefing()
    {
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        Started(campaign);
        return campaign;
    }

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));

    [Fact]
    public void TheHostLoopOpensTheBriefingAboveTheGeoscapeAndTheScreensCoverTheClock()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        var client = Client(campaign, saves, out var host, out var local);
        using var session = local;
        Assert.Equal(0, client.Screens.Count);
        client.Tick(Step);
        Assert.IsType<BattleBriefingScreen>(client.Screens.Top);
        Assert.True(host.Clock.Covered);
        var time = campaign.QueryOverview().Time;
        for (var tick = 0; tick < 3; tick++) client.Tick(Step);
        Assert.Equal(time, campaign.QueryOverview().Time);
        // The same screen is not pushed again while it stays open.
        Assert.Equal(1, client.Screens.Count);
        Assert.Empty(saves);
    }

    [Fact]
    public void TheFirstTurnRequestsTheTurnOneSaveOnceAndTheBattlescapeFollows()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        client.Tick(Step);
        Key(client, 13);
        client.Tick(Step);
        Assert.IsType<BattleInventoryScreen>(client.Screens.Top);
        Assert.Empty(saves);
        Key(client, 27);
        Assert.IsType<BattlescapeClient>(client.Screens.Top);
        client.Tick(Step);
        client.Tick(Step);
        Assert.Equal([CampaignSaveKind.BattleTurnOne], saves);
    }

    [Fact]
    public void ABattleLoadedRunningOpensTheBattlescapeWithoutTheTurnOneSave()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        campaign.Execute(new ConfirmBriefing());
        campaign.Execute(new StartFirstTurn());
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        client.Tick(Step);
        Assert.IsType<BattlescapeClient>(client.Screens.Top);
        Assert.Empty(saves);
    }

    [Fact]
    public void ADeploymentRequestAsksToStartTheBattleAndOkStartsIt()
    {
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        var popup = GeoscapeNotifications.DescribeAll(new DeploymentRequested(request), campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld(),
            static text => text, campaign.Content.RuntimeRules, 0).Single();
        var ok = Assert.Single(popup.Actions);
        Assert.IsType<StartBattle>(ok.Command);
        Assert.True(ok.OkKey);
    }

    private static CampaignState Requesting()
    {
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        return campaign;
    }

    [Fact]
    public void OkOnTheStartPopupStartsTheBattleAndTheBriefingCarriesTheRequestsLines()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Requesting();
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        Assert.Equal("Start the battle", Assert.Single(client.Popups).Title);
        Key(client, 13);
        Assert.Empty(client.Popups);
        Assert.NotNull(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));
        client.Tick(Step);
        Assert.IsType<BattleBriefingScreen>(client.Screens.Top);
        var briefing = campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        // BriefingState names the craft that landed, or the base; the request carried one of them.
        Assert.NotEqual("", briefing.CraftOrBaseName);
    }

    [Fact]
    public void ARefusedStartShowsTheReasonAndOffersTheRequestAgain()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Requesting();
        campaign.BattleTerrainFiles = null;
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        Key(client, 13);
        // The refusal comes first; once read, the start popup returns because the request still holds the clock.
        Assert.NotEqual("Start the battle", client.Popups[0].Title);
        Key(client, 13);
        Assert.Equal("Start the battle", Assert.Single(client.Popups).Title);
        Assert.Null(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));
    }

    [Theory]
    [InlineData(CampaignSaveKind.Ironman, false, false, "save.sav")]
    [InlineData(CampaignSaveKind.Autosave, true, false, "_autogeo_.asav")]
    [InlineData(CampaignSaveKind.BattleTurnOne, true, true, "save.sav")]
    [InlineData(CampaignSaveKind.BattleTurnOne, false, true, "_autobattle_.asav")]
    [InlineData(CampaignSaveKind.BattleTurnOne, false, false, null)]
    public void TheSavePolicyMapsEachRequestToItsFile(CampaignSaveKind kind, bool ironman, bool autosave, string? file)
    {
        var path = Path.Combine(Path.GetTempPath(), "policy", "save.sav");
        var expected = file is null ? null : Path.Combine(Path.GetDirectoryName(path)!, file);
        Assert.Equal(expected, BattleSavePolicy.PathFor(kind, ironman, autosave, path));
    }
}
