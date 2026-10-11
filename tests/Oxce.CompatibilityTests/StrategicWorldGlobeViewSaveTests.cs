using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>SavedGame globeLon/globeLat/globeZoom, and quick save and load from the geoscape.</summary>
public sealed class StrategicWorldGlobeViewSaveTests
{
    [Fact]
    public void SavesCarryTheGlobeViewAndRewritesKeepOrReplaceIt()
    {
        var content = StrategicReadinessTestContent.Load();
        var snapshot = TestFixtures.CreateLogisticsCampaign(content, "Globe view").Capture();

        // SavedGame::save always writes the view; a new campaign starts at the reference defaults.
        var fresh = OxceSaveAdapter.EmitNewCampaign(snapshot);
        Assert.Contains("globeLon: 0", fresh, StringComparison.Ordinal);
        Assert.Contains("globeZoom: 0", fresh, StringComparison.Ordinal);
        Assert.Equal(new OxceGlobeView(0, 0, 0), TestFixtures.LoadLogisticsSave(fresh, content, seed: 1, name: "fresh.sav").Source.GlobeView);

        var viewed = OxceSaveAdapter.EmitNewCampaign(snapshot,
            new OxceSaveWriteOptions { GlobeView = new OxceGlobeView(1.25, -0.5, 3) });
        var loaded = TestFixtures.LoadLogisticsSave(viewed, content, seed: 1, name: "viewed.sav");
        Assert.Equal(new OxceGlobeView(1.25, -0.5, 3), loaded.Source.GlobeView);
        Assert.Equivalent(snapshot, loaded.Campaign.Capture(), strict: true);

        var kept = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.Equal(new OxceGlobeView(1.25, -0.5, 3), TestFixtures.LoadLogisticsSave(kept, content, seed: 1, name: "kept.sav").Source.GlobeView);
        var moved = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source,
            new OxceSaveWriteOptions { GlobeView = new OxceGlobeView(4.0, 0.25, 1) });
        Assert.Equal(new OxceGlobeView(4.0, 0.25, 1), TestFixtures.LoadLogisticsSave(moved, content, seed: 1, name: "moved.sav").Source.GlobeView);
        Assert.Throws<InvalidDataException>(() => OxceSaveAdapter.EmitNewCampaign(snapshot,
            new OxceSaveWriteOptions { GlobeView = new OxceGlobeView(double.NaN, 0, 0) }));
    }

    [Fact]
    public void QuickSaveAndLoadCarryTheViewAndResetTheClock()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CampaignState.Restore(StrategicWorldTestSupport.CreateSnapshot(content, "Globe view"), content,
            new SplitMix64RandomSource(5));
        string? saved = null;
        GeoscapeClient? client = null;
        var loads = 0;
        client = Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign) with
        {
            GlobeView = new GlobeViewState(2.0, 0.4, 2),
        }, static () => TimeSpan.Zero, save: () =>
        {
            var view = client!.CurrentView;
            saved = OxceSaveAdapter.EmitNewCampaign(campaign.Capture(),
                new OxceSaveWriteOptions { GlobeView = new(view.Longitude, view.Latitude, view.Zoom) });
        }, load: () =>
        {
            loads++;
            var loaded = TestFixtures.LoadLogisticsSave(saved!, content, seed: 5, name: "quick.sav");
            var view = loaded.Source.GlobeView;
            return CampaignUiSession.ForCampaign(loaded.Campaign, loaded.Campaign, loaded.Campaign) with
            {
                GlobeView = new GlobeViewState(view.Longitude, view.Latitude, view.Zoom),
            };
        });
        // Globe's constructor takes the saved centre and zoom.
        Assert.Equal(new GlobeViewState(2.0, 0.4, 2), client.CurrentView);

        Key(client, 0x4000003E);
        Assert.Equal("Campaign saved.", client.Status);
        client.View.Center(0.5, 0.1);
        client.View.SetZoom(0);

        Key(client, '4');
        Key(client, 0x40000042);
        Key(client, 13);
        Assert.Equal(1, loads);
        Assert.Equal(0, client.Time.Owed);
        Assert.Equal(CampaignTimeSpeed.FiveSeconds, client.Time.Speed);
        Assert.Equal(new GlobeViewState(2.0, 0.4, 2), client.CurrentView);
    }

    private static GeoscapeClient Client(RuntimeContent content, CampaignUiSession session, Func<TimeSpan> stopwatch,
        Action save, Func<CampaignUiSession> load)
    {
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        return new GeoscapeClient(new GeoscapeClientSetup(session, content.RuntimeRules, assets,
            IndexedInterfaceFont.Create(), static text => text)
        {
            Stopwatch = stopwatch,
            Save = save,
            Load = load,
        });
    }

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));
}
